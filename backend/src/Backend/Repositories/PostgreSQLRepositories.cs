using AntennaMonitoring.Data;
using AntennaMonitoring.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace AntennaMonitoring.Repositories;

public class BaseStationRepository : IBaseStationRepository
{
    private readonly ApplicationDbContext _context;

    public BaseStationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<BaseStation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.BaseStations
            .Include(s => s.Channels)
            .Include(s => s.Alarms.Where(a => a.Status == "active"))
            .OrderBy(s => s.StationCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<BaseStation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.BaseStations
            .Include(s => s.Channels)
            .Include(s => s.Alarms)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<BaseStation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.BaseStations
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.StationCode == code, cancellationToken);
    }

    public async Task<BaseStation> CreateAsync(BaseStation station, CancellationToken cancellationToken = default)
    {
        station.Id = Guid.NewGuid();
        station.CreatedAt = DateTime.UtcNow;
        station.UpdatedAt = DateTime.UtcNow;

        if (station.Longitude != 0 && station.Latitude != 0)
        {
            station.Location = new Point((double)station.Longitude, (double)station.Latitude) { SRID = 4326 };
        }

        _context.BaseStations.Add(station);
        await _context.SaveChangesAsync(cancellationToken);
        return station;
    }

    public async Task<BaseStation?> UpdateAsync(Guid id, BaseStation station, CancellationToken cancellationToken = default)
    {
        var existing = await _context.BaseStations.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null) return null;

        existing.StationName = station.StationName;
        existing.Address = station.Address;
        existing.Longitude = station.Longitude;
        existing.Latitude = station.Latitude;
        existing.Status = station.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        if (station.Longitude != 0 && station.Latitude != 0)
        {
            existing.Location = new Point((double)station.Longitude, (double)station.Latitude) { SRID = 4326 };
        }

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var station = await _context.BaseStations.FindAsync(new object[] { id }, cancellationToken);
        if (station == null) return false;

        _context.BaseStations.Remove(station);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IEnumerable<BaseStation>> GetByLocationAsync(
        double minLat, double maxLat, double minLng, double maxLng,
        CancellationToken cancellationToken = default)
    {
        return await _context.BaseStations
            .Where(s => s.Latitude >= (decimal)minLat && s.Latitude <= (decimal)maxLat &&
                       s.Longitude >= (decimal)minLng && s.Longitude <= (decimal)maxLng)
            .Include(s => s.Alarms.Where(a => a.Status == "active"))
            .ToListAsync(cancellationToken);
    }
}

public class ChannelRepository : IChannelRepository
{
    private readonly ApplicationDbContext _context;

    public ChannelRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Channel>> GetByStationIdAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        return await _context.Channels
            .Where(c => c.StationId == stationId)
            .OrderBy(c => c.ChannelIndex)
            .ToListAsync(cancellationToken);
    }

    public async Task<Channel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Channels
            .Include(c => c.Station)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Channel?> GetByIndexAsync(Guid stationId, int channelIndex, CancellationToken cancellationToken = default)
    {
        return await _context.Channels
            .FirstOrDefaultAsync(c => c.StationId == stationId && c.ChannelIndex == channelIndex,
                cancellationToken);
    }

    public async Task<bool> UpdateCalibrationCoeffAsync(Guid channelId,
        decimal amplitudeCoeff, decimal phaseCoeff, CancellationToken cancellationToken = default)
    {
        var channel = await _context.Channels.FindAsync(new object[] { channelId }, cancellationToken);
        if (channel == null) return false;

        channel.CalibrationCoeffAmplitude = amplitudeCoeff;
        channel.CalibrationCoeffPhase = phaseCoeff;
        channel.LastCalibrationTime = DateTime.UtcNow;
        channel.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateStatusAsync(Guid channelId, string status,
        CancellationToken cancellationToken = default)
    {
        var channel = await _context.Channels.FindAsync(new object[] { channelId }, cancellationToken);
        if (channel == null) return false;

        channel.Status = status;
        channel.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateFailureProbabilityAsync(Guid channelId, decimal probability,
        CancellationToken cancellationToken = default)
    {
        var channel = await _context.Channels.FindAsync(new object[] { channelId }, cancellationToken);
        if (channel == null) return false;

        channel.FailureProbability = probability;
        channel.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> GetCountByStatusAsync(Guid stationId, string status,
        CancellationToken cancellationToken = default)
    {
        return await _context.Channels
            .CountAsync(c => c.StationId == stationId && c.Status == status, cancellationToken);
    }

    public async Task BulkUpdateAsync(IEnumerable<Channel> channels, CancellationToken cancellationToken = default)
    {
        foreach (var channel in channels)
        {
            _context.Entry(channel).State = EntityState.Modified;
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class AlarmRepository : IAlarmRepository
{
    private readonly ApplicationDbContext _context;

    public AlarmRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Alarm>> GetAllAsync(
        string? status = null, string? level = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Alarms
            .Include(a => a.Station)
            .Include(a => a.Channel)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrEmpty(level))
            query = query.Where(a => a.AlarmLevel == level);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Alarm>> GetByStationIdAsync(Guid stationId,
        string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Alarms
            .Include(a => a.Channel)
            .Where(a => a.StationId == stationId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<Alarm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Alarms
            .Include(a => a.Station)
            .Include(a => a.Channel)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Alarm> CreateAsync(Alarm alarm, CancellationToken cancellationToken = default)
    {
        alarm.Id = Guid.NewGuid();
        alarm.CreatedAt = DateTime.UtcNow;
        alarm.UpdatedAt = DateTime.UtcNow;

        _context.Alarms.Add(alarm);
        await _context.SaveChangesAsync(cancellationToken);
        return alarm;
    }

    public async Task<Alarm?> AcknowledgeAsync(Guid id, string acknowledgedBy,
        CancellationToken cancellationToken = default)
    {
        var alarm = await _context.Alarms.FindAsync(new object[] { id }, cancellationToken);
        if (alarm == null) return null;

        alarm.Acknowledged = true;
        alarm.AcknowledgedBy = acknowledgedBy;
        alarm.AcknowledgedAt = DateTime.UtcNow;
        alarm.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return alarm;
    }

    public async Task<Alarm?> ClearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var alarm = await _context.Alarms.FindAsync(new object[] { id }, cancellationToken);
        if (alarm == null) return null;

        alarm.Status = "cleared";
        alarm.ClearedAt = DateTime.UtcNow;
        alarm.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return alarm;
    }

    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Alarms
            .CountAsync(a => a.Status == "active", cancellationToken);
    }

    public async Task<int> GetActiveCountByLevelAsync(string level, CancellationToken cancellationToken = default)
    {
        return await _context.Alarms
            .CountAsync(a => a.Status == "active" && a.AlarmLevel == level, cancellationToken);
    }

    public async Task<IEnumerable<Alarm>> GetUnpublishedAsync(int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _context.Alarms
            .Include(a => a.Station)
            .Include(a => a.Channel)
            .Where(a => !a.MqttPublished && a.Status == "active")
            .OrderBy(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkAsPublishedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var alarm = await _context.Alarms.FindAsync(new object[] { id }, cancellationToken);
        if (alarm == null) return false;

        alarm.MqttPublished = true;
        alarm.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var alarm = await _context.Alarms.FindAsync(new object[] { id }, cancellationToken);
        if (alarm == null) return false;

        _context.Alarms.Remove(alarm);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class CalibrationRecordRepository : ICalibrationRecordRepository
{
    private readonly ApplicationDbContext _context;

    public CalibrationRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<CalibrationRecord>> GetAllAsync(
        Guid? stationId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CalibrationRecords
            .Include(c => c.Station)
            .Include(c => c.Channel)
            .AsQueryable();

        if (stationId.HasValue)
        {
            query = query.Where(c => c.StationId == stationId.Value);
        }

        return await query
            .OrderByDescending(c => c.CalibrationTime)
            .Take(1000)
            .ToListAsync(cancellationToken);
    }

    public async Task<CalibrationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CalibrationRecords
            .Include(c => c.Station)
            .Include(c => c.Channel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<CalibrationRecord>> GetByStationIdAsync(Guid stationId,
        DateTime? startTime = null, DateTime? endTime = null,
        int limit = 1000, CancellationToken cancellationToken = default)
    {
        var query = _context.CalibrationRecords
            .Where(c => c.StationId == stationId)
            .AsQueryable();

        if (startTime.HasValue)
            query = query.Where(c => c.CalibrationTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(c => c.CalibrationTime <= endTime.Value);

        return await query
            .OrderByDescending(c => c.CalibrationTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<CalibrationRecord>> GetByChannelIdAsync(Guid channelId,
        DateTime? startTime = null, DateTime? endTime = null,
        int limit = 1000, CancellationToken cancellationToken = default)
    {
        var query = _context.CalibrationRecords
            .Where(c => c.ChannelId == channelId)
            .AsQueryable();

        if (startTime.HasValue)
            query = query.Where(c => c.CalibrationTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(c => c.CalibrationTime <= endTime.Value);

        return await query
            .OrderByDescending(c => c.CalibrationTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<CalibrationRecord> CreateAsync(CalibrationRecord record,
        CancellationToken cancellationToken = default)
    {
        record.Id = Guid.NewGuid();
        record.CreatedAt = DateTime.UtcNow;

        _context.CalibrationRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BulkCreateAsync(IEnumerable<CalibrationRecord> records,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        foreach (var record in recordList)
        {
            record.Id = Guid.NewGuid();
            record.CreatedAt = DateTime.UtcNow;
        }

        _context.CalibrationRecords.AddRange(recordList);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<double?> GetLatestSLLAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var latest = await _context.CalibrationRecords
            .Where(c => c.StationId == stationId)
            .OrderByDescending(c => c.CalibrationTime)
            .Select(c => c.SllAfter)
            .FirstOrDefaultAsync(cancellationToken);

        return latest.HasValue ? (double)latest.Value : null;
    }
}

public class DiagnosisRecordRepository : IDiagnosisRecordRepository
{
    private readonly ApplicationDbContext _context;

    public DiagnosisRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<DiagnosisRecord>> GetAllAsync(
        Guid? stationId = null, Guid? channelId = null,
        DateTime? startTime = null, DateTime? endTime = null,
        int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DiagnosisRecords
            .Include(d => d.Station)
            .Include(d => d.Channel)
            .AsQueryable();

        if (stationId.HasValue)
            query = query.Where(d => d.StationId == stationId.Value);

        if (channelId.HasValue)
            query = query.Where(d => d.ChannelId == channelId.Value);

        if (startTime.HasValue)
            query = query.Where(d => d.DiagnosisTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(d => d.DiagnosisTime <= endTime.Value);

        return await query
            .OrderByDescending(d => d.DiagnosisTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(
        Guid? stationId = null, Guid? channelId = null,
        DateTime? startTime = null, DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DiagnosisRecords.AsQueryable();

        if (stationId.HasValue)
            query = query.Where(d => d.StationId == stationId.Value);

        if (channelId.HasValue)
            query = query.Where(d => d.ChannelId == channelId.Value);

        if (startTime.HasValue)
            query = query.Where(d => d.DiagnosisTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(d => d.DiagnosisTime <= endTime.Value);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<DiagnosisRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DiagnosisRecords
            .Include(d => d.Station)
            .Include(d => d.Channel)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<DiagnosisRecord>> GetByChannelIdAsync(Guid channelId,
        DateTime? startTime = null, DateTime? endTime = null,
        int limit = 1000, CancellationToken cancellationToken = default)
    {
        var query = _context.DiagnosisRecords
            .Where(d => d.ChannelId == channelId)
            .AsQueryable();

        if (startTime.HasValue)
            query = query.Where(d => d.DiagnosisTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(d => d.DiagnosisTime <= endTime.Value);

        return await query
            .OrderByDescending(d => d.DiagnosisTime)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DiagnosisRecord>> GetByStationIdAsync(Guid stationId,
        DateTime? startTime = null, CancellationToken cancellationToken = default)
    {
        var query = _context.DiagnosisRecords
            .Where(d => d.StationId == stationId)
            .AsQueryable();

        if (startTime.HasValue)
            query = query.Where(d => d.DiagnosisTime >= startTime.Value);

        return await query
            .OrderByDescending(d => d.DiagnosisTime)
            .Take(1000)
            .ToListAsync(cancellationToken);
    }

    public async Task<DiagnosisRecord> CreateAsync(DiagnosisRecord record,
        CancellationToken cancellationToken = default)
    {
        record.Id = Guid.NewGuid();
        record.CreatedAt = DateTime.UtcNow;

        _context.DiagnosisRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BulkCreateAsync(IEnumerable<DiagnosisRecord> records,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        foreach (var record in recordList)
        {
            record.Id = Guid.NewGuid();
            record.CreatedAt = DateTime.UtcNow;
        }

        _context.DiagnosisRecords.AddRange(recordList);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DiagnosisRecord?> GetLatestAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        return await _context.DiagnosisRecords
            .Where(d => d.ChannelId == channelId)
            .OrderByDescending(d => d.DiagnosisTime)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public class SystemConfigRepository : ISystemConfigRepository
{
    private readonly ApplicationDbContext _context;

    public SystemConfigRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<SystemConfig>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SystemConfigs
            .OrderBy(c => c.ConfigKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<SystemConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.SystemConfigs
            .FirstOrDefaultAsync(c => c.ConfigKey == key, cancellationToken);
    }

    public async Task<T?> GetValueAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var config = await GetByKeyAsync(key, cancellationToken);
        if (config == null || string.IsNullOrEmpty(config.ConfigValue))
            return default;

        try
        {
            return (T?)Convert.ChangeType(config.ConfigValue, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    public async Task<bool> SetValueAsync(string key, string value, string? description = null,
        CancellationToken cancellationToken = default)
    {
        var config = await GetByKeyAsync(key, cancellationToken);
        if (config == null)
        {
            config = new SystemConfig
            {
                Id = Guid.NewGuid(),
                ConfigKey = key,
                ConfigValue = value,
                Description = description,
                UpdatedAt = DateTime.UtcNow
            };
            _context.SystemConfigs.Add(config);
        }
        else
        {
            config.ConfigValue = value;
            if (!string.IsNullOrEmpty(description))
                config.Description = description;
            config.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class ECPRIDataRepository : IECPRIDataRepository
{
    private readonly ApplicationDbContext _context;

    public ECPRIDataRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<long> GetTotalPacketsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CalibrationRecords
            .LongCountAsync(cancellationToken);
    }

    public async Task<long> GetPacketsByStationAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var stationGuid = Guid.Parse(stationId);
        return await _context.CalibrationRecords
            .Where(c => c.StationId == stationGuid)
            .LongCountAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastPacketTimeAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CalibrationRecords
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
