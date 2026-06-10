using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Modules.CoSiteInterferenceAnalyzer;

public interface ICoSiteInterferenceAnalyzer
{
    Task<IReadOnlyList<CoSiteInterferenceResult>> RunInterferenceAnalysisAsync(
        CoSiteInterferenceRequest request,
        CancellationToken stoppingToken);

    Task<CoSiteInterferenceRecord> SaveInterferenceRecordAsync(
        Guid stationId,
        CoSiteAntenna interferingAntenna,
        CoSiteInterferenceResult result,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<CoSiteInterferenceRecord>> GetInterferenceHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<CoSiteAntenna>> GetCoSiteAntennasAsync(
        Guid stationId,
        CancellationToken stoppingToken);
}
