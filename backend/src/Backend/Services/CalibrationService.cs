using AntennaMonitoring.Algorithms;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Services;

public interface ICalibrationService
{
    Task<CalibrationResult> RunCalibrationAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<BeamPatternDTO> CalculateBeamPatternAsync(Guid stationId,
        double azimuth = 0, double elevation = 0, CancellationToken cancellationToken = default);
}

public class CalibrationService : BackgroundService, ICalibrationService
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly CalibrationOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IBeamformingCalibration> _algorithms;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IOptions<CalibrationOptions> options,
        IServiceProvider serviceProvider,
        IEnumerable<IBeamformingCalibration> algorithms)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _algorithms = algorithms;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"Calibration Service started with interval {_options.IntervalMinutes} minutes");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCalibrationForAllStationsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Calibration service error");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task RunCalibrationForAllStationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var stationRepo = scope.ServiceProvider.GetRequiredService<IBaseStationRepository>();
        var mqttService = scope.ServiceProvider.GetRequiredService<MQTTService>();

        var stations = await stationRepo.GetAllAsync(cancellationToken);

        foreach (var station in stations.Where(s => s.Status == "active"))
        {
            try
            {
                var result = await RunCalibrationAsync(station.Id, cancellationToken);

                if (result.Success)
                {
                    await mqttService.PublishCalibrationAsync(
                        station.Id,
                        station.StationCode,
                        result.SllBefore,
                        result.SllAfter,
                        result.Algorithm,
                        cancellationToken);

                    _logger.LogInformation(
                        $"Calibration completed for {station.StationCode}: " +
                        $"SLL {result.SllBefore:F1} → {result.SllAfter:F1} dB, " +
                        $"Algorithm: {result.Algorithm}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Calibration failed for station {station.StationCode}");
            }
        }
    }

    public async Task<CalibrationResult> RunCalibrationAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();

        var channelRepo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var influxRepo = scope.ServiceProvider.GetRequiredService<IInfluxDBRepository>();
        var calibrationRepo = scope.ServiceProvider.GetRequiredService<ICalibrationRecordRepository>();

        var channels = (await channelRepo.GetByStationIdAsync(stationId, cancellationToken)).ToList();
        if (!channels.Any())
        {
            return new CalibrationResult
            {
                Success = false,
                Algorithm = _options.Algorithm,
                CalibrationTime = DateTime.UtcNow
            };
        }

        var startTime = DateTime.UtcNow.AddHours(-1);
        var endTime = DateTime.UtcNow;
        var metrics = (await influxRepo.GetStationMetricsAsync(stationId.ToString(), startTime, endTime, cancellationToken))
            .ToList();

        if (!metrics.Any())
        {
            var latestMetrics = await influxRepo.GetLatestStationMetricsAsync(stationId.ToString(), cancellationToken);
            metrics = latestMetrics.ToList();
        }

        if (!metrics.Any())
        {
            return new CalibrationResult
            {
                Success = false,
                Algorithm = _options.Algorithm,
                CalibrationTime = DateTime.UtcNow
            };
        }

        var algorithm = _algorithms.FirstOrDefault(a => a.AlgorithmName == _options.Algorithm)
                         ?? _algorithms.First();

        var result = await algorithm.CalibrateAsync(stationId, channels, metrics, cancellationToken);

        if (result.Success || result.Converged)
        {
            await SaveCalibrationResultsAsync(stationId, result, calibrationRepo, channelRepo, cancellationToken);
            await influxRepo.WriteBeamformingMetricsAsync(
                stationId, result.Algorithm,
                (result.SllBefore + result.SllAfter) / 2,
                result.SllBefore, result.SllAfter,
                10.0, 8.0,
                result.Converged, cancellationToken);
        }

        return result;
    }

    private async Task SaveCalibrationResultsAsync(
        Guid stationId,
        CalibrationResult result,
        ICalibrationRecordRepository calibrationRepo,
        IChannelRepository channelRepo,
        CancellationToken cancellationToken)
    {
        var records = new List<CalibrationRecord>();

        foreach (var cc in result.ChannelCalibrations)
        {
            records.Add(new CalibrationRecord
            {
                StationId = stationId,
                ChannelId = cc.ChannelId,
                CalibrationTime = result.CalibrationTime,
                AmplitudeDeviation = (decimal)cc.AmplitudeDeviation,
                PhaseDeviation = (decimal)cc.PhaseDeviation,
                CalibrationCoeffAmplitude = (decimal)cc.CalibrationCoeffAmplitude,
                CalibrationCoeffPhase = (decimal)cc.CalibrationCoeffPhase,
                SllBefore = (decimal)result.SllBefore,
                SllAfter = (decimal)result.SllAfter,
                Algorithm = result.Algorithm
            });

            await channelRepo.UpdateCalibrationCoeffAsync(
                cc.ChannelId,
                (decimal)cc.CalibrationCoeffAmplitude,
                (decimal)cc.CalibrationCoeffPhase,
                cancellationToken);
        }

        await calibrationRepo.BulkCreateAsync(records, cancellationToken);
    }

    public async Task<BeamPatternDTO> CalculateBeamPatternAsync(Guid stationId,
        double azimuth = 0, double elevation = 0, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();

        var stationRepo = scope.ServiceProvider.GetRequiredService<IBaseStationRepository>();
        var channelRepo = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var influxRepo = scope.ServiceProvider.GetRequiredService<IInfluxDBRepository>();

        var station = await stationRepo.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return new BeamPatternDTO { StationId = stationId, CalculatedAt = DateTime.UtcNow };
        }

        var channels = (await channelRepo.GetByStationIdAsync(stationId, cancellationToken)).ToList();
        var metrics = (await influxRepo.GetLatestStationMetricsAsync(stationId.ToString(), cancellationToken)).ToList();

        var algorithm = _algorithms.First();
        var pattern = algorithm.CalculateBeamPattern(channels, metrics);
        var sll = algorithm.CalculateSLL(channels, metrics);

        return new BeamPatternDTO
        {
            StationId = stationId,
            Azimuth = azimuth,
            Elevation = elevation,
            GainPattern = pattern.ToList(),
            Sll = sll,
            BeamwidthAzimuth = 10.0,
            BeamwidthElevation = 8.0,
            CalculatedAt = DateTime.UtcNow
        };
    }
}
