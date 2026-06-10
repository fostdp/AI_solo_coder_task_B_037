using SpectrumScanner.Module.Models;

namespace SpectrumScanner.Module;

public interface IChannelRepository
{
    Task<IEnumerable<Channel>> GetByStationIdAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<Channel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Channel?> GetByIndexAsync(Guid stationId, int channelIndex, CancellationToken cancellationToken = default);
    Task<bool> UpdateCalibrationCoeffAsync(Guid channelId,
        decimal amplitudeCoeff, decimal phaseCoeff,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(Guid channelId, string status,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateFailureProbabilityAsync(Guid channelId, decimal probability,
        CancellationToken cancellationToken = default);
    Task<int> GetCountByStatusAsync(Guid stationId, string status,
        CancellationToken cancellationToken = default);
    Task BulkUpdateAsync(IEnumerable<Channel> channels, CancellationToken cancellationToken = default);
}
