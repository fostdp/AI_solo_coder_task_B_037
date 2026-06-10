using AntennaMonitoring.Messages;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.HealthDiagnoser;

public class HealthDiagnoserHostedService : BackgroundService
{
    private readonly ILogger<HealthDiagnoserHostedService> _logger;
    private readonly IHealthDiagnoser _diagnoser;
    private readonly IDataChannels _dataChannels;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly DiagnosisOptions _options;

    public HealthDiagnoserHostedService(
        ILogger<HealthDiagnoserHostedService> logger,
        IHealthDiagnoser diagnoser,
        IDataChannels dataChannels,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        IInfluxDBRepository influxRepo,
        IOptions<DiagnosisOptions> options)
    {
        _logger = logger;
        _diagnoser = diagnoser;
        _dataChannels = dataChannels;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Health Diagnoser started: Interval={Interval}min, Model={Model}, Threshold={Threshold}",
            _options.IntervalMinutes, _options.ModelType, _options.FailureProbabilityThreshold);

        var scheduledTask = RunScheduledDiagnosisAsync(stoppingToken);
        var channelTask = ProcessDiagnosisRequestsAsync(stoppingToken);

        await Task.WhenAny(scheduledTask, channelTask);
    }

    private async Task RunScheduledDiagnosisAsync(CancellationToken stoppingToken)
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

                    await RunStationDiagnosisAsync(station, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled diagnosis failed");
            }
        }
    }

    private async Task ProcessDiagnosisRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dataChannels.DiagnosisRequestReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _diagnoser.RunDiagnosisAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing diagnosis request for station {StationId}", request.StationId);
            }
        }
    }

    private async Task RunStationDiagnosisAsync(BaseStation station, CancellationToken stoppingToken)
    {
        var channels = (await _channelRepo.GetByStationIdAsync(station.Id, stoppingToken)).ToList();
        if (!channels.Any()) return;

        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-24);
        var metrics = (await _influxRepo.GetStationMetricsAsync(
            station.Id.ToString(), startTime, endTime, stoppingToken)).ToList();

        if (!metrics.Any()) return;

        var request = new DiagnosisRequest
        {
            StationId = station.Id,
            ModelType = _options.ModelType,
            Channels = channels.AsReadOnly(),
            Metrics = metrics.AsReadOnly()
        };

        await _diagnoser.RunDiagnosisAsync(request, stoppingToken);
    }
}
