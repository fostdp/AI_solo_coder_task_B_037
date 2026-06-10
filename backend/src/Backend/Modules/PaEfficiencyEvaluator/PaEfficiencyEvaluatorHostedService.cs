using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.PaEfficiencyEvaluator;

public class PaEfficiencyEvaluatorHostedService : BackgroundService
{
    private readonly ILogger<PaEfficiencyEvaluatorHostedService> _logger;
    private readonly IPaEfficiencyEvaluator _evaluator;
    private readonly IDataChannels _dataChannels;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly PaEfficiencyOptions _options;

    public PaEfficiencyEvaluatorHostedService(
        ILogger<PaEfficiencyEvaluatorHostedService> logger,
        IPaEfficiencyEvaluator evaluator,
        IDataChannels dataChannels,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        IInfluxDBRepository influxRepo,
        IOptions<PaEfficiencyOptions> options)
    {
        _logger = logger;
        _evaluator = evaluator;
        _dataChannels = dataChannels;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PA Efficiency Evaluator started: Interval={Interval}min, Threshold={Threshold}%",
            _options.IntervalMinutes, _options.ThresholdPercent);

        var scheduledTask = RunScheduledEvaluationAsync(stoppingToken);
        var channelTask = ProcessEfficiencyRequestsAsync(stoppingToken);

        await Task.WhenAny(scheduledTask, channelTask);
    }

    private async Task RunScheduledEvaluationAsync(CancellationToken stoppingToken)
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

                    await RunStationEfficiencyEvaluationAsync(station, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled PA efficiency evaluation failed");
            }
        }
    }

    private async Task ProcessEfficiencyRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dataChannels.PaEfficiencyRequestReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _evaluator.RunEfficiencyEvaluationAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PA efficiency request for station {StationId}",
                    request.StationId);
            }
        }
    }

    private async Task RunStationEfficiencyEvaluationAsync(BaseStation station, CancellationToken stoppingToken)
    {
        var channels = (await _channelRepo.GetByStationIdAsync(station.Id, stoppingToken)).ToList();
        if (!channels.Any()) return;

        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddMinutes(-_options.IntervalMinutes * 2);

        var metrics = (await _influxRepo.GetStationMetricsAsync(
            station.Id.ToString(), startTime, endTime, stoppingToken)).ToList();

        var request = new PaEfficiencyRequest
        {
            StationId = station.Id,
            Channels = channels.AsReadOnly(),
            RecentMetrics = metrics.AsReadOnly()
        };

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, stoppingToken);
        await WriteEfficiencyMetricsToInfluxDBAsync(station.Id, results, stoppingToken);
    }

    private async Task WriteEfficiencyMetricsToInfluxDBAsync(
        Guid stationId,
        IReadOnlyList<PaEfficiencyResult> results,
        CancellationToken stoppingToken)
    {
        foreach (var result in results)
        {
            await _influxRepo.WriteEfficiencyMetricAsync(
                stationId.ToString(),
                result,
                stoppingToken);
        }
    }
}
