using DeformationMonitor.Module.Models;
using DeformationMonitor.Module.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeformationMonitor.Module;

public class DeformationMonitorHostedService : BackgroundService
{
    private readonly ILogger<DeformationMonitorHostedService> _logger;
    private readonly IDeformationMonitor _deformationMonitor;
    private readonly DeformationOptions _options;
    private readonly FemCalculationWorker _femWorker;

    public DeformationMonitorHostedService(
        ILogger<DeformationMonitorHostedService> logger,
        IDeformationMonitor deformationMonitor,
        IOptions<DeformationOptions> options,
        FemCalculationWorker femWorker)
    {
        _logger = logger;
        _deformationMonitor = deformationMonitor;
        _options = options.Value;
        _femWorker = femWorker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Deformation Monitor started: Interval={Interval}min, Threshold={Threshold}mm, AutoCorrection={AutoCorrection}",
            _options.IntervalMinutes, _options.ThresholdMm, _options.AutoBeamCorrection);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override void Dispose()
    {
        _femWorker.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
