using AntennaMonitoring.Data;
using AntennaMonitoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AntennaMonitoring.Repositories;

public class DeformationRecordRepository : IDeformationRecordRepository
{
    private readonly ApplicationDbContext _context;

    public DeformationRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<DeformationRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.DeformationRecords
            .Where(r => r.StationId == stationId &&
                       r.MeasurementTime >= startTime &&
                       r.MeasurementTime <= endTime)
            .OrderByDescending(r => r.MeasurementTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<DeformationRecord> AddAsync(DeformationRecord record,
        CancellationToken cancellationToken = default)
    {
        _context.DeformationRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BulkCreateAsync(IEnumerable<DeformationRecord> records,
        CancellationToken cancellationToken = default)
    {
        await _context.DeformationRecords.AddRangeAsync(records, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeformationRecord>> GetExceedingThresholdAsync(
        double thresholdMm, CancellationToken cancellationToken = default)
    {
        return await _context.DeformationRecords
            .Where(r => r.CalculatedDisplacementMm > thresholdMm)
            .Include(r => r.Station)
            .OrderByDescending(r => r.MeasurementTime)
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}

public class CoSiteInterferenceRecordRepository : ICoSiteInterferenceRecordRepository
{
    private readonly ApplicationDbContext _context;

    public CoSiteInterferenceRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CoSiteInterferenceRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.CoSiteInterferenceRecords
            .Where(r => r.StationId == stationId &&
                       r.MeasurementTime >= startTime &&
                       r.MeasurementTime <= endTime)
            .OrderByDescending(r => r.MeasurementTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<CoSiteInterferenceRecord> AddAsync(CoSiteInterferenceRecord record,
        CancellationToken cancellationToken = default)
    {
        _context.CoSiteInterferenceRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BulkCreateAsync(IEnumerable<CoSiteInterferenceRecord> records,
        CancellationToken cancellationToken = default)
    {
        await _context.CoSiteInterferenceRecords.AddRangeAsync(records, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CoSiteInterferenceRecord>> GetInsufficientIsolationAsync(
        double thresholdDb, CancellationToken cancellationToken = default)
    {
        return await _context.CoSiteInterferenceRecords
            .Where(r => !r.IsIsolationSufficient && r.IsolationDb < thresholdDb)
            .Include(r => r.Station)
            .OrderBy(r => r.IsolationDb)
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}

public class CoSiteAntennaRepository : ICoSiteAntennaRepository
{
    private readonly ApplicationDbContext _context;

    public CoSiteAntennaRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CoSiteAntennaEntity>> GetByStationIdAsync(
        Guid stationId, CancellationToken cancellationToken = default)
    {
        return await _context.CoSiteAntennas
            .Where(a => a.StationId == stationId && a.Status == "active")
            .OrderBy(a => a.OperatorName)
            .ToListAsync(cancellationToken);
    }

    public async Task<CoSiteAntennaEntity> AddAsync(CoSiteAntennaEntity antenna,
        CancellationToken cancellationToken = default)
    {
        antenna.Id = Guid.NewGuid();
        antenna.CreatedAt = DateTime.UtcNow;
        antenna.UpdatedAt = DateTime.UtcNow;
        _context.CoSiteAntennas.Add(antenna);
        await _context.SaveChangesAsync(cancellationToken);
        return antenna;
    }

    public async Task<CoSiteAntennaEntity?> UpdateAsync(Guid id, CoSiteAntennaEntity antenna,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.CoSiteAntennas.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null) return null;

        existing.OperatorName = antenna.OperatorName;
        existing.AntennaType = antenna.AntennaType;
        existing.FrequencyBandStartMhz = antenna.FrequencyBandStartMhz;
        existing.FrequencyBandEndMhz = antenna.FrequencyBandEndMhz;
        existing.TransmitPowerDbm = antenna.TransmitPowerDbm;
        existing.SeparationDistanceMeters = antenna.SeparationDistanceMeters;
        existing.AzimuthAngleDeg = antenna.AzimuthAngleDeg;
        existing.ElevationAngleDeg = antenna.ElevationAngleDeg;
        existing.HeightOffsetMeters = antenna.HeightOffsetMeters;
        existing.Status = antenna.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var antenna = await _context.CoSiteAntennas.FindAsync(new object[] { id }, cancellationToken);
        if (antenna == null) return false;

        _context.CoSiteAntennas.Remove(antenna);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class PaEfficiencyRecordRepository : IPaEfficiencyRecordRepository
{
    private readonly ApplicationDbContext _context;

    public PaEfficiencyRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PaEfficiencyRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.PaEfficiencyRecords
            .Where(r => r.StationId == stationId &&
                       r.MeasurementTime >= startTime &&
                       r.MeasurementTime <= endTime)
            .OrderByDescending(r => r.MeasurementTime)
            .Take(1000)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaEfficiencyRecord>> GetByChannelIdAndTimeRangeAsync(
        Guid channelId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.PaEfficiencyRecords
            .Where(r => r.ChannelId == channelId &&
                       r.MeasurementTime >= startTime &&
                       r.MeasurementTime <= endTime)
            .OrderBy(r => r.MeasurementTime)
            .Take(1000)
            .ToListAsync(cancellationToken);
    }

    public async Task<PaEfficiencyRecord> AddAsync(PaEfficiencyRecord record,
        CancellationToken cancellationToken = default)
    {
        _context.PaEfficiencyRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BulkCreateAsync(IEnumerable<PaEfficiencyRecord> records,
        CancellationToken cancellationToken = default)
    {
        await _context.PaEfficiencyRecords.AddRangeAsync(records, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaEfficiencyRecord>> GetNeedingReplacementAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.PaEfficiencyRecords
            .Where(r => r.NeedsReplacement)
            .Include(r => r.Station)
            .Include(r => r.Channel)
            .GroupBy(r => r.ChannelId)
            .Select(g => g.OrderByDescending(r => r.MeasurementTime).First())
            .OrderBy(r => r.EfficiencyPercent)
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}

public class SpectrumScanRecordRepository : ISpectrumScanRecordRepository
{
    private readonly ApplicationDbContext _context;

    public SpectrumScanRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SpectrumScanRecord>> GetByStationIdAndTimeRangeAsync(
        Guid stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.SpectrumScanRecords
            .Where(r => r.StationId == stationId &&
                       r.ScanTime >= startTime &&
                       r.ScanTime <= endTime)
            .OrderByDescending(r => r.ScanTime)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<SpectrumScanRecord> AddAsync(SpectrumScanRecord record,
        CancellationToken cancellationToken = default)
    {
        _context.SpectrumScanRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BulkCreateAsync(IEnumerable<SpectrumScanRecord> records,
        CancellationToken cancellationToken = default)
    {
        await _context.SpectrumScanRecords.AddRangeAsync(records, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpectrumScanRecord>> GetWithInterferenceAsync(
        Guid stationId, int limit = 10,
        CancellationToken cancellationToken = default)
    {
        return await _context.SpectrumScanRecords
            .Where(r => r.StationId == stationId && r.InterferenceCount > 0)
            .OrderByDescending(r => r.ScanTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
