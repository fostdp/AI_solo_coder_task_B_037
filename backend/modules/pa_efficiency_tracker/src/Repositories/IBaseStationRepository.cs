using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module.Repositories;

public interface IBaseStationRepository
{
    Task<IReadOnlyList<BaseStation>> GetAllAsync(CancellationToken stoppingToken);
    Task<BaseStation?> GetByIdAsync(Guid stationId, CancellationToken stoppingToken);
}
