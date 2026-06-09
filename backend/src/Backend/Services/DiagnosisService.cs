using AntennaMonitoring.Algorithms;
using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Services;

public interface IDiagnosisService
{
    Task<IEnumerable<DiagnosisResult>> RunDiagnosisAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<DiagnosisResult> DiagnoseChannelAsync(Guid channelId, CancellationToken cancellationToken = default);
}

public class DiagnosisService : BackgroundService, IDiagnosisService
{
    private readonly ILogger<DiagnosisService> _logger;
    private readonly DiagnosisOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IHealthDiagnosis> _diagnosisModels;

    public DiagnosisService(
        ILogger<DiagnosisService> logger,
        IOptions<DiagnosisOptions> options,
        IServiceProvider serviceProvider,
        IEnumerable<IHealthDiagnosis> diagnosisModels)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _diagnosisModels = diagnosisModels;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"Diagnosis Service started with interval {_options.IntervalMinutes} minutes");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDiagnosisForAllStationsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnosis service error");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task RunDiagnosisForAllStationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var stationRepo = scope.ServiceProvider.GetRequiredService<IBaseStationRepository>();

        var stations = await stationRepo.GetAllAsync(cancellationToken);

        foreach (var station in stations.Where(s => s.Status == "active"))
        {
            try
            {
                var results = await RunDiagnosisAsync(station.Id, cancellationToken);
                var highRiskCount = results.Count(r => r.FailureProbability >= _options.FailureProbabilityThreshold);

                _logger.LogInformation(
                    $"Diagnosis completed for {station.StationCode}: " +
                    $"{results.Count()} channels analyzed, " +
                    $"{highRiskCount} high risk channels");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Diagnosis failed for station {station.StationCode}");
            }
        }
    }

    public async Task<IEnumerable<DiagnosisResult>> RunDiagnosisAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();

        var channelRepo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var influxRepo = scope.ServiceProvider.GetRequiredService<IInfluxDBRepository>();
        var diagnosisRepo = scope.ServiceProvider.GetRequiredService<IDiagnosisRecordRepository>();
        var alarmRepo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();

        var channels = (await channelRepo.GetByStationIdAsync(stationId, cancellationToken)).ToList();
        if (!channels.Any())
            return Enumerable.Empty<DiagnosisResult>();

        var startTime = DateTime.UtcNow.AddHours(-24);
        var endTime = DateTime.UtcNow;
        var allMetrics = (await influxRepo.GetStationMetricsAsync(stationId.ToString(), startTime, endTime, cancellationToken))
            .ToList();

        var model = _diagnosisModels.FirstOrDefault(m => m.ModelName == _options.ModelType)
                    ?? _diagnosisModels.First();

        var results = new List<DiagnosisResult>();
        var diagnosisRecords = new List<DiagnosisRecord>();

        foreach (var channel in channels)
        {
            var channelMetrics = allMetrics.Where(m => m.ChannelId == channel.Id.ToString()).ToList();
            var result = await model.DiagnoseAsync(stationId, channel, channelMetrics, cancellationToken);

            results.Add(result);

            diagnosisRecords.Add(new DiagnosisRecord
            {
                StationId = stationId,
                ChannelId = channel.Id,
                DiagnosisTime = result.DiagnosisTime,
                SwrValue = (decimal)result.SwrValue,
                TemperatureValue = (decimal)result.TemperatureValue,
                FailureProbability = (decimal)result.FailureProbability,
                ModelType = result.ModelType,
                PredictionHorizonHours = result.PredictionHorizonHours,
                Recommendation = result.Recommendation
            });

            await channelRepo.UpdateFailureProbabilityAsync(channel.Id, (decimal)result.FailureProbability, cancellationToken);

            await influxRepo.WriteDiagnosisMetricsAsync(
                stationId, channel.Id, result.ModelType,
                result.FailureProbability, result.SwrPredicted,
                result.TemperaturePredicted, result.AnomalyScore,
                result.PredictedFailureHours, result.HealthScore,
                cancellationToken);

            if (result.FailureProbability >= _options.FailureProbabilityThreshold)
            {
                await CreateHighRiskAlarmAsync(stationId, channel, result, alarmRepo, cancellationToken);
            }
        }

        await diagnosisRepo.BulkCreateAsync(diagnosisRecords, cancellationToken);
        return results;
    }

    public async Task<DiagnosisResult> DiagnoseChannelAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();

        var channelRepo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var influxRepo = scope.ServiceProvider.GetRequiredService<IInfluxDBRepository>();

        var channel = await channelRepo.GetByIdAsync(channelId, cancellationToken);
        if (channel == null)
        {
            return new DiagnosisResult
            {
                Success = false,
                ChannelId = channelId,
                DiagnosisTime = DateTime.UtcNow
            };
        }

        var startTime = DateTime.UtcNow.AddHours(-24);
        var endTime = DateTime.UtcNow;
        var metrics = (await influxRepo.GetChannelMetricsAsync(
            channelId.ToString(), startTime, endTime, "raw", cancellationToken)).ToList();

        var model = _diagnosisModels.FirstOrDefault(m => m.ModelName == _options.ModelType)
                    ?? _diagnosisModels.First();

        var result = await model.DiagnoseAsync(channel.StationId, channel, metrics, cancellationToken);

        if (result.Success)
        {
            await channelRepo.UpdateFailureProbabilityAsync(channelId, (decimal)result.FailureProbability, cancellationToken);
        }

        return result;
    }

    private async Task CreateHighRiskAlarmAsync(
        Guid stationId,
        Channel channel,
        DiagnosisResult result,
        IAlarmRepository alarmRepo,
        CancellationToken cancellationToken)
    {
        var activeAlarms = await alarmRepo.GetByStationIdAsync(stationId, "active", cancellationToken);
        var existing = activeAlarms.FirstOrDefault(a =>
            a.AlarmCode == "FAILURE_PREDICTED" &&
            a.ChannelId == channel.Id &&
            a.Status == "active");

        if (existing != null)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ActualValue = (decimal)result.FailureProbability;
            return;
        }

        var alarm = new Alarm
        {
            AlarmCode = "FAILURE_PREDICTED",
            AlarmType = "prediction",
            AlarmLevel = "warning",
            StationId = stationId,
            ChannelId = channel.Id,
            Title = $"Channel {channel.ChannelIndex} Failure Predicted",
            Description = $"AI model predicts {result.FailureProbability:P1} failure probability " +
                          $"within {result.PredictionHorizonHours}h. " +
                          $"Predicted SWR: {result.SwrPredicted:F2}, " +
                          $"Temperature: {result.TemperaturePredicted:F1}°C. " +
                          $"Recommendation: {result.Recommendation}",
            ThresholdValue = (decimal)_options.FailureProbabilityThreshold,
            ActualValue = (decimal)result.FailureProbability,
            Status = "active"
        };

        await alarmRepo.CreateAsync(alarm, cancellationToken);

        await UpdateChannelStatusAsync(channel.Id, result.FailureProbability, cancellationToken);
    }

    private async Task UpdateChannelStatusAsync(Guid channelId, double failureProbability,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var channelRepo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();

        string status = failureProbability switch
        {
            >= 0.9 => "fault",
            >= 0.7 => "warning",
            _ => "normal"
        };

        await channelRepo.UpdateStatusAsync(channelId, status, cancellationToken);
    }
}
