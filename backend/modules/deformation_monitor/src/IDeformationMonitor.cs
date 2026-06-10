using DeformationMonitor.Module.Models;

namespace DeformationMonitor.Module;

public interface IDeformationMonitor
{
    Task<IReadOnlyList<DeformationResult>> RunDeformationAnalysisAsync(
        DeformationRequest request,
        CancellationToken stoppingToken);

    Task<DeformationRecord> SaveDeformationRecordAsync(
        Guid stationId,
        SensorData sensorData,
        DeformationResult result,
        CancellationToken stoppingToken);

    Task ApplyBeamCorrectionAsync(
        Guid stationId,
        double correctionAzimuth,
        double correctionElevation,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<DeformationRecord>> GetDeformationHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken);
}
