using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.CoSiteInterferenceAnalyzer;

public class CoSiteInterferenceAnalyzerHostedService : BackgroundService
{
    private readonly ILogger<CoSiteInterferenceAnalyzerHostedService> _logger;
    private readonly ICoSiteInterferenceAnalyzer _analyzer;
    private readonly IDataChannels _dataChannels;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly CoSiteInterferenceOptions _options;

    public CoSiteInterferenceAnalyzerHostedService(
        ILogger<CoSiteInterferenceAnalyzerHostedService> logger,
        ICoSiteInterferenceAnalyzer analyzer,
        IDataChannels dataChannels,
        IBaseStationRepository stationRepo,
        IInfluxDBRepository influxRepo,
        IOptions<CoSiteInterferenceOptions> options)
    {
        _logger = logger;
        _analyzer = analyzer;
        _dataChannels = dataChannels;
        _stationRepo = stationRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Co-Site Interference Analyzer started: Interval={Interval}min, IsolationThreshold={Threshold}dB",
            _options.IntervalMinutes, _options.IsolationThresholdDb);

        var scheduledTask = RunScheduledAnalysisAsync(stoppingToken);
        var channelTask = ProcessInterferenceRequestsAsync(stoppingToken);

        await Task.WhenAny(scheduledTask, channelTask);
    }

    private async Task RunScheduledAnalysisAsync(CancellationToken stoppingToken)
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

                    await RunStationInterferenceAnalysisAsync(station, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled co-site interference analysis failed");
            }
        }
    }

    private async Task ProcessInterferenceRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dataChannels.CoSiteInterferenceRequestReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _analyzer.RunInterferenceAnalysisAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing co-site interference request for station {StationId}",
                    request.StationId);
            }
        }
    }

    private async Task RunStationInterferenceAnalysisAsync(BaseStation station, CancellationToken stoppingToken)
    {
        var coSiteAntennas = await _analyzer.GetCoSiteAntennasAsync(station.Id, stoppingToken);
        if (!coSiteAntennas.Any()) return;

        var frequencyBand = station.FrequencyBand ?? 3500;
        var bandwidth = 100;

        var request = new CoSiteInterferenceRequest
        {
            StationId = station.Id,
            CoSiteAntennas = coSiteAntennas,
            SelfFrequencyStartMhz = frequencyBand - bandwidth / 2,
            SelfFrequencyEndMhz = frequencyBand + bandwidth / 2
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, stoppingToken);
        await WriteInterferenceMetricsToInfluxDBAsync(station.Id, results, stoppingToken);
    }

    private async Task WriteInterferenceMetricsToInfluxDBAsync(
        Guid stationId,
        IReadOnlyList<CoSiteInterferenceResult> results,
        CancellationToken stoppingToken)
    {
        foreach (var result in results)
        {
            await _influxRepo.WriteInterferenceMetricAsync(
                stationId.ToString(),
                result,
                stoppingToken);
        }
    }
}
