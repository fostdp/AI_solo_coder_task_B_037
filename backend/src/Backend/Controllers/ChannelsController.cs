using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController : ControllerBase
{
    private readonly IChannelRepository _channelRepository;
    private readonly IBaseStationRepository _baseStationRepository;
    private readonly IInfluxDBRepository _influxDBRepository;
    private readonly ICalibrationRecordRepository _calibrationRecordRepository;

    public ChannelsController(
        IChannelRepository channelRepository,
        IBaseStationRepository baseStationRepository,
        IInfluxDBRepository influxDBRepository,
        ICalibrationRecordRepository calibrationRecordRepository)
    {
        _channelRepository = channelRepository;
        _baseStationRepository = baseStationRepository;
        _influxDBRepository = influxDBRepository;
        _calibrationRecordRepository = calibrationRecordRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ChannelDTO>>> GetChannels(
        [FromQuery] Guid? stationId = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Channel> channels;

        if (stationId.HasValue)
        {
            var station = await _baseStationRepository.GetByIdAsync(stationId.Value, cancellationToken);
            if (station == null)
            {
                return NotFound($"基站 {stationId.Value} 不存在");
            }
            channels = await _channelRepository.GetByStationIdAsync(stationId.Value, cancellationToken);
        }
        else
        {
            var stations = await _baseStationRepository.GetAllAsync(cancellationToken);
            var channelList = new List<Channel>();
            foreach (var station in stations)
            {
                var stationChannels = await _channelRepository.GetByStationIdAsync(station.Id, cancellationToken);
                channelList.AddRange(stationChannels);
            }
            channels = channelList;
        }

        var channelDTOs = new List<ChannelDTO>();
        foreach (var channel in channels)
        {
            var latestMetrics = await _influxDBRepository.GetLatestChannelMetricsAsync(
                channel.Id.ToString(), cancellationToken);

            channelDTOs.Add(MapToChannelDTO(channel, latestMetrics));
        }

        return Ok(channelDTOs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ChannelDTO>> GetChannel(Guid id, CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(id, cancellationToken);
        if (channel == null)
        {
            return NotFound($"通道 {id} 不存在");
        }

        var latestMetrics = await _influxDBRepository.GetLatestChannelMetricsAsync(
            id.ToString(), cancellationToken);

        return Ok(MapToChannelDTO(channel, latestMetrics));
    }

    [HttpGet("{id}/status")]
    public async Task<ActionResult<ChannelStatusDTO>> GetChannelStatus(Guid id, CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(id, cancellationToken);
        if (channel == null)
        {
            return NotFound($"通道 {id} 不存在");
        }

        var latestMetrics = await _influxDBRepository.GetLatestChannelMetricsAsync(
            id.ToString(), cancellationToken);

        var statusDTO = new ChannelStatusDTO
        {
            Id = channel.Id,
            ChannelIndex = channel.ChannelIndex,
            RowIndex = channel.RowIndex,
            ColumnIndex = channel.ColumnIndex,
            Status = channel.Status,
            AmplitudeDeviation = latestMetrics?.AmplitudeDeviation ?? 0,
            PhaseDeviation = latestMetrics?.PhaseDeviation ?? 0,
            Swr = latestMetrics?.Swr ?? 0,
            Temperature = latestMetrics?.PaTemperature ?? 0,
            FailureProbability = (double)channel.FailureProbability
        };

        return Ok(statusDTO);
    }

    [HttpGet("{id}/trend")]
    public async Task<ActionResult<IEnumerable<ChannelTrendDTO>>> GetChannelTrend(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(id, cancellationToken);
        if (channel == null)
        {
            return NotFound($"通道 {id} 不存在");
        }

        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-24);

        var metrics = await _influxDBRepository.GetChannelMetricsAsync(
            id.ToString(), startTime, endTime, "5m", cancellationToken);

        var trendDTOs = metrics.Select(m => new ChannelTrendDTO
        {
            Timestamp = m.Timestamp,
            Amplitude = m.Amplitude,
            Swr = m.Swr,
            Temperature = m.PaTemperature
        });

        return Ok(trendDTOs);
    }

    [HttpGet("{id}/metrics")]
    public async Task<ActionResult<IEnumerable<ChannelMetricsDTO>>> GetChannelMetrics(
        Guid id,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] string aggregation = "raw",
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(id, cancellationToken);
        if (channel == null)
        {
            return NotFound($"通道 {id} 不存在");
        }

        var actualEndTime = endTime ?? DateTime.UtcNow;
        var actualStartTime = startTime ?? actualEndTime.AddHours(-1);

        var metrics = await _influxDBRepository.GetChannelMetricsAsync(
            id.ToString(), actualStartTime, actualEndTime, aggregation, cancellationToken);

        var metricsDTOs = metrics.Select(m => new ChannelMetricsDTO
        {
            ChannelId = id,
            ChannelIndex = m.ChannelIndex,
            Timestamp = m.Timestamp,
            Amplitude = m.Amplitude,
            Phase = m.Phase,
            AmplitudeDeviation = m.AmplitudeDeviation,
            PhaseDeviation = m.PhaseDeviation,
            Swr = m.Swr,
            PaTemperature = m.PaTemperature,
            TxPower = m.TxPower
        });

        return Ok(metricsDTOs);
    }

    [HttpGet("station/{stationId}/statuses")]
    public async Task<ActionResult<IEnumerable<ChannelStatusDTO>>> GetChannelStatuses(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound($"基站 {stationId} 不存在");
        }

        var channels = await _channelRepository.GetByStationIdAsync(stationId, cancellationToken);
        var statusDTOs = new List<ChannelStatusDTO>();

        foreach (var channel in channels)
        {
            var latestMetrics = await _influxDBRepository.GetLatestChannelMetricsAsync(
                channel.Id.ToString(), cancellationToken);

            statusDTOs.Add(new ChannelStatusDTO
            {
                Id = channel.Id,
                ChannelIndex = channel.ChannelIndex,
                RowIndex = channel.RowIndex,
                ColumnIndex = channel.ColumnIndex,
                Status = channel.Status,
                AmplitudeDeviation = latestMetrics?.AmplitudeDeviation ?? 0,
                PhaseDeviation = latestMetrics?.PhaseDeviation ?? 0,
                Swr = latestMetrics?.Swr ?? 0,
                Temperature = latestMetrics?.PaTemperature ?? 0,
                FailureProbability = (double)channel.FailureProbability
            });
        }

        return Ok(statusDTOs);
    }

    [HttpPut("{id}/calibration")]
    public async Task<ActionResult<CalibrationResultDTO>> UpdateCalibration(
        Guid id,
        [FromBody] CalibrationUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(id, cancellationToken);
        if (channel == null)
        {
            return NotFound($"通道 {id} 不存在");
        }

        var success = await _channelRepository.UpdateCalibrationCoeffAsync(
            id,
            (decimal)request.CalibrationCoeffAmplitude,
            (decimal)request.CalibrationCoeffPhase,
            cancellationToken);

        if (!success)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "更新校准系数失败");
        }

        var calibrationRecord = new CalibrationRecord
        {
            StationId = channel.StationId,
            ChannelId = id,
            CalibrationTime = DateTime.UtcNow,
            AmplitudeDeviation = (decimal)request.AmplitudeDeviation,
            PhaseDeviation = (decimal)request.PhaseDeviation,
            CalibrationCoeffAmplitude = (decimal)request.CalibrationCoeffAmplitude,
            CalibrationCoeffPhase = (decimal)request.CalibrationCoeffPhase,
            SllBefore = (decimal)request.SllBefore,
            SllAfter = (decimal)request.SllAfter,
            Algorithm = request.Algorithm
        };

        await _calibrationRecordRepository.CreateAsync(calibrationRecord, cancellationToken);

        var resultDTO = new CalibrationResultDTO
        {
            ChannelId = id,
            ChannelIndex = channel.ChannelIndex,
            AmplitudeDeviation = request.AmplitudeDeviation,
            PhaseDeviation = request.PhaseDeviation,
            CalibrationCoeffAmplitude = request.CalibrationCoeffAmplitude,
            CalibrationCoeffPhase = request.CalibrationCoeffPhase,
            SllBefore = request.SllBefore,
            SllAfter = request.SllAfter,
            Algorithm = request.Algorithm,
            CalibrationTime = calibrationRecord.CalibrationTime
        };

        return Ok(resultDTO);
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
}

public class CalibrationUpdateRequest
{
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double CalibrationCoeffAmplitude { get; set; }
    public double CalibrationCoeffPhase { get; set; }
    public double SllBefore { get; set; }
    public double SllAfter { get; set; }
    public string Algorithm { get; set; } = string.Empty;
}
