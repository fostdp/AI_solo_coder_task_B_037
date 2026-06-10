using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.SpectrumScanner;

public class SpectrumScannerHostedService : BackgroundService
{
    private readonly ILogger<SpectrumScannerHostedService> _logger;
    private readonly ISpectrumScanner _scanner;
    private readonly IDataChannels _dataChannels;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly SpectrumScanOptions _options;

    public SpectrumScannerHostedService(
        ILogger<SpectrumScannerHostedService> logger,
        ISpectrumScanner scanner,
        IDataChannels dataChannels,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        IInfluxDBRepository influxRepo,
        IOptions<SpectrumScanOptions> options)
    {
        _logger = logger;
        _scanner = scanner;
        _dataChannels = dataChannels;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Spectrum Scanner started: Interval={Interval}min, Band={Start}-{End}MHz, AutoNull={AutoNull}",
            _options.IntervalMinutes, _options.StartFrequencyMhz,
            _options.EndFrequencyMhz, _options.AutoNullSteering);

        var scheduledTask = RunScheduledScansAsync(stoppingToken);
        var channelTask = ProcessScanRequestsAsync(stoppingToken);

        await Task.WhenAny(scheduledTask, channelTask);
    }

    private async Task RunScheduledScansAsync(CancellationToken stoppingToken)
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

                    await RunStationSpectrumScanAsync(station, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled spectrum scanning failed");
            }
        }
    }

    private async Task ProcessScanRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dataChannels.SpectrumScanRequestReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = await _scanner.RunSpectrumScanAsync(request, stoppingToken);
                await WriteScanMetricsToInfluxDBAsync(request.StationId, result, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing spectrum scan request for station {StationId}",
                    request.StationId);
            }
        }
    }

    private async Task RunStationSpectrumScanAsync(BaseStation station, CancellationToken stoppingToken)
    {
        var channels = (await _channelRepo.GetByStationIdAsync(station.Id, stoppingToken)).ToList();
        if (!channels.Any()) return;

        var request = new SpectrumScanRequest
        {
            StationId = station.Id,
            StartFrequencyMhz = _options.StartFrequencyMhz,
            EndFrequencyMhz = _options.EndFrequencyMhz,
            ResolutionBandwidthKhz = _options.ResolutionBandwidthKhz,
            Channels = channels.AsReadOnly()
        };

        var result = await _scanner.RunSpectrumScanAsync(request, stoppingToken);
        await WriteScanMetricsToInfluxDBAsync(station.Id, result, stoppingToken);
    }

    private async Task WriteScanMetricsToInfluxDBAsync(
        Guid stationId,
        SpectrumScanResult result,
        CancellationToken stoppingToken)
    {
        await _influxRepo.WriteSpectrumMetricAsync(
            stationId.ToString(),
            result,
            stoppingToken);
    }
}
