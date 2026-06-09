using AntennaMonitoring.Models;

namespace AntennaMonitoring.Repositories;

public interface IBaseStationRepository
{
    Task<IEnumerable<BaseStation>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BaseStation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseStation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<BaseStation> CreateAsync(BaseStation station, CancellationToken cancellationToken = default);
    Task<BaseStation?> UpdateAsync(Guid id, BaseStation station, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<BaseStation>> GetByLocationAsync(
        double minLat, double maxLat, double minLng, double maxLng,
        CancellationToken cancellationToken = default);
}

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

public interface IAlarmRepository
{
    Task<IEnumerable<Alarm>> GetAllAsync(
        string? status = null, string? level = null,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Alarm>> GetByStationIdAsync(Guid stationId,
        string? status = null, CancellationToken cancellationToken = default);
    Task<Alarm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Alarm> CreateAsync(Alarm alarm, CancellationToken cancellationToken = default);
    Task<Alarm?> AcknowledgeAsync(Guid id, string acknowledgedBy,
        CancellationToken cancellationToken = default);
    Task<Alarm?> ClearAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetActiveCountByLevelAsync(string level, CancellationToken cancellationToken = default);
    Task<IEnumerable<Alarm>> GetUnpublishedAsync(int limit = 100,
        CancellationToken cancellationToken = default);
    Task<bool> MarkAsPublishedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ICalibrationRecordRepository
{
    Task<IEnumerable<CalibrationRecord>> GetAllAsync(
        Guid? stationId = null,
        CancellationToken cancellationToken = default);
    Task<CalibrationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CalibrationRecord>> GetByStationIdAsync(Guid stationId,
        DateTime? startTime = null, DateTime? endTime = null,
        int limit = 1000, CancellationToken cancellationToken = default);
    Task<IEnumerable<CalibrationRecord>> GetByChannelIdAsync(Guid channelId,
        DateTime? startTime = null, DateTime? endTime = null,
        int limit = 1000, CancellationToken cancellationToken = default);
    Task<CalibrationRecord> CreateAsync(CalibrationRecord record,
        CancellationToken cancellationToken = default);
    Task BulkCreateAsync(IEnumerable<CalibrationRecord> records,
        CancellationToken cancellationToken = default);
    Task<double?> GetLatestSLLAsync(Guid stationId, CancellationToken cancellationToken = default);
}

public interface IDiagnosisRecordRepository
{
    Task<IEnumerable<DiagnosisRecord>> GetAllAsync(
        Guid? stationId = null, Guid? channelId = null,
        DateTime? startTime = null, DateTime? endTime = null,
        int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(
        Guid? stationId = null, Guid? channelId = null,
        DateTime? startTime = null, DateTime? endTime = null,
        CancellationToken cancellationToken = default);
    Task<DiagnosisRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DiagnosisRecord>> GetByChannelIdAsync(Guid channelId,
        DateTime? startTime = null, DateTime? endTime = null,
        int limit = 1000, CancellationToken cancellationToken = default);
    Task<IEnumerable<DiagnosisRecord>> GetByStationIdAsync(Guid stationId,
        DateTime? startTime = null, CancellationToken cancellationToken = default);
    Task<DiagnosisRecord> CreateAsync(DiagnosisRecord record,
        CancellationToken cancellationToken = default);
    Task BulkCreateAsync(IEnumerable<DiagnosisRecord> records,
        CancellationToken cancellationToken = default);
    Task<DiagnosisRecord?> GetLatestAsync(Guid channelId, CancellationToken cancellationToken = default);
}

public interface ISystemConfigRepository
{
    Task<IEnumerable<SystemConfig>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SystemConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<T?> GetValueAsync<T>(string key, CancellationToken cancellationToken = default);
    Task<bool> SetValueAsync(string key, string value, string? description = null,
        CancellationToken cancellationToken = default);
}
