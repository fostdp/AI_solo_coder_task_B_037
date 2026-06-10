using AntennaMonitoring.Models;

namespace AntennaMonitoring.Repositories;

public interface IInfluxDBRepository
{
    Task WriteChannelMetricsAsync(IEnumerable<ChannelMetrics> metrics, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChannelMetrics>> GetChannelMetricsAsync(
        string channelId, DateTime startTime, DateTime endTime,
        string aggregation = "raw", CancellationToken cancellationToken = default);
    Task<IEnumerable<ChannelMetrics>> GetStationMetricsAsync(
        string stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task<ChannelMetrics?> GetLatestChannelMetricsAsync(
        string channelId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChannelMetrics>> GetLatestStationMetricsAsync(
        string stationId, CancellationToken cancellationToken = default);
    Task<double?> GetMetricAverageAsync(
        string channelId, string field,
        DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task WriteBeamformingMetricsAsync(
        Guid stationId, string algorithm,
        double sll, double sllBefore, double sllAfter,
        double beamwidthAz, double beamwidthEl,
        bool converged, CancellationToken cancellationToken = default);
    Task WriteDiagnosisMetricsAsync(
        Guid stationId, Guid channelId, string modelType,
        double failureProbability, double swrPredicted,
        double temperaturePredicted, double anomalyScore,
        int predictedFailureHours, double healthScore,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<double[]>> GetBeamPatternHistoryAsync(
        Guid stationId, int limit = 10,
        CancellationToken cancellationToken = default);

    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
