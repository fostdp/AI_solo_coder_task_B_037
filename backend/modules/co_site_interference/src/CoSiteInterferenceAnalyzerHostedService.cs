using CoSiteInterference.Module.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace CoSiteInterference.Module;

public class CoSiteInterferenceAnalyzerHostedService : BackgroundService
{
    private readonly ILogger<CoSiteInterferenceAnalyzerHostedService> _logger;
    private readonly ICoSiteInterferenceAnalyzer _analyzer;
    private readonly CoSiteInterferenceOptions _options;
    private readonly Channel<CoSiteInterferenceRequest> _requestChannel;

    public CoSiteInterferenceAnalyzerHostedService(
        ILogger<CoSiteInterferenceAnalyzerHostedService> logger,
        ICoSiteInterferenceAnalyzer analyzer,
        IOptions<CoSiteInterferenceOptions> options)
    {
        _logger = logger;
        _analyzer = analyzer;
        _options = options.Value;
        _requestChannel = Channel.CreateUnbounded<CoSiteInterferenceRequest>();
    }

    public ChannelWriter<CoSiteInterferenceRequest> RequestWriter => _requestChannel.Writer;

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
                _logger.LogDebug("Running scheduled co-site interference analysis");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled co-site interference analysis failed");
            }
        }
    }

    private async Task ProcessInterferenceRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _requestChannel.Reader.ReadAllAsync(stoppingToken))
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

    public async Task ProcessStationAsync(Guid stationId, double selfStartMhz, double selfEndMhz, CancellationToken stoppingToken)
    {
        var coSiteAntennas = await _analyzer.GetCoSiteAntennasAsync(stationId, stoppingToken);
        if (!coSiteAntennas.Any()) return;

        var request = new CoSiteInterferenceRequest
        {
            StationId = stationId,
            CoSiteAntennas = coSiteAntennas,
            SelfFrequencyStartMhz = selfStartMhz,
            SelfFrequencyEndMhz = selfEndMhz
        };

        await _analyzer.RunInterferenceAnalysisAsync(request, stoppingToken);
    }
}
