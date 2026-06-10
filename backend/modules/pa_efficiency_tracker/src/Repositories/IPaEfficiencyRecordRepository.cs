using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module.Repositories;

public interface IPaEfficiencyRecordRepository
{
    Task<PaEfficiencyRecord> AddAsync(PaEfficiencyRecord record, CancellationToken stoppingToken);
    Task<IReadOnlyList<PaEfficiencyRecord>> GetByChannelIdAndTimeRangeAsync(
        Guid channelId, DateTime startTime, DateTime endTime, CancellationToken stoppingToken);
    Task<IReadOnlyList<PaEfficiencyRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime, CancellationToken stoppingToken);
    Task<IReadOnlyList<PaEfficiencyRecord>> GetNeedingReplacementAsync(CancellationToken stoppingToken);
}
