using SpectrumScanner.Module.Models;

namespace SpectrumScanner.Module;

public interface ISpectrumScanRecordRepository
{
    Task<IReadOnlyList<SpectrumScanRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task<SpectrumScanRecord> AddAsync(SpectrumScanRecord record,
        CancellationToken cancellationToken = default);
    Task BulkCreateAsync(IEnumerable<SpectrumScanRecord> records,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpectrumScanRecord>> GetWithInterferenceAsync(
        Guid stationId, int limit = 10,
        CancellationToken cancellationToken = default);
}
