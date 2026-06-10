using MediatR;
using AntennaMonitoring.Algorithms;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.HealthDiagnoser;

public class HealthDiagnoser : IHealthDiagnoser
{
    private readonly ILogger<HealthDiagnoser> _logger;
    private readonly IMediator _mediator;
    private readonly IEnumerable<IHealthDiagnosis> _models;
    private readonly IChannelRepository _channelRepo;
    private readonly IDiagnosisRecordRepository _diagnosisRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly DiagnosisOptions _options;

    public HealthDiagnoser(
        ILogger<HealthDiagnoser> logger,
        IMediator mediator,
        IEnumerable<IHealthDiagnosis> models,
        IChannelRepository channelRepo,
        IDiagnosisRecordRepository diagnosisRepo,
        IInfluxDBRepository influxRepo,
        IOptions<DiagnosisOptions> options)
    {
        _logger = logger;
        _mediator = mediator;
        _models = models;
        _channelRepo = channelRepo;
        _diagnosisRepo = diagnosisRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    public async Task<DiagnosisResponse> RunDiagnosisAsync(DiagnosisRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var model = SelectModel(request.ModelType);
            if (model == null)
            {
                return new DiagnosisResponse
                {
                    Success = false,
                    ErrorMessage = $"Model '{request.ModelType}' not found"
                };
            }

            var results = new List<ChannelDiagnosisResult>();
            var records = new List<DiagnosisRecord>();
            var highRiskChannelIds = new List<Guid>();

            foreach (var channel in request.Channels)
            {
                var channelMetrics = request.Metrics
                    .Where(m => m.ChannelId == channel.Id.ToString())
                    .ToList();

                var result = await model.DiagnoseAsync(
                    request.StationId,
                    channel,
                    channelMetrics,
                    cancellationToken);

                var channelResult = new ChannelDiagnosisResult
                {
                    ChannelId = result.ChannelId,
                    ChannelIndex = result.ChannelIndex,
                    SwrValue = result.SwrValue,
                    TemperatureValue = result.TemperatureValue,
                    FailureProbability = result.FailureProbability,
                    SwrPredicted = result.SwrPredicted,
                    TemperaturePredicted = result.TemperaturePredicted,
                    AnomalyScore = result.AnomalyScore,
                    PredictedFailureHours = result.PredictedFailureHours,
                    HealthScore = result.HealthScore,
                    PredictionHorizonHours = result.PredictionHorizonHours,
                    Recommendation = result.Recommendation
                };

                results.Add(channelResult);

                records.Add(new DiagnosisRecord
                {
                    StationId = request.StationId,
                    ChannelId = channel.Id,
                    DiagnosisTime = result.DiagnosisTime,
                    SwrValue = (decimal)result.SwrValue,
                    TemperatureValue = (decimal)result.TemperatureValue,
                    FailureProbability = (decimal)result.FailureProbability,
                    ModelType = result.ModelType,
                    PredictionHorizonHours = result.PredictionHorizonHours,
                    Recommendation = result.Recommendation
                });

                await _channelRepo.UpdateFailureProbabilityAsync(
                    channel.Id, (decimal)result.FailureProbability, cancellationToken);

                await _influxRepo.WriteDiagnosisMetricsAsync(
                    request.StationId, channel.Id, result.ModelType,
                    result.FailureProbability, result.SwrPredicted,
                    result.TemperaturePredicted, result.AnomalyScore,
                    result.PredictedFailureHours, result.HealthScore,
                    cancellationToken);

                if (result.FailureProbability > _options.FailureProbabilityThreshold)
                {
                    highRiskChannelIds.Add(channel.Id);
                }
            }

            await _diagnosisRepo.BulkCreateAsync(records, cancellationToken);

            await PublishDiagnosisCompletedEventAsync(
                request.StationId, results, highRiskChannelIds, model.ModelName, cancellationToken);

            _logger.LogInformation(
                "Diagnosis completed for station {StationId}: Model={Model}, Channels={Count}, HighRisk={HighRisk}",
                request.StationId, model.ModelName, results.Count, highRiskChannelIds.Count);

            return new DiagnosisResponse
            {
                Success = true,
                DiagnosisTime = DateTime.UtcNow,
                ModelType = model.ModelName,
                Results = results.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnosis failed for station {StationId}", request.StationId);
            return new DiagnosisResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DiagnosisFeature> ExtractFeaturesAsync(
        Channel channel,
        IReadOnlyList<ChannelMetrics> metrics,
        CancellationToken cancellationToken)
    {
        var ordered = metrics.OrderBy(m => m.Timestamp).ToList();

        var feature = new DiagnosisFeature
        {
            StationId = channel.StationId,
            ChannelId = channel.Id,
            ChannelIndex = channel.ChannelIndex,
            SampleCount = ordered.Count,
            TimeSpanHours = ordered.Count > 1
                ? (ordered.Last().Timestamp - ordered.First().Timestamp).TotalHours
                : 0
        };

        if (ordered.Count == 0) return feature;

        var swrValues = ordered.Select(m => m.Swr).ToArray();
        var tempValues = ordered.Select(m => m.Temperature).ToArray();
        var ampValues = ordered.Select(m => m.Amplitude).ToArray();
        var phaseValues = ordered.Select(m => m.Phase).ToArray();
        var txPowerValues = ordered.Select(m => m.TxPower).ToArray();
        var rxPowerValues = ordered.Select(m => m.RxPower).ToArray();
        var berValues = ordered.Select(m => m.Ber).ToArray();

        feature.SwrMean = swrValues.Average();
        feature.SwrStd = CalculateStdDev(swrValues);
        feature.SwrMax = swrValues.Max();
        feature.SwrTrend = CalculateTrend(swrValues);

        feature.TempMean = tempValues.Average();
        feature.TempStd = CalculateStdDev(tempValues);
        feature.TempMax = tempValues.Max();
        feature.TempTrend = CalculateTrend(tempValues);

        feature.AmpMean = ampValues.Average();
        feature.AmpStd = CalculateStdDev(ampValues);

        feature.PhaseMean = phaseValues.Average();
        feature.PhaseStd = CalculateStdDev(phaseValues);

        feature.TxPowerMean = txPowerValues.Average();
        feature.RxPowerMean = rxPowerValues.Average();
        feature.BerMean = berValues.Average();

        return await Task.FromResult(feature);
    }

    private IHealthDiagnosis? SelectModel(string modelType)
    {
        if (string.IsNullOrEmpty(modelType))
        {
            return _models.FirstOrDefault(m => m.ModelName == _options.ModelType)
                   ?? _models.First();
        }

        return _models.FirstOrDefault(m =>
            m.ModelName.Equals(modelType, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PublishDiagnosisCompletedEventAsync(
        Guid stationId,
        List<ChannelDiagnosisResult> results,
        List<Guid> highRiskChannelIds,
        string modelType,
        CancellationToken cancellationToken)
    {
        var eventData = new DiagnosisCompletedEvent
        {
            StationId = stationId,
            ModelType = modelType,
            DiagnosisTime = DateTime.UtcNow,
            ChannelCount = results.Count,
            HighRiskChannelCount = highRiskChannelIds.Count,
            AverageFailureProbability = results.Average(r => r.FailureProbability),
            HighRiskChannelIds = highRiskChannelIds.AsReadOnly()
        };

        await _mediator.Publish(eventData, cancellationToken);
    }

    private static double CalculateStdDev(double[] values)
    {
        if (values.Length <= 1) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => Math.Pow(v - mean, 2)));
    }

    private static double CalculateTrend(double[] values)
    {
        if (values.Length <= 1) return 0;
        var x = Enumerable.Range(0, values.Length).Select(i => (double)i).ToArray();
        var meanX = x.Average();
        var meanY = values.Average();
        var numerator = x.Zip(values, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
        var denominator = x.Sum(xi => (xi - meanX) * (xi - meanX));
        return denominator == 0 ? 0 : numerator / denominator;
    }
}

public class DiagnosisFeature
{
    public Guid StationId { get; set; }
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public int SampleCount { get; set; }
    public double TimeSpanHours { get; set; }

    public double SwrMean { get; set; }
    public double SwrStd { get; set; }
    public double SwrMax { get; set; }
    public double SwrTrend { get; set; }

    public double TempMean { get; set; }
    public double TempStd { get; set; }
    public double TempMax { get; set; }
    public double TempTrend { get; set; }

    public double AmpMean { get; set; }
    public double AmpStd { get; set; }

    public double PhaseMean { get; set; }
    public double PhaseStd { get; set; }

    public double TxPowerMean { get; set; }
    public double RxPowerMean { get; set; }
    public double BerMean { get; set; }
}
