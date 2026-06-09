using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using MathNet.Numerics.Statistics;

namespace AntennaMonitoring.Algorithms;

public class RandomForestDiagnosis : IHealthDiagnosis
{
    public string ModelName => "RandomForest";
    private readonly DiagnosisOptions _options;
    private readonly List<DecisionTree> _forest;
    private const int TreeCount = 50;
    private const int MinSamplesPerLeaf = 5;
    private const int MaxDepth = 10;

    public RandomForestDiagnosis(Microsoft.Extensions.Options.IOptions<DiagnosisOptions> options)
    {
        _options = options.Value;
        _forest = new List<DecisionTree>();
        InitializeForest();
    }

    private void InitializeForest()
    {
        for (int i = 0; i < TreeCount; i++)
        {
            var tree = new DecisionTree(MaxDepth, MinSamplesPerLeaf, i);
            tree.Train(GenerateTrainingData());
            _forest.Add(tree);
        }
    }

    private List<(DiagnosisFeature Features, double FailureProbability)> GenerateTrainingData()
    {
        var trainingData = new List<(DiagnosisFeature, double)>();
        var random = new Random(42);

        for (int i = 0; i < 2000; i++)
        {
            var features = new DiagnosisFeature
            {
                SwrMean = random.NextDouble() * 4.0 + 1.0,
                SwrStd = random.NextDouble() * 0.5,
                SwrMax = random.NextDouble() * 6.0 + 1.0,
                SwrTrend = (random.NextDouble() - 0.5) * 0.5,
                TempMean = random.NextDouble() * 40.0 + 30.0,
                TempStd = random.NextDouble() * 3.0,
                TempMax = random.NextDouble() * 50.0 + 35.0,
                TempTrend = (random.NextDouble() - 0.5) * 2.0,
                AmpMean = random.NextDouble() * 0.5 + 0.8,
                AmpStd = random.NextDouble() * 0.1,
                PhaseMean = (random.NextDouble() - 0.5) * 0.5,
                PhaseStd = random.NextDouble() * 0.2,
                TxPowerMean = random.NextDouble() * 5.0 + 40.0,
                RxPowerMean = random.NextDouble() * 10.0 - 65.0,
                BerMean = random.NextDouble() * 0.001
            };

            double failureProb = CalculateFailureProbabilityFromFeatures(features);
            trainingData.Add((features, failureProb));
        }

        return trainingData;
    }

    private double CalculateFailureProbabilityFromFeatures(DiagnosisFeature f)
    {
        double swrScore = Math.Min(1.0, (f.SwrMean - 1.0) / 3.0) * 0.4;
        double tempScore = Math.Min(1.0, (f.TempMean - 35.0) / 35.0) * 0.3;
        double trendScore = Math.Max(0, f.SwrTrend) * 0.15 + Math.Max(0, f.TempTrend) * 0.15;

        if (f.SwrMax > 2.0) swrScore += 0.2;
        if (f.TempMax > 75.0) tempScore += 0.15;
        if (f.BerMean > 0.0001) trendScore += 0.1;

        return Math.Min(1.0, swrScore + tempScore + trendScore);
    }

    public async Task<DiagnosisResult> DiagnoseAsync(
        Guid stationId,
        Channel channel,
        IEnumerable<ChannelMetrics> historicalMetrics,
        CancellationToken cancellationToken)
    {
        var metricsList = historicalMetrics.ToList();
        var result = new DiagnosisResult
        {
            ModelType = ModelName,
            ChannelId = channel.Id,
            ChannelIndex = channel.ChannelIndex,
            PredictionHorizonHours = _options.PredictionHorizonHours,
            DiagnosisTime = DateTime.UtcNow
        };

        if (metricsList.Count == 0)
        {
            result.FailureProbability = (double)channel.FailureProbability;
            result.Success = true;
            result.Recommendation = "Insufficient historical data for diagnosis";
            result.HealthScore = 100 - result.FailureProbability * 100;
            return result;
        }

        var latest = metricsList.OrderByDescending(m => m.Timestamp).First();
        result.SwrValue = latest.Swr;
        result.TemperatureValue = latest.PaTemperature;

        var features = ExtractFeatures(metricsList);
        result.FailureProbability = PredictFailureProbability(features);

        var (swrPred, tempPred) = PredictNextValues(metricsList, _options.PredictionHorizonHours);
        result.SwrPredicted = swrPred;
        result.TemperaturePredicted = tempPred;

        result.AnomalyScore = CalculateAnomalyScore(metricsList, features);
        result.PredictedFailureHours = PredictTimeToFailure(result.FailureProbability, features);
        result.HealthScore = CalculateHealthScore(result.FailureProbability, result.AnomalyScore);
        result.Recommendation = GenerateRecommendation(result);

        result.Success = true;
        return await Task.FromResult(result);
    }

    public async Task<IEnumerable<DiagnosisResult>> DiagnoseBatchAsync(
        Guid stationId,
        IEnumerable<Channel> channels,
        IEnumerable<ChannelMetrics> historicalMetrics,
        CancellationToken cancellationToken)
    {
        var results = new List<DiagnosisResult>();
        var metricsList = historicalMetrics.ToList();

        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channelMetrics = metricsList.Where(m => m.ChannelId == channel.Id.ToString());
            var result = await DiagnoseAsync(stationId, channel, channelMetrics, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private DiagnosisFeature ExtractFeatures(List<ChannelMetrics> metrics)
    {
        var swrValues = metrics.Select(m => m.Swr).ToArray();
        var tempValues = metrics.Select(m => m.PaTemperature).ToArray();
        var ampValues = metrics.Select(m => m.Amplitude).ToArray();
        var phaseValues = metrics.Select(m => m.Phase).ToArray();
        var txValues = metrics.Select(m => m.TxPower).ToArray();
        var rxValues = metrics.Select(m => m.RxPower).ToArray();
        var berValues = metrics.Select(m => m.Ber).ToArray();

        var timestamps = metrics.Select(m => m.Timestamp.ToOADate()).ToArray();

        return new DiagnosisFeature
        {
            SwrMean = swrValues.Mean(),
            SwrStd = swrValues.StandardDeviation(),
            SwrMax = swrValues.Max(),
            SwrTrend = CalculateTrend(timestamps, swrValues),
            TempMean = tempValues.Mean(),
            TempStd = tempValues.StandardDeviation(),
            TempMax = tempValues.Max(),
            TempTrend = CalculateTrend(timestamps, tempValues),
            AmpMean = ampValues.Mean(),
            AmpStd = ampValues.StandardDeviation(),
            PhaseMean = phaseValues.Mean(),
            PhaseStd = phaseValues.StandardDeviation(),
            TxPowerMean = txValues.Mean(),
            RxPowerMean = rxValues.Mean(),
            BerMean = berValues.Mean()
        };
    }

    private double CalculateTrend(double[] x, double[] y)
    {
        if (x.Length < 2) return 0;

        double meanX = x.Mean();
        double meanY = y.Mean();

        double numerator = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
        double denominator = x.Sum(xi => (xi - meanX) * (xi - meanX));

        return denominator == 0 ? 0 : numerator / denominator;
    }

    private double PredictFailureProbability(DiagnosisFeature features)
    {
        double[] predictions = new double[TreeCount];
        for (int i = 0; i < TreeCount; i++)
        {
            predictions[i] = _forest[i].Predict(features);
        }
        return predictions.Mean();
    }

    private (double Swr, double Temp) PredictNextValues(List<ChannelMetrics> metrics, int hours)
    {
        var recent = metrics.OrderByDescending(m => m.Timestamp).Take(24).ToList();
        if (recent.Count < 2) return (1.2, 40.0);

        var swrTrend = CalculateTrend(recent.Select(m => m.Timestamp.ToOADate()).ToArray(),
                                       recent.Select(m => m.Swr).ToArray());
        var tempTrend = CalculateTrend(recent.Select(m => m.Timestamp.ToOADate()).ToArray(),
                                        recent.Select(m => m.PaTemperature).ToArray());

        double lastSwr = recent.First().Swr;
        double lastTemp = recent.First().PaTemperature;

        double predictedSwr = lastSwr + swrTrend * hours * 0.5;
        double predictedTemp = lastTemp + tempTrend * hours * 0.5;

        return (Math.Clamp(predictedSwr, 1.0, 10.0), Math.Clamp(predictedTemp, 25.0, 90.0));
    }

    private double CalculateAnomalyScore(List<ChannelMetrics> metrics, DiagnosisFeature features)
    {
        double swrZScore = Math.Abs((features.SwrMean - 1.15) / Math.Max(features.SwrStd, 0.01));
        double tempZScore = Math.Abs((features.TempMean - 45.0) / Math.Max(features.TempStd, 0.01));
        double ampZScore = Math.Abs((features.AmpMean - 1.0) / Math.Max(features.AmpStd, 0.01));

        return Math.Min(1.0, (swrZScore + tempZScore + ampZScore) / 15.0);
    }

    private int PredictTimeToFailure(double failureProb, DiagnosisFeature features)
    {
        if (failureProb < 0.3) return 168;
        if (failureProb < 0.5) return 72;
        if (failureProb < 0.7) return 24;
        if (failureProb < 0.85) return 12;
        return 6;
    }

    private double CalculateHealthScore(double failureProb, double anomalyScore)
    {
        double score = 100 - (failureProb * 80 + anomalyScore * 20);
        return Math.Clamp(score, 0, 100);
    }

    private string GenerateRecommendation(DiagnosisResult result)
    {
        if (result.FailureProbability >= _options.FailureProbabilityThreshold)
        {
            return $"CRITICAL: Channel {result.ChannelIndex} has high failure probability ({result.FailureProbability:P1}). " +
                   $"Predicted failure in {result.PredictedFailureHours}h. Immediate maintenance required.";
        }
        if (result.FailureProbability >= 0.5)
        {
            return $"WARNING: Channel {result.ChannelIndex} shows degradation signs. " +
                   $"SWR: {result.SwrValue:F2}, Temp: {result.TemperatureValue:F1}°C. Schedule preventive maintenance.";
        }
        if (result.SwrValue >= _options.SWRAlarmThreshold)
        {
            return $"ATTENTION: Channel {result.ChannelIndex} SWR ({result.SwrValue:F2}) exceeds threshold. Check antenna connections.";
        }
        if (result.TemperatureValue > 75.0)
        {
            return $"WARNING: Channel {result.ChannelIndex} PA temperature ({result.TemperatureValue:F1}°C) is high. Check cooling system.";
        }
        return $"Channel {result.ChannelIndex} operating normally. Health score: {result.HealthScore:F1}/100";
    }
}

public class DecisionTree
{
    private readonly int _maxDepth;
    private readonly int _minSamplesPerLeaf;
    private readonly Random _random;
    private TreeNode? _root;

    public DecisionTree(int maxDepth, int minSamplesPerLeaf, int seed)
    {
        _maxDepth = maxDepth;
        _minSamplesPerLeaf = minSamplesPerLeaf;
        _random = new Random(seed);
    }

    public void Train(List<(DiagnosisFeature Features, double Label)> trainingData)
    {
        _root = BuildTree(trainingData, 0);
    }

    private TreeNode BuildTree(List<(DiagnosisFeature Features, double Label)> data, int depth)
    {
        if (depth >= _maxDepth || data.Count <= _minSamplesPerLeaf)
        {
            return new TreeNode
            {
                IsLeaf = true,
                Prediction = data.Select(d => d.Label).Average()
            };
        }

        var (featureIndex, threshold, gain) = FindBestSplit(data);

        if (gain < 0.01)
        {
            return new TreeNode
            {
                IsLeaf = true,
                Prediction = data.Select(d => d.Label).Average()
            };
        }

        var left = data.Where(d => GetFeatureValue(d.Features, featureIndex) <= threshold).ToList();
        var right = data.Where(d => GetFeatureValue(d.Features, featureIndex) > threshold).ToList();

        if (left.Count == 0 || right.Count == 0)
        {
            return new TreeNode
            {
                IsLeaf = true,
                Prediction = data.Select(d => d.Label).Average()
            };
        }

        return new TreeNode
        {
            IsLeaf = false,
            FeatureIndex = featureIndex,
            Threshold = threshold,
            Left = BuildTree(left, depth + 1),
            Right = BuildTree(right, depth + 1)
        };
    }

    private (int FeatureIndex, double Threshold, double Gain) FindBestSplit(
        List<(DiagnosisFeature Features, double Label)> data)
    {
        int bestFeature = 0;
        double bestThreshold = 0;
        double bestGain = -1;

        int[] candidateFeatures = Enumerable.Range(0, 15)
                                            .OrderBy(_ => _random.Next())
                                            .Take(8)
                                            .ToArray();

        foreach (var featureIdx in candidateFeatures)
        {
            var values = data.Select(d => GetFeatureValue(d.Features, featureIdx)).OrderBy(v => v).ToList();
            var thresholds = values.Distinct().Skip(1).Take(10).ToArray();

            foreach (var threshold in thresholds)
            {
                double gain = CalculateInformationGain(data, featureIdx, threshold);
                if (gain > bestGain)
                {
                    bestGain = gain;
                    bestFeature = featureIdx;
                    bestThreshold = threshold;
                }
            }
        }

        return (bestFeature, bestThreshold, bestGain);
    }

    private double CalculateInformationGain(
        List<(DiagnosisFeature Features, double Label)> data,
        int featureIndex, double threshold)
    {
        var left = data.Where(d => GetFeatureValue(d.Features, featureIndex) <= threshold).ToList();
        var right = data.Where(d => GetFeatureValue(d.Features, featureIndex) > threshold).ToList();

        if (left.Count == 0 || right.Count == 0) return 0;

        double parentVariance = CalculateVariance(data.Select(d => d.Label));
        double leftVariance = CalculateVariance(left.Select(d => d.Label));
        double rightVariance = CalculateVariance(right.Select(d => d.Label));

        double weightedChildVariance = (left.Count * leftVariance + right.Count * rightVariance) / data.Count;
        return parentVariance - weightedChildVariance;
    }

    private double CalculateVariance(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return 0;
        double mean = list.Average();
        return list.Sum(v => (v - mean) * (v - mean)) / list.Count;
    }

    private double GetFeatureValue(DiagnosisFeature f, int index)
    {
        return index switch
        {
            0 => f.SwrMean,
            1 => f.SwrStd,
            2 => f.SwrMax,
            3 => f.SwrTrend,
            4 => f.TempMean,
            5 => f.TempStd,
            6 => f.TempMax,
            7 => f.TempTrend,
            8 => f.AmpMean,
            9 => f.AmpStd,
            10 => f.PhaseMean,
            11 => f.PhaseStd,
            12 => f.TxPowerMean,
            13 => f.RxPowerMean,
            14 => f.BerMean,
            _ => 0
        };
    }

    public double Predict(DiagnosisFeature features)
    {
        return Predict(_root, features);
    }

    private double Predict(TreeNode? node, DiagnosisFeature features)
    {
        if (node == null) return 0.5;
        if (node.IsLeaf) return node.Prediction;

        double value = GetFeatureValue(features, node.FeatureIndex);
        if (value <= node.Threshold)
            return Predict(node.Left, features);
        else
            return Predict(node.Right, features);
    }
}

public class TreeNode
{
    public bool IsLeaf { get; set; }
    public int FeatureIndex { get; set; }
    public double Threshold { get; set; }
    public double Prediction { get; set; }
    public TreeNode? Left { get; set; }
    public TreeNode? Right { get; set; }
}
