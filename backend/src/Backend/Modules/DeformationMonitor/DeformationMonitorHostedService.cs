using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.DeformationMonitor;

public class DeformationMonitorHostedService : BackgroundService
{
    private readonly ILogger<DeformationMonitorHostedService> _logger;
    private readonly IDeformationMonitor _deformationMonitor;
    private readonly IDataChannels _dataChannels;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly DeformationOptions _options;

    public DeformationMonitorHostedService(
        ILogger<DeformationMonitorHostedService> logger,
        IDeformationMonitor deformationMonitor,
        IDataChannels dataChannels,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        IInfluxDBRepository influxRepo,
        IOptions<DeformationOptions> options)
    {
        _logger = logger;
        _deformationMonitor = deformationMonitor;
        _dataChannels = dataChannels;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Deformation Monitor started: Interval={Interval}min, Threshold={Threshold}mm, AutoCorrection={AutoCorrection}",
            _options.IntervalMinutes, _options.ThresholdMm, _options.AutoBeamCorrection);

        var scheduledTask = RunScheduledMonitoringAsync(stoppingToken);
        var sensorDataTask = ProcessSensorDataAsync(stoppingToken);
        var channelTask = ProcessDeformationRequestsAsync(stoppingToken);

        await Task.WhenAny(scheduledTask, sensorDataTask, channelTask);
    }

    private async Task RunScheduledMonitoringAsync(CancellationToken stoppingToken)
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

                    await RunStationDeformationMonitoringAsync(station, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled deformation monitoring failed");
            }
        }
    }

    private async Task ProcessSensorDataAsync(CancellationToken stoppingToken)
    {
        await foreach (var sensorEvent in _dataChannels.SensorDataReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var channels = (await _channelRepo.GetByStationIdAsync(sensorEvent.StationId, stoppingToken)).ToList();

                var request = new DeformationRequest
                {
                    StationId = sensorEvent.StationId,
                    SensorData = sensorEvent.SensorData,
                    Channels = channels.AsReadOnly()
                };

                await _deformationMonitor.RunDeformationAnalysisAsync(request, stoppingToken);

                await WriteSensorMetricsToInfluxDBAsync(sensorEvent, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sensor data for station {StationId}",
                    sensorEvent.StationId);
            }
        }
    }

    private async Task ProcessDeformationRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dataChannels.DeformationRequestReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _deformationMonitor.RunDeformationAnalysisAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deformation request for station {StationId}",
                    request.StationId);
            }
        }
    }

    private async Task RunStationDeformationMonitoringAsync(BaseStation station, CancellationToken stoppingToken)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddMinutes(-_options.IntervalMinutes);

        var metrics = await _influxRepo.GetSensorMetricsAsync(
            station.Id.ToString(), startTime, endTime, stoppingToken);

        if (!metrics.Any()) return;

        var sensorData = metrics.Select(m => new SensorData
        {
            StationId = station.Id,
            SensorIndex = m.SensorIndex,
            SensorType = m.SensorType,
            TiltAngleX = m.TiltAngleX,
            TiltAngleY = m.TiltAngleY,
            TiltAngleZ = m.TiltAngleZ,
            StrainValue = m.StrainValue,
            Temperature = m.Temperature,
            WindSpeed = m.WindSpeed,
            Timestamp = m.Timestamp
        }).ToList();

        var channels = (await _channelRepo.GetByStationIdAsync(station.Id, stoppingToken)).ToList();

        var request = new DeformationRequest
        {
            StationId = station.Id,
            SensorData = sensorData.AsReadOnly(),
            Channels = channels.AsReadOnly()
        };

        await _deformationMonitor.RunDeformationAnalysisAsync(request, stoppingToken);
    }

    private async Task WriteSensorMetricsToInfluxDBAsync(
        SensorDataReceivedEvent sensorEvent,
        CancellationToken stoppingToken)
    {
        foreach (var sensor in sensorEvent.SensorData)
        {
            await _influxRepo.WriteSensorMetricAsync(
                sensorEvent.StationId.ToString(),
                sensor,
                stoppingToken);
        }
    }
}
