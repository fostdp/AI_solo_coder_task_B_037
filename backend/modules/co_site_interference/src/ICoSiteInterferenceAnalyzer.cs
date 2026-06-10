using CoSiteInterference.Module.Models;

namespace CoSiteInterference.Module;

public interface ICoSiteInterferenceAnalyzer
{
    Task<IReadOnlyList<CoSiteInterferenceResult>> RunInterferenceAnalysisAsync(
        CoSiteInterferenceRequest request,
        CancellationToken stoppingToken);

    Task<object> SaveInterferenceRecordAsync(
        Guid stationId,
        CoSiteAntenna interferingAntenna,
        CoSiteInterferenceResult result,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<object>> GetInterferenceHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<CoSiteAntenna>> GetCoSiteAntennasAsync(
        Guid stationId,
        CancellationToken stoppingToken);
}
