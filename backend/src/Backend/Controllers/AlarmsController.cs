using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlarmsController : ControllerBase
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IAlarmService _alarmService;
    private readonly IBaseStationRepository _baseStationRepository;
    private readonly IChannelRepository _channelRepository;

    public AlarmsController(
        IAlarmRepository alarmRepository,
        IAlarmService alarmService,
        IBaseStationRepository baseStationRepository,
        IChannelRepository channelRepository)
    {
        _alarmRepository = alarmRepository;
        _alarmService = alarmService;
        _baseStationRepository = baseStationRepository;
        _channelRepository = channelRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlarmDTO>>> GetAlarms(
        [FromQuery] string? level = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? stationId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var alarms = await _alarmRepository.GetAllAsync(status, level, cancellationToken);
        var query = alarms.AsQueryable();

        if (stationId.HasValue)
        {
            query = query.Where(a => a.StationId == stationId.Value);
        }

        var totalCount = query.Count();
        var pagedAlarms = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var alarmDtos = pagedAlarms.Select(MapToAlarmDTO).ToList();

        Response.Headers.Append("X-Total-Count", totalCount.ToString());
        Response.Headers.Append("X-Page", page.ToString());
        Response.Headers.Append("X-Page-Size", pageSize.ToString());

        return Ok(alarmDtos);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<AlarmSummaryDTO>> GetSummary(CancellationToken cancellationToken = default)
    {
        var summary = await _alarmService.GetSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AlarmDTO>> GetAlarm(Guid id, CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(id, cancellationToken);
        if (alarm == null)
        {
            return NotFound();
        }

        return Ok(MapToAlarmDTO(alarm));
    }

    [HttpPost]
    public async Task<ActionResult<AlarmDTO>> CreateAlarm(
        [FromBody] CreateAlarmDTO dto,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(dto.StationId, cancellationToken);
        if (station == null)
        {
            return NotFound($"Base station with id {dto.StationId} not found");
        }

        if (dto.ChannelId.HasValue)
        {
            var channel = await _channelRepository.GetByIdAsync(dto.ChannelId.Value, cancellationToken);
            if (channel == null)
            {
                return NotFound($"Channel with id {dto.ChannelId.Value} not found");
            }
        }

        var alarm = await _alarmService.CreateAlarmAsync(dto, cancellationToken);
        if (alarm == null)
        {
            return BadRequest("Failed to create alarm");
        }

        var createdAlarm = await _alarmRepository.GetByIdAsync(alarm.Id, cancellationToken);
        var alarmDto = MapToAlarmDTO(createdAlarm!);

        return CreatedAtAction(nameof(GetAlarm), new { id = alarm.Id }, alarmDto);
    }

    [HttpPut("{id}/acknowledge")]
    public async Task<ActionResult<AlarmDTO>> AcknowledgeAlarm(
        Guid id,
        [FromBody] AcknowledgeAlarmDTO dto,
        CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(id, cancellationToken);
        if (alarm == null)
        {
            return NotFound();
        }

        var updatedAlarm = await _alarmService.AcknowledgeAsync(id, dto, cancellationToken);
        if (updatedAlarm == null)
        {
            return BadRequest("Failed to acknowledge alarm");
        }

        var result = await _alarmRepository.GetByIdAsync(id, cancellationToken);
        return Ok(MapToAlarmDTO(result!));
    }

    [HttpPut("{id}/clear")]
    public async Task<ActionResult<AlarmDTO>> ClearAlarm(
        Guid id,
        [FromBody] ClearAlarmDTO dto,
        CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(id, cancellationToken);
        if (alarm == null)
        {
            return NotFound();
        }

        var updatedAlarm = await _alarmService.ClearAsync(id, cancellationToken);
        if (updatedAlarm == null)
        {
            return BadRequest("Failed to clear alarm");
        }

        var result = await _alarmRepository.GetByIdAsync(id, cancellationToken);
        return Ok(MapToAlarmDTO(result!));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAlarm(Guid id, CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(id, cancellationToken);
        if (alarm == null)
        {
            return NotFound();
        }

        var result = await _alarmRepository.DeleteAsync(id, cancellationToken);
        if (!result)
        {
            return BadRequest("Failed to delete alarm");
        }

        return NoContent();
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
}
