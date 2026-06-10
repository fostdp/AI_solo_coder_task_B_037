using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpectrumScanner.Module.Models;
using SpectrumScanner.Module.Workers;

namespace SpectrumScanner.Module;

public class SpectrumScannerHostedService : BackgroundService
{
    private readonly ILogger<SpectrumScannerHostedService> _logger;
    private readonly ISpectrumScanner _scanner;
    private readonly SpectrumScanOptions _options;
    private readonly GpuFftWorker _gpuFftWorker;

    public SpectrumScannerHostedService(
        ILogger<SpectrumScannerHostedService> logger,
        ISpectrumScanner scanner,
        IOptions<SpectrumScanOptions> options,
        GpuFftWorker gpuFftWorker = null)
    {
        _logger = logger;
        _scanner = scanner;
        _options = options.Value;
        _gpuFftWorker = gpuFftWorker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Spectrum Scanner Module started: Interval={Interval}min, Band={Start}-{End}MHz, AutoNull={AutoNull}, GpuAcceleration={GpuAcceleration}",
            _options.IntervalMinutes, _options.StartFrequencyMhz,
            _options.EndFrequencyMhz, _options.AutoNullSteering,
            _gpuFftWorker?.IsGpuAvailable ?? false);

        await RunScheduledScansAsync(stoppingToken);
    }

    private async Task RunScheduledScansAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.LogDebug("Executing scheduled spectrum scan");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled spectrum scanning failed");
            }
        }
    }

    public async Task<SpectrumScanResult> ExecuteScanAsync(
        SpectrumScanRequest request,
        CancellationToken stoppingToken)
    {
        return await _scanner.RunSpectrumScanAsync(request, stoppingToken);
    }
}
