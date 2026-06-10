using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module.Repositories;

public interface IChannelRepository
{
    Task<IReadOnlyList<Channel>> GetByStationIdAsync(Guid stationId, CancellationToken stoppingToken);
    Task<Channel?> GetByIdAsync(Guid channelId, CancellationToken stoppingToken);
}
