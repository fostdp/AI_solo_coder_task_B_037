using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;

namespace AntennaMonitoring.Algorithms;

public class LSTMDiagnosis : IHealthDiagnosis
{
    public string ModelName => "LSTM";
    private readonly DiagnosisOptions _options;
    private readonly LSTMNetwork _lstm;
    private const int SequenceLength = 24;
    private const int HiddenSize = 32;
    private const int InputSize = 6;
    private const int OutputSize = 1;

    public LSTMDiagnosis(Microsoft.Extensions.Options.IOptions<DiagnosisOptions> options)
    {
        _options = options.Value;
        _lstm = new LSTMNetwork(InputSize, HiddenSize, OutputSize, 42);
        TrainModel();
    }

    private void TrainModel()
    {
        var trainingData = GenerateTrainingSequences();
        _lstm.Train(trainingData, epochs: 100, learningRate: 0.01);
    }

    private List<(double[][] Sequence, double Label)> GenerateTrainingSequences()
    {
        var sequences = new List<(double[][], double)>();
        var random = new Random(123);

        for (int i = 0; i < 500; i++)
        {
            var sequence = GenerateRandomSequence(random, SequenceLength, failure: false);
            sequences.Add((sequence, 0.0));
        }

        for (int i = 0; i < 500; i++)
        {
            var sequence = GenerateRandomSequence(random, SequenceLength, failure: true);
            sequences.Add((sequence, 1.0));
        }

        return sequences;
    }

    private double[][] GenerateRandomSequence(Random random, int length, bool failure)
    {
        var sequence = new double[length][];
        double swrBase = failure ? 1.8 : 1.1;
        double tempBase = failure ? 65.0 : 42.0;
        double swrTrend = failure ? 0.02 : 0.001;
        double tempTrend = failure ? 0.3 : 0.05;

        for (int t = 0; t < length; t++)
        {
            double swr = swrBase + t * swrTrend + random.NextDouble() * 0.2;
            double temp = tempBase + t * tempTrend + random.NextDouble() * 2.0;
            double amp = 1.0 + random.NextDouble() * 0.1 - 0.05;
            double phase = random.NextDouble() * 0.2 - 0.1;
            double tx = 43.0 + random.NextDouble() * 1.0 - 0.5;
            double rx = -60.0 + random.NextDouble() * 3.0 - 1.5;

            if (failure && t > length * 0.7)
            {
                swr += 0.5 * (t - length * 0.7) * 0.1;
                temp += 2.0 * (t - length * 0.7) * 0.1;
            }

            sequence[t] = new[] { swr, temp, amp, phase, tx, rx };
        }

        return sequence;
    }

    public async Task<DiagnosisResult> DiagnoseAsync(
        Guid stationId,
        Channel channel,
        IEnumerable<ChannelMetrics> historicalMetrics,
        CancellationToken cancellationToken)
    {
        var metricsList = historicalMetrics.OrderBy(m => m.Timestamp).ToList();
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
            result.Recommendation = "Insufficient historical data for LSTM diagnosis";
            result.HealthScore = 100 - result.FailureProbability * 100;
            return result;
        }

        var latest = metricsList.Last();
        result.SwrValue = latest.Swr;
        result.TemperatureValue = latest.PaTemperature;

        var sequence = BuildSequence(metricsList);
        result.FailureProbability = _lstm.Predict(sequence);

        var (swrPred, tempPred) = PredictTimeSeries(metricsList, _options.PredictionHorizonHours);
        result.SwrPredicted = swrPred;
        result.TemperaturePredicted = tempPred;

        result.AnomalyScore = CalculateReconstructionError(sequence);
        result.PredictedFailureHours = EstimateTimeToFailure(metricsList, result.FailureProbability);
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

    private double[][] BuildSequence(List<ChannelMetrics> metrics)
    {
        var sequence = new double[SequenceLength][];
        int available = Math.Min(metrics.Count, SequenceLength);
        int startIndex = metrics.Count - available;

        for (int i = 0; i < SequenceLength; i++)
        {
            int idx = i < available ? startIndex + i : startIndex + available - 1;
            var m = metrics[idx];
            sequence[i] = new[] { m.Swr, m.PaTemperature, m.Amplitude, m.Phase, m.TxPower, m.RxPower };
        }

        return sequence;
    }

    private (double Swr, double Temp) PredictTimeSeries(List<ChannelMetrics> metrics, int hours)
    {
        int steps = Math.Min(metrics.Count, 48);
        if (steps < 5) return (1.2, 40.0);

        var recent = metrics.Skip(metrics.Count - steps).ToList();

        var swrValues = recent.Select(m => m.Swr).ToArray();
        var tempValues = recent.Select(m => m.PaTemperature).ToArray();
        var times = recent.Select((m, i) => (double)i).ToArray();

        double swrSlope = LinearRegression(times, swrValues);
        double tempSlope = LinearRegression(times, tempValues);

        double lastSwr = swrValues.Last();
        double lastTemp = tempValues.Last();

        double predictedSwr = lastSwr + swrSlope * hours;
        double predictedTemp = lastTemp + tempSlope * hours;

        return (Math.Clamp(predictedSwr, 1.0, 10.0), Math.Clamp(predictedTemp, 25.0, 90.0));
    }

    private double LinearRegression(double[] x, double[] y)
    {
        if (x.Length < 2) return 0;
        double meanX = x.Average();
        double meanY = y.Average();
        double num = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
        double den = x.Sum(xi => (xi - meanX) * (xi - meanX));
        return den == 0 ? 0 : num / den;
    }

    private double CalculateReconstructionError(double[][] sequence)
    {
        int n = sequence.Length;
        double totalError = 0;

        for (int i = 1; i < n; i++)
        {
            for (int f = 0; f < InputSize; f++)
            {
                double diff = sequence[i][f] - sequence[i - 1][f];
                totalError += diff * diff;
            }
        }

        return Math.Min(1.0, totalError / (n * InputSize * 100));
    }

    private int EstimateTimeToFailure(List<ChannelMetrics> metrics, double failureProb)
    {
        if (metrics.Count < 2) return 72;

        var recent = metrics.TakeLast(12).ToList();
        double avgSwrIncrease = 0;

        for (int i = 1; i < recent.Count; i++)
        {
            avgSwrIncrease += recent[i].Swr - recent[i - 1].Swr;
        }
        avgSwrIncrease /= Math.Max(1, recent.Count - 1);

        double currentSwr = recent.Last().Swr;
        double distanceToFailure = 3.0 - currentSwr;

        if (avgSwrIncrease <= 0)
        {
            return failureProb < 0.5 ? 168 : 24;
        }

        int hoursToFailure = (int)(distanceToFailure / avgSwrIncrease * 5);
        return Math.Clamp(hoursToFailure, 1, 168);
    }

    private double CalculateHealthScore(double failureProb, double anomalyScore)
    {
        double score = 100 - (failureProb * 70 + anomalyScore * 30);
        return Math.Clamp(score, 0, 100);
    }

    private string GenerateRecommendation(DiagnosisResult result)
    {
        if (result.FailureProbability >= _options.FailureProbabilityThreshold)
        {
            return $"LSTM CRITICAL: Channel {result.ChannelIndex} failure probability {result.FailureProbability:P1}. " +
                   $"Predicted SWR: {result.SwrPredicted:F2}, Temp: {result.TemperaturePredicted:F1}°C. " +
                   $"Expected failure in {result.PredictedFailureHours}h. Immediate action required!";
        }
        if (result.FailureProbability >= 0.5)
        {
            return $"LSTM WARNING: Channel {result.ChannelIndex} degradation detected. " +
                   $"SWR trend: {result.SwrValue:F2} → {result.SwrPredicted:F2}. Schedule maintenance within 48h.";
        }
        if (result.AnomalyScore > 0.5)
        {
            return $"LSTM ANOMALY: Channel {result.ChannelIndex} shows unusual patterns (score: {result.AnomalyScore:F2}). " +
                   $"Investigate potential intermittent issues.";
        }
        return $"LSTM: Channel {result.ChannelIndex} stable. Health: {result.HealthScore:F1}/100. " +
               $"Predicted 24h: SWR={result.SwrPredicted:F2}, Temp={result.TemperaturePredicted:F1}°C";
    }
}

public class LSTMNetwork
{
    private readonly int _inputSize;
    private readonly int _hiddenSize;
    private readonly int _outputSize;
    private readonly Random _random;

    private Matrix<double> _Wf, _Wi, _Wc, _Wo;
    private Matrix<double> _Uf, _Ui, _Uc, _Uo;
    private Vector<double> _bf, _bi, _bc, _bo;
    private Matrix<double> _Wy;
    private Vector<double> _by;

    public LSTMNetwork(int inputSize, int hiddenSize, int outputSize, int seed)
    {
        _inputSize = inputSize;
        _hiddenSize = hiddenSize;
        _outputSize = outputSize;
        _random = new Random(seed);
        InitializeWeights();
    }

    private void InitializeWeights()
    {
        double scale = 0.1;

        _Wf = CreateRandomMatrix(_hiddenSize, _inputSize, scale);
        _Wi = CreateRandomMatrix(_hiddenSize, _inputSize, scale);
        _Wc = CreateRandomMatrix(_hiddenSize, _inputSize, scale);
        _Wo = CreateRandomMatrix(_hiddenSize, _inputSize, scale);

        _Uf = CreateRandomMatrix(_hiddenSize, _hiddenSize, scale);
        _Ui = CreateRandomMatrix(_hiddenSize, _hiddenSize, scale);
        _Uc = CreateRandomMatrix(_hiddenSize, _hiddenSize, scale);
        _Uo = CreateRandomMatrix(_hiddenSize, _hiddenSize, scale);

        _bf = Vector<double>.Build.Dense(_hiddenSize, 1.0);
        _bi = Vector<double>.Build.Dense(_hiddenSize, 0.0);
        _bc = Vector<double>.Build.Dense(_hiddenSize, 0.0);
        _bo = Vector<double>.Build.Dense(_hiddenSize, 0.0);

        _Wy = CreateRandomMatrix(_outputSize, _hiddenSize, scale);
        _by = Vector<double>.Build.Dense(_outputSize, 0.0);
    }

    private Matrix<double> CreateRandomMatrix(int rows, int cols, double scale)
    {
        var matrix = Matrix<double>.Build.Dense(rows, cols);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                matrix[i, j] = (_random.NextDouble() * 2 - 1) * scale;
        return matrix;
    }

    public void Train(List<(double[][] Sequence, double Label)> trainingData, int epochs, double learningRate)
    {
        for (int epoch = 0; epoch < epochs; epoch++)
        {
            double totalLoss = 0;
            var shuffled = trainingData.OrderBy(_ => _random.Next()).ToList();

            foreach (var (sequence, label) in shuffled)
            {
                var (loss, grads) = Backpropagate(sequence, label);
                totalLoss += loss;
                ApplyGradients(grads, learningRate);
            }
        }
    }

    private (double Loss, Gradients Grads) Backpropagate(double[][] sequence, double label)
    {
        int T = sequence.Length;
        var h = new Vector<double>[T + 1];
        var c = new Vector<double>[T + 1];
        var f = new Vector<double>[T];
        var i = new Vector<double>[T];
        var c_tilde = new Vector<double>[T];
        var o = new Vector<double>[T];
        var x = new Vector<double>[T];

        h[0] = Vector<double>.Build.Dense(_hiddenSize, 0);
        c[0] = Vector<double>.Build.Dense(_hiddenSize, 0);

        for (int t = 0; t < T; t++)
        {
            x[t] = Vector<double>.Build.DenseOfArray(sequence[t]);

            f[t] = Sigmoid(_Wf * x[t] + _Uf * h[t] + _bf);
            i[t] = Sigmoid(_Wi * x[t] + _Ui * h[t] + _bi);
            c_tilde[t] = Tanh(_Wc * x[t] + _Uc * h[t] + _bc);
            c[t + 1] = f[t].PointwiseMultiply(c[t]) + i[t].PointwiseMultiply(c_tilde[t]);
            o[t] = Sigmoid(_Wo * x[t] + _Uo * h[t] + _bo);
            h[t + 1] = o[t].PointwiseMultiply(Tanh(c[t + 1]));
        }

        var y = Sigmoid(_Wy * h[T] + _by);
        double loss = -label * Math.Log(y[0] + 1e-10) - (1 - label) * Math.Log(1 - y[0] + 1e-10);

        var grads = new Gradients(_inputSize, _hiddenSize, _outputSize);

        var dy = y - Vector<double>.Build.Dense(new[] { label });
        grads.dWy += dy.OuterProduct(h[T]);
        grads.dby += dy;

        var dh = _Wy.Transpose() * dy;

        for (int t = T - 1; t >= 0; t--)
        {
            var dc = dh.PointwiseMultiply(o[t]).PointwiseMultiply(Dtanh(c[t + 1]));

            var df = dc.PointwiseMultiply(c[t]).PointwiseMultiply(Dsigmoid(f[t]));
            var di = dc.PointwiseMultiply(c_tilde[t]).PointwiseMultiply(Dsigmoid(i[t]));
            var dc_tilde = dc.PointwiseMultiply(i[t]).PointwiseMultiply(Dtanh(c_tilde[t]));
            var d o = dh.PointwiseMultiply(Tanh(c[t + 1])).PointwiseMultiply(Dsigmoid(o[t]));

            grads.dWf += df.OuterProduct(x[t]);
            grads.dWi += di.OuterProduct(x[t]);
            grads.dWc += dc_tilde.OuterProduct(x[t]);
            grads.dWo += d_o.OuterProduct(x[t]);

            grads.dUf += df.OuterProduct(h[t]);
            grads.dUi += di.OuterProduct(h[t]);
            grads.dUc += dc_tilde.OuterProduct(h[t]);
            grads.dUo += d_o.OuterProduct(h[t]);

            grads.dbf += df;
            grads.dbi += di;
            grads.dbc += dc_tilde;
            grads.dbo += d_o;

            dh = _Uf.Transpose() * df + _Ui.Transpose() * di +
                 _Uc.Transpose() * dc_tilde + _Uo.Transpose() * d_o;
        }

        return (loss, grads);
    }

    private void ApplyGradients(Gradients grads, double lr)
    {
        _Wf -= lr * grads.dWf;
        _Wi -= lr * grads.dWi;
        _Wc -= lr * grads.dWc;
        _Wo -= lr * grads.dWo;

        _Uf -= lr * grads.dUf;
        _Ui -= lr * grads.dUi;
        _Uc -= lr * grads.dUc;
        _Uo -= lr * grads.dUo;

        _bf -= lr * grads.dbf;
        _bi -= lr * grads.dbi;
        _bc -= lr * grads.dbc;
        _bo -= lr * grads.dbo;

        _Wy -= lr * grads.dWy;
        _by -= lr * grads.dby;
    }

    public double Predict(double[][] sequence)
    {
        Vector<double> h = Vector<double>.Build.Dense(_hiddenSize, 0);
        Vector<double> c = Vector<double>.Build.Dense(_hiddenSize, 0);

        foreach (var step in sequence)
        {
            var x = Vector<double>.Build.DenseOfArray(step);

            var f = Sigmoid(_Wf * x + _Uf * h + _bf);
            var i = Sigmoid(_Wi * x + _Ui * h + _bi);
            var c_tilde = Tanh(_Wc * x + _Uc * h + _bc);
            c = f.PointwiseMultiply(c) + i.PointwiseMultiply(c_tilde);
            var o = Sigmoid(_Wo * x + _Uo * h + _bo);
            h = o.PointwiseMultiply(Tanh(c));
        }

        var y = Sigmoid(_Wy * h + _by);
        return y[0];
    }

    private Vector<double> Sigmoid(Vector<double> x)
    {
        return x.Map(v => 1.0 / (1.0 + Math.Exp(-v)));
    }

    private Vector<double> Tanh(Vector<double> x)
    {
        return x.Map(Math.Tanh);
    }

    private Vector<double> Dsigmoid(Vector<double> x)
    {
        return x.PointwiseMultiply(Vector<double>.Build.Dense(x.Count, 1) - x);
    }

    private Vector<double> Dtanh(Vector<double> x)
    {
        return x.Map(v => 1 - v * v);
    }
}

public class Gradients
{
    public Matrix<double> dWf, dWi, dWc, dWo;
    public Matrix<double> dUf, dUi, dUc, dUo;
    public Vector<double> dbf, dbi, dbc, dbo;
    public Matrix<double> dWy;
    public Vector<double> dby;

    public Gradients(int inputSize, int hiddenSize, int outputSize)
    {
        dWf = Matrix<double>.Build.Dense(hiddenSize, inputSize);
        dWi = Matrix<double>.Build.Dense(hiddenSize, inputSize);
        dWc = Matrix<double>.Build.Dense(hiddenSize, inputSize);
        dWo = Matrix<double>.Build.Dense(hiddenSize, inputSize);

        dUf = Matrix<double>.Build.Dense(hiddenSize, hiddenSize);
        dUi = Matrix<double>.Build.Dense(hiddenSize, hiddenSize);
        dUc = Matrix<double>.Build.Dense(hiddenSize, hiddenSize);
        dUo = Matrix<double>.Build.Dense(hiddenSize, hiddenSize);

        dbf = Vector<double>.Build.Dense(hiddenSize);
        dbi = Vector<double>.Build.Dense(hiddenSize);
        dbc = Vector<double>.Build.Dense(hiddenSize);
        dbo = Vector<double>.Build.Dense(hiddenSize);

        dWy = Matrix<double>.Build.Dense(outputSize, hiddenSize);
        dby = Vector<double>.Build.Dense(outputSize);
    }
}
