using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Modules.SpectrumScanner;

public interface ISpectrumScanner
{
    Task<SpectrumScanResult> RunSpectrumScanAsync(
        SpectrumScanRequest request,
        CancellationToken stoppingToken);

    Task<SpectrumScanRecord> SaveScanRecordAsync(
        Guid stationId,
        SpectrumScanResult result,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<SpectrumScanRecord>> GetScanHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken);

    Task ApplyNullSteeringAsync(
        Guid stationId,
        double[] interferenceDirectionsDeg,
        CancellationToken stoppingToken);
}
