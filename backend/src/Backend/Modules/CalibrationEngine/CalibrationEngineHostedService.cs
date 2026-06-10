using MediatR;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.CalibrationEngine;

public class CalibrationEngineHostedService : BackgroundService
{
    private readonly ILogger<CalibrationEngineHostedService> _logger;
    private readonly ICalibrationEngine _engine;
    private readonly IDataChannels _dataChannels;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly CalibrationOptions _options;

    public CalibrationEngineHostedService(
        ILogger<CalibrationEngineHostedService> logger,
        ICalibrationEngine engine,
        IDataChannels dataChannels,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        IInfluxDBRepository influxRepo,
        IOptions<CalibrationOptions> options)
    {
        _logger = logger;
        _engine = engine;
        _dataChannels = dataChannels;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Calibration Engine started: Interval={Interval}min, Algorithm={Algorithm}",
            _options.IntervalMinutes, _options.Algorithm);

        var scheduledTask = RunScheduledCalibrationAsync(stoppingToken);
        var channelTask = ProcessCalibrationRequestsAsync(stoppingToken);

        await Task.WhenAny(scheduledTask, channelTask);
    }

    private async Task RunScheduledCalibrationAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var stations = await _stationRepo.GetAllAsync(stoppingToken);

                foreach (var station in stations)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    await RunStationCalibrationAsync(station, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled calibration failed");
            }
        }
    }

    private async Task ProcessCalibrationRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dataChannels.CalibrationRequestReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _engine.RunCalibrationAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing calibration request for station {StationId}", request.StationId);
            }
        }
    }

    private async Task RunStationCalibrationAsync(BaseStation station, CancellationToken stoppingToken)
    {
        var channels = (await _channelRepo.GetByStationIdAsync(station.Id, stoppingToken)).ToList();
        if (!channels.Any()) return;

        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-1);
        var metrics = (await _influxRepo.GetStationMetricsAsync(
            station.Id.ToString(), startTime, endTime, stoppingToken)).ToList();

        if (!metrics.Any())
        {
            var latest = await _influxRepo.GetLatestStationMetricsAsync(
                station.Id.ToString(), stoppingToken);
            metrics = latest.ToList();
        }

        if (!metrics.Any()) return;

        var request = new CalibrationRequest
        {
            StationId = station.Id,
            AlgorithmType = _options.Algorithm,
            Channels = channels.AsReadOnly(),
            Metrics = metrics.AsReadOnly()
        };

        await _engine.RunCalibrationAsync(request, stoppingToken);
    }
}
