using AntennaMonitoring.Algorithms;
using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Services;

public interface IAlarmService
{
    Task<Alarm?> CreateAlarmAsync(CreateAlarmDTO dto, CancellationToken cancellationToken = default);
    Task<Alarm?> AcknowledgeAsync(Guid id, AcknowledgeAlarmDTO dto, CancellationToken cancellationToken = default);
    Task<Alarm?> ClearAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AlarmSummaryDTO> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task CheckAndCreateChannelAlarmsAsync(Guid stationId, Guid channelId,
        double swr, double temperature, CancellationToken cancellationToken = default);
    Task CheckSectorFailureAsync(Guid stationId, CancellationToken cancellationToken = default);
}

public class AlarmService : IAlarmService
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly DiagnosisOptions _diagnosisOptions;

    public AlarmService(
        IAlarmRepository alarmRepository,
        IChannelRepository channelRepository,
        IOptions<DiagnosisOptions> options)
    {
        _alarmRepository = alarmRepository;
        _channelRepository = channelRepository;
        _diagnosisOptions = options.Value;
    }

    public async Task<Alarm?> CreateAlarmAsync(CreateAlarmDTO dto, CancellationToken cancellationToken = default)
    {
        var existing = (await _alarmRepository.GetByStationIdAsync(dto.StationId, "active", cancellationToken))
            .FirstOrDefault(a => a.AlarmCode == dto.AlarmCode &&
                               a.ChannelId == dto.ChannelId &&
                               a.Status == "active");

        if (existing != null)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ActualValue = dto.ActualValue;
            return existing;
        }

        var alarm = new Alarm
        {
            AlarmCode = dto.AlarmCode,
            AlarmType = dto.AlarmType,
            AlarmLevel = dto.AlarmLevel,
            StationId = dto.StationId,
            ChannelId = dto.ChannelId,
            Title = dto.Title,
            Description = dto.Description,
            ThresholdValue = dto.ThresholdValue,
            ActualValue = dto.ActualValue,
            Status = "active"
        };

        return await _alarmRepository.CreateAsync(alarm, cancellationToken);
    }

    public async Task<Alarm?> AcknowledgeAsync(Guid id, AcknowledgeAlarmDTO dto, CancellationToken cancellationToken = default)
    {
        return await _alarmRepository.AcknowledgeAsync(id, dto.AcknowledgedBy, cancellationToken);
    }

    public async Task<Alarm?> ClearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _alarmRepository.ClearAsync(id, cancellationToken);
    }

    public async Task<AlarmSummaryDTO> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var activeAlarms = await _alarmRepository.GetAllAsync("active", null, cancellationToken);
        var list = activeAlarms.ToList();

        return new AlarmSummaryDTO
        {
            TotalActive = list.Count,
            Critical = list.Count(a => a.AlarmLevel == "critical"),
            Warning = list.Count(a => a.AlarmLevel == "warning"),
            Info = list.Count(a => a.AlarmLevel == "info"),
            Acknowledged = list.Count(a => a.Acknowledged),
            Unacknowledged = list.Count(a => !a.Acknowledged)
        };
    }

    public async Task CheckAndCreateChannelAlarmsAsync(Guid stationId, Guid channelId,
        double swr, double temperature, CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId, cancellationToken);
        if (channel == null) return;

        if (swr >= _diagnosisOptions.SWRAlarmThreshold)
        {
            await CreateAlarmAsync(new CreateAlarmDTO
            {
                AlarmCode = "SWR_HIGH",
                AlarmType = "channel",
                AlarmLevel = "critical",
                StationId = stationId,
                ChannelId = channelId,
                Title = $"Channel {channel.ChannelIndex} SWR Exceeded",
                Description = $"Channel {channel.ChannelIndex} SWR ({swr:F2}) exceeds threshold {_diagnosisOptions.SWRAlarmThreshold}",
                ThresholdValue = (decimal)_diagnosisOptions.SWRAlarmThreshold,
                ActualValue = (decimal)swr
            }, cancellationToken);

            await _channelRepository.UpdateStatusAsync(channelId, "fault", cancellationToken);
        }
        else if (swr >= 1.5)
        {
            await CreateAlarmAsync(new CreateAlarmDTO
            {
                AlarmCode = "SWR_WARNING",
                AlarmType = "channel",
                AlarmLevel = "warning",
                StationId = stationId,
                ChannelId = channelId,
                Title = $"Channel {channel.ChannelIndex} SWR Warning",
                Description = $"Channel {channel.ChannelIndex} SWR ({swr:F2}) is approaching threshold",
                ThresholdValue = 1.5m,
                ActualValue = (decimal)swr
            }, cancellationToken);

            await _channelRepository.UpdateStatusAsync(channelId, "warning", cancellationToken);
        }

        if (temperature > 80.0)
        {
            await CreateAlarmAsync(new CreateAlarmDTO
            {
                AlarmCode = "PA_TEMP_HIGH",
                AlarmType = "channel",
                AlarmLevel = "critical",
                StationId = stationId,
                ChannelId = channelId,
                Title = $"Channel {channel.ChannelIndex} PA Temperature High",
                Description = $"Channel {channel.ChannelIndex} PA temperature ({temperature:F1}°C) exceeds safe limit",
                ThresholdValue = 80.0m,
                ActualValue = (decimal)temperature
            }, cancellationToken);
        }
    }

    public async Task CheckSectorFailureAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var channels = (await _channelRepository.GetByStationIdAsync(stationId, cancellationToken)).ToList();
        int totalChannels = channels.Count;
        int faultChannels = channels.Count(c => c.Status == "fault");

        double faultRatio = (double)faultChannels / totalChannels;

        if (faultRatio >= _diagnosisOptions.SectorFailureChannelRatio)
        {
            var existingSectorAlarm = (await _alarmRepository.GetByStationIdAsync(stationId, "active", cancellationToken))
                .FirstOrDefault(a => a.AlarmCode == "SECTOR_FAILURE" && a.Status == "active");

            if (existingSectorAlarm == null)
            {
                await CreateAlarmAsync(new CreateAlarmDTO
                {
                    AlarmCode = "SECTOR_FAILURE",
                    AlarmType = "sector",
                    AlarmLevel = "critical",
                    StationId = stationId,
                    ChannelId = null,
                    Title = "Sector Failure Imminent",
                    Description = $"More than {_diagnosisOptions.SectorFailureChannelRatio:P0} of channels ({faultChannels}/{totalChannels}) are in fault state. Sector failure risk is high.",
                    ThresholdValue = (decimal)_diagnosisOptions.SectorFailureChannelRatio,
                    ActualValue = (decimal)faultRatio
                }, cancellationToken);
            }
        }
    }
}
