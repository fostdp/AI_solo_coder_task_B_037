using AntennaMonitoring.Models;

namespace AntennaMonitoring.Repositories;

public interface IDeformationRecordRepository
{
    Task<IReadOnlyList<DeformationRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task<DeformationRecord> AddAsync(DeformationRecord record,
        CancellationToken cancellationToken = default);
    Task BulkCreateAsync(IEnumerable<DeformationRecord> records,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeformationRecord>> GetExceedingThresholdAsync(
        double thresholdMm, CancellationToken cancellationToken = default);
}

public interface ICoSiteInterferenceRecordRepository
{
    Task<IReadOnlyList<CoSiteInterferenceRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task<CoSiteInterferenceRecord> AddAsync(CoSiteInterferenceRecord record,
        CancellationToken cancellationToken = default);
    Task BulkCreateAsync(IEnumerable<CoSiteInterferenceRecord> records,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoSiteInterferenceRecord>> GetInsufficientIsolationAsync(
        double thresholdDb, CancellationToken cancellationToken = default);
}

public interface ICoSiteAntennaRepository
{
    Task<IReadOnlyList<CoSiteAntennaEntity>> GetByStationIdAsync(
        Guid stationId, CancellationToken cancellationToken = default);
    Task<CoSiteAntennaEntity> AddAsync(CoSiteAntennaEntity antenna,
        CancellationToken cancellationToken = default);
    Task<CoSiteAntennaEntity?> UpdateAsync(Guid id, CoSiteAntennaEntity antenna,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPaEfficiencyRecordRepository
{
    Task<IReadOnlyList<PaEfficiencyRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaEfficiencyRecord>> GetByChannelIdAndTimeRangeAsync(
        Guid channelId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default);
    Task<PaEfficiencyRecord> AddAsync(PaEfficiencyRecord record,
        CancellationToken cancellationToken = default);
    Task BulkCreateAsync(IEnumerable<PaEfficiencyRecord> records,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaEfficiencyRecord>> GetNeedingReplacementAsync(
        CancellationToken cancellationToken = default);
}

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
