using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module.Repositories;

public interface IInfluxDBRepository
{
    Task<IReadOnlyList<ChannelMetric>> GetStationMetricsAsync(
        string stationId, DateTime startTime, DateTime endTime, CancellationToken stoppingToken);
    Task WriteEfficiencyMetricAsync(
        string stationId, PaEfficiencyResult result, CancellationToken stoppingToken);
}
