using AntennaMonitoring.Algorithms;
using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BaseStationsController : ControllerBase
{
    private readonly IBaseStationRepository _baseStationRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IAlarmRepository _alarmRepository;
    private readonly ICalibrationService _calibrationService;
    private readonly IDiagnosisService _diagnosisService;
    private readonly IBeamformingCalibration _beamformingCalibration;
    private readonly IInfluxDBRepository _influxDBRepository;

    public BaseStationsController(
        IBaseStationRepository baseStationRepository,
        IChannelRepository channelRepository,
        IAlarmRepository alarmRepository,
        ICalibrationService calibrationService,
        IDiagnosisService diagnosisService,
        IBeamformingCalibration beamformingCalibration,
        IInfluxDBRepository influxDBRepository)
    {
        _baseStationRepository = baseStationRepository;
        _channelRepository = channelRepository;
        _alarmRepository = alarmRepository;
        _calibrationService = calibrationService;
        _diagnosisService = diagnosisService;
        _beamformingCalibration = beamformingCalibration;
        _influxDBRepository = influxDBRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BaseStationDTO>>> GetBaseStations(
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var stations = await _baseStationRepository.GetAllAsync(cancellationToken);
        var pagedStations = stations
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);

        var dtos = new List<BaseStationDTO>();
        foreach (var station in pagedStations)
        {
            dtos.Add(await MapToBaseStationDTO(station, cancellationToken));
        }

        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BaseStationDTO>> GetBaseStation(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (station == null)
        {
            return NotFound();
        }

        var dto = await MapToBaseStationDTO(station, cancellationToken);
        return Ok(dto);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<BaseStationSummaryDTO>>> GetBaseStationsSummary(
        CancellationToken cancellationToken = default)
    {
        var stations = await _baseStationRepository.GetAllAsync(cancellationToken);
        var dtos = new List<BaseStationSummaryDTO>();

        foreach (var station in stations)
        {
            var alarms = await _alarmRepository.GetByStationIdAsync(station.Id, "active", cancellationToken);
            var alarmList = alarms.ToList();

            dtos.Add(new BaseStationSummaryDTO
            {
                Id = station.Id,
                StationName = station.StationName,
                StationCode = station.StationCode,
                Longitude = station.Longitude,
                Latitude = station.Latitude,
                Status = station.Status,
                ActiveAlarms = alarmList.Count,
                CriticalAlarms = alarmList.Count(a => a.AlarmLevel == "critical"),
                WarningAlarms = alarmList.Count(a => a.AlarmLevel == "warning")
            });
        }

        return Ok(dtos);
    }

    [HttpPost]
    public async Task<ActionResult<BaseStationDTO>> PostBaseStation(
        CreateBaseStationDTO dto,
        CancellationToken cancellationToken = default)
    {
        var existingStation = await _baseStationRepository.GetByCodeAsync(dto.StationCode, cancellationToken);
        if (existingStation != null)
        {
            return Conflict(new { message = $"基站代码 {dto.StationCode} 已存在" });
        }

        var station = new BaseStation
        {
            Id = Guid.NewGuid(),
            StationName = dto.StationName,
            StationCode = dto.StationCode,
            Address = dto.Address,
            Longitude = dto.Longitude,
            Latitude = dto.Latitude,
            Altitude = dto.Altitude,
            AntennaModel = dto.AntennaModel,
            ChannelCount = dto.ChannelCount,
            ArrayRows = dto.ArrayRows,
            ArrayColumns = dto.ArrayColumns,
            FrequencyBand = dto.FrequencyBand,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createdStation = await _baseStationRepository.CreateAsync(station, cancellationToken);

        var channels = CreateChannelsForStation(createdStation.Id, dto.ChannelCount, dto.ArrayRows, dto.ArrayColumns);
        foreach (var channel in channels)
        {
            createdStation.Channels.Add(channel);
        }

        var result = await MapToBaseStationDTO(createdStation, cancellationToken);
        return CreatedAtAction(nameof(GetBaseStation), new { id = createdStation.Id }, result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutBaseStation(
        Guid id,
        UpdateBaseStationDTO dto,
        CancellationToken cancellationToken = default)
    {
        var existingStation = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (existingStation == null)
        {
            return NotFound();
        }

        existingStation.StationName = dto.StationName;
        existingStation.Address = dto.Address;
        if (dto.Longitude.HasValue)
        {
            existingStation.Longitude = dto.Longitude.Value;
        }
        if (dto.Latitude.HasValue)
        {
            existingStation.Latitude = dto.Latitude.Value;
        }
        existingStation.Status = dto.Status;
        existingStation.UpdatedAt = DateTime.UtcNow;

        var updatedStation = await _baseStationRepository.UpdateAsync(id, existingStation, cancellationToken);
        if (updatedStation == null)
        {
            return NotFound();
        }

        var result = await MapToBaseStationDTO(updatedStation, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBaseStation(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _baseStationRepository.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpGet("{id}/channels")]
    public async Task<ActionResult<IEnumerable<ChannelDTO>>> GetStationChannels(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (station == null)
        {
            return NotFound();
        }

        var channels = await _channelRepository.GetByStationIdAsync(id, cancellationToken);
        var dtos = new List<ChannelDTO>();

        foreach (var channel in channels)
        {
            var latestMetrics = await _influxDBRepository.GetLatestChannelMetricsAsync(channel.Id.ToString(), cancellationToken);
            dtos.Add(MapToChannelDTO(channel, latestMetrics));
        }

        return Ok(dtos);
    }

    [HttpGet("{id}/alarms")]
    public async Task<ActionResult<IEnumerable<AlarmDTO>>> GetStationAlarms(
        Guid id,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (station == null)
        {
            return NotFound();
        }

        var alarms = await _alarmRepository.GetByStationIdAsync(id, status, cancellationToken);
        var dtos = alarms.Select(MapToAlarmDTO);

        return Ok(dtos);
    }

    [HttpGet("{id}/calibrate")]
    public async Task<ActionResult<IEnumerable<CalibrationResultDTO>>> CalibrateStation(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (station == null)
        {
            return NotFound();
        }

        var result = await _calibrationService.RunCalibrationAsync(id, cancellationToken);

        if (!result.Success && !result.Converged)
        {
            return BadRequest(new { message = "校准失败，请检查基站通道和数据" });
        }

        var dtos = result.ChannelCalibrations.Select(cc => new CalibrationResultDTO
        {
            ChannelId = cc.ChannelId,
            ChannelIndex = cc.ChannelIndex,
            AmplitudeDeviation = cc.AmplitudeDeviation,
            PhaseDeviation = cc.PhaseDeviation,
            CalibrationCoeffAmplitude = cc.CalibrationCoeffAmplitude,
            CalibrationCoeffPhase = cc.CalibrationCoeffPhase,
            SllBefore = result.SllBefore,
            SllAfter = result.SllAfter,
            Algorithm = result.Algorithm,
            CalibrationTime = result.CalibrationTime
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id}/diagnose")]
    public async Task<ActionResult<IEnumerable<DiagnosisResultDTO>>> DiagnoseStation(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (station == null)
        {
            return NotFound();
        }

        var results = await _diagnosisService.RunDiagnosisAsync(id, cancellationToken);

        var dtos = results.Select(r => new DiagnosisResultDTO
        {
            ChannelId = r.ChannelId,
            ChannelIndex = r.ChannelIndex,
            SwrValue = r.SwrValue,
            TemperatureValue = r.TemperatureValue,
            FailureProbability = r.FailureProbability,
            ModelType = r.ModelType,
            PredictionHorizonHours = r.PredictionHorizonHours,
            Recommendation = r.Recommendation,
            DiagnosisTime = r.DiagnosisTime
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id}/beampattern")]
    public async Task<ActionResult<BeamPatternDTO>> GetBeamPattern(
        Guid id,
        double azimuth = 0,
        double elevation = 0,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(id, cancellationToken);
        if (station == null)
        {
            return NotFound();
        }

        var beamPattern = await _calibrationService.CalculateBeamPatternAsync(id, azimuth, elevation, cancellationToken);
        return Ok(beamPattern);
    }

    private async Task<BaseStationDTO> MapToBaseStationDTO(BaseStation station, CancellationToken cancellationToken)
    {
        var channels = (await _channelRepository.GetByStationIdAsync(station.Id, cancellationToken)).ToList();
        var alarms = (await _alarmRepository.GetByStationIdAsync(station.Id, "active", cancellationToken)).ToList();

        return new BaseStationDTO
        {
            Id = station.Id,
            StationName = station.StationName,
            StationCode = station.StationCode,
            Address = station.Address,
            Longitude = station.Longitude,
            Latitude = station.Latitude,
            Altitude = station.Altitude,
            AntennaModel = station.AntennaModel,
            ChannelCount = station.ChannelCount,
            ArrayRows = station.ArrayRows,
            ArrayColumns = station.ArrayColumns,
            FrequencyBand = station.FrequencyBand,
            Status = station.Status,
            NormalChannels = await _channelRepository.GetCountByStatusAsync(station.Id, "normal", cancellationToken),
            WarningChannels = await _channelRepository.GetCountByStatusAsync(station.Id, "warning", cancellationToken),
            FaultChannels = await _channelRepository.GetCountByStatusAsync(station.Id, "fault", cancellationToken),
            ActiveAlarms = alarms.Count
        };
    }

    private static ChannelDTO MapToChannelDTO(Channel channel, ChannelMetrics? latestMetrics)
    {
        return new ChannelDTO
        {
            Id = channel.Id,
            StationId = channel.StationId,
            ChannelIndex = channel.ChannelIndex,
            RowIndex = channel.RowIndex,
            ColumnIndex = channel.ColumnIndex,
            TxPower = channel.TxPower,
            NominalAmplitude = channel.NominalAmplitude,
            NominalPhase = channel.NominalPhase,
            CalibrationCoeffAmplitude = channel.CalibrationCoeffAmplitude,
            CalibrationCoeffPhase = channel.CalibrationCoeffPhase,
            LastCalibrationTime = channel.LastCalibrationTime,
            Status = channel.Status,
            FailureProbability = channel.FailureProbability,
            CurrentAmplitude = latestMetrics?.Amplitude ?? 0,
            CurrentPhase = latestMetrics?.Phase ?? 0,
            CurrentSwr = latestMetrics?.Swr ?? 0,
            CurrentTemperature = latestMetrics?.PaTemperature ?? 0
        };
    }

    private static AlarmDTO MapToAlarmDTO(Alarm alarm)
    {
        return new AlarmDTO
        {
            Id = alarm.Id,
            AlarmCode = alarm.AlarmCode,
            AlarmType = alarm.AlarmType,
            AlarmLevel = alarm.AlarmLevel,
            StationId = alarm.StationId,
            StationName = alarm.Station?.StationName ?? string.Empty,
            StationCode = alarm.Station?.StationCode ?? string.Empty,
            ChannelId = alarm.ChannelId,
            ChannelIndex = alarm.Channel?.ChannelIndex,
            Title = alarm.Title,
            Description = alarm.Description,
            ThresholdValue = alarm.ThresholdValue,
            ActualValue = alarm.ActualValue,
            Status = alarm.Status,
            Acknowledged = alarm.Acknowledged,
            AcknowledgedBy = alarm.AcknowledgedBy,
            AcknowledgedAt = alarm.AcknowledgedAt,
            ClearedAt = alarm.ClearedAt,
            CreatedAt = alarm.CreatedAt
        };
    }

    private static IEnumerable<Channel> CreateChannelsForStation(Guid stationId, int channelCount, int arrayRows, int arrayColumns)
    {
        var channels = new List<Channel>();
        for (int i = 0; i < channelCount; i++)
        {
            channels.Add(new Channel
            {
                Id = Guid.NewGuid(),
                StationId = stationId,
                ChannelIndex = i,
                RowIndex = i / arrayColumns,
                ColumnIndex = i % arrayColumns,
                NominalAmplitude = 1.0m,
                NominalPhase = 0.0m,
                CalibrationCoeffAmplitude = 1.0m,
                CalibrationCoeffPhase = 0.0m,
                Status = "normal",
                FailureProbability = 0.0m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        return channels;
    }
}
