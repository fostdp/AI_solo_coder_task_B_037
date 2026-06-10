using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module;

public interface IPaEfficiencyEvaluator
{
    Task<IReadOnlyList<PaEfficiencyResult>> RunEfficiencyEvaluationAsync(
        PaEfficiencyRequest request,
        CancellationToken stoppingToken);

    Task<PaEfficiencyRecord> SaveEfficiencyRecordAsync(
        Guid stationId,
        PaEfficiencyResult result,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<PaEfficiencyRecord>> GetEfficiencyHistoryAsync(
        Guid stationId,
        Guid? channelId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken);

    Task<IReadOnlyList<PaEfficiencyRecord>> GetChannelsNeedingReplacementAsync(
        CancellationToken stoppingToken);
}
