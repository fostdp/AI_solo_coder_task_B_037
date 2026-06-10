using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Modules.PaEfficiencyEvaluator;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaEfficiencyController : ControllerBase
{
    private readonly IPaEfficiencyRecordRepository _efficiencyRepo;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IPaEfficiencyEvaluator _efficiencyEvaluator;
    private readonly IMediator _mediator;

    public PaEfficiencyController(
        IPaEfficiencyRecordRepository efficiencyRepo,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        IPaEfficiencyEvaluator efficiencyEvaluator,
        IMediator mediator)
    {
        _efficiencyRepo = efficiencyRepo;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _efficiencyEvaluator = efficiencyEvaluator;
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PaEfficiencyRecordDTO>>> GetRecords(
        Guid? stationId = null,
        bool? needsReplacementOnly = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<PaEfficiencyRecord> records;
        if (needsReplacementOnly == true && stationId.HasValue)
        {
            records = await _efficiencyRepo.GetNeedingReplacementAsync(
                stationId.Value, pageSize, cancellationToken);
        }
        else if (stationId.HasValue)
        {
            records = await _efficiencyRepo.GetByStationIdAsync(stationId.Value, pageSize, cancellationToken);
        }
        else
        {
            records = await _efficiencyRepo.GetRecentAsync(pageSize, cancellationToken);
        }

        var paged = records
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);

        var dtos = paged.Select(MapToDTO);
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PaEfficiencyRecordDTO>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await _efficiencyRepo.GetByIdAsync(id, cancellationToken);
        if (record == null) return NotFound();

        return Ok(MapToDTO(record));
    }

    [HttpGet("channel/{channelId}")]
    public async Task<ActionResult<IEnumerable<PaEfficiencyRecordDTO>>> GetByChannel(
        Guid channelId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var records = await _efficiencyRepo.GetByChannelIdAsync(channelId, limit, cancellationToken);
        var dtos = records.Select(MapToDTO);
        return Ok(dtos);
    }

    [HttpGet("history/{channelId}")]
    public async Task<ActionResult<PaEfficiencyHistoryDTO>> GetEfficiencyHistory(
        Guid channelId,
        int hours = 24,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow.AddHours(-hours);
        var records = await _efficiencyRepo.GetByTimeRangeAsync(channelId, startTime, DateTime.UtcNow, cancellationToken);

        var ordered = records.OrderBy(r => r.MeasurementTime).ToList();

        return Ok(new PaEfficiencyHistoryDTO
        {
            ChannelId = channelId,
            TimePoints = ordered.Select(r => r.MeasurementTime).ToArray(),
            EfficiencyValues = ordered.Select(r => r.EfficiencyPercent).ToArray(),
            TemperatureValues = ordered.Select(r => r.PaTemperature).ToArray(),
            OutputPowerValues = ordered.Select(r => r.OutputPowerDbm).ToArray(),
            DecayRate = ordered.Count >= 10 ? ordered[^1].EfficiencyDecayRate : 0,
            PredictedRemainingHours = ordered.Count >= 10 ? ordered[^1].PredictedRemainingHours : double.PositiveInfinity,
            NeedsReplacement = ordered.Count > 0 && ordered[^1].NeedsReplacement
        });
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<PaEfficiencyResultDTO>> EvaluateEfficiency(
        [FromBody] EvaluatePaEfficiencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var channel = await _channelRepo.GetByIdAsync(request.ChannelId, cancellationToken);
        if (channel == null) return NotFound($"Channel {request.ChannelId} not found");

        var recentRecords = (await _efficiencyRepo.GetByChannelIdAsync(request.ChannelId, 24, cancellationToken))
            .OrderByDescending(r => r.MeasurementTime)
            .Select(r => r.EfficiencyPercent)
            .ToArray();

        var result = await _efficiencyEvaluator.EvaluateEfficiencyAsync(
            request.StationId,
            channel,
            request.PaTemperature,
            request.OutputPowerDbm,
            request.InputPowerDbm,
            recentRecords,
            cancellationToken);

        await _mediator.Publish(new PaEfficiencyEvaluatedEvent
        {
            StationId = request.StationId,
            Result = result,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Ok(new PaEfficiencyResultDTO
        {
            ChannelId = result.ChannelId,
            PaTemperature = result.PaTemperature,
            OutputPowerDbm = result.OutputPowerDbm,
            InputPowerDbm = result.InputPowerDbm,
            EfficiencyPercent = result.EfficiencyPercent,
            EfficiencyDecayRate = result.EfficiencyDecayRate,
            PredictedRemainingHours = result.PredictedRemainingHours,
            NeedsReplacement = result.NeedsReplacement,
            ReplacementReason = result.ReplacementReason,
            EfficiencyHistory = result.EfficiencyHistory,
            EvaluationTime = DateTime.UtcNow
        });
    }

    [HttpGet("replacement-summary")]
    public async Task<ActionResult<IEnumerable<PaReplacementSummaryDTO>>> GetReplacementSummary(
        CancellationToken cancellationToken = default)
    {
        var stations = await _stationRepo.GetAllAsync(cancellationToken);
        var summaries = new List<PaReplacementSummaryDTO>();

        foreach (var station in stations)
        {
            var needsReplacement = (await _efficiencyRepo.GetNeedingReplacementAsync(station.Id, 100, cancellationToken))
                .GroupBy(r => r.ChannelId)
                .Select(g => g.OrderByDescending(r => r.MeasurementTime).First())
                .ToList();

            if (needsReplacement.Any())
            {
                foreach (var record in needsReplacement)
                {
                    var channel = await _channelRepo.GetByIdAsync(record.ChannelId, cancellationToken);
                    summaries.Add(new PaReplacementSummaryDTO
                    {
                        StationId = station.Id,
                        StationCode = station.StationCode,
                        StationName = station.Name,
                        ChannelId = record.ChannelId,
                        ChannelIndex = channel?.ChannelIndex ?? 0,
                        CurrentEfficiency = record.EfficiencyPercent,
                        DecayRate = record.EfficiencyDecayRate,
                        PredictedRemainingHours = record.PredictedRemainingHours,
                        ReplacementReason = record.ReplacementReason,
                        LastEvaluated = record.MeasurementTime
                    });
                }
            }
        }

        return Ok(summaries);
    }

    [HttpGet("channel-panel/{channelId}")]
    public async Task<ActionResult<PaChannelPanelDTO>> GetChannelPanelData(
        Guid channelId,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepo.GetByIdAsync(channelId, cancellationToken);
        if (channel == null) return NotFound();

        var records = await _efficiencyRepo.GetByChannelIdAsync(channelId, 24, cancellationToken);
        var ordered = records.OrderByDescending(r => r.MeasurementTime).ToList();
        var latest = ordered.FirstOrDefault();

        var historyData = ordered
            .OrderBy(r => r.MeasurementTime)
            .Select(r => new
            {
                Time = r.MeasurementTime,
                Efficiency = r.EfficiencyPercent,
                Temperature = r.PaTemperature
            })
            .ToList();

        var trend = historyData.Count >= 5
            ? (historyData.Last().Efficiency - historyData.First().Efficiency) / historyData.Count
            : 0;

        return Ok(new PaChannelPanelDTO
        {
            ChannelId = channelId,
            ChannelIndex = channel.ChannelIndex,
            Status = channel.Status,
            CurrentEfficiency = latest?.EfficiencyPercent ?? 0,
            CurrentTemperature = latest?.PaTemperature ?? 0,
            CurrentOutputPower = latest?.OutputPowerDbm ?? 0,
            EfficiencyDecayRate = latest?.EfficiencyDecayRate ?? 0,
            PredictedRemainingHours = latest?.PredictedRemainingHours ?? double.PositiveInfinity,
            NeedsReplacement = latest?.NeedsReplacement ?? false,
            Trend = trend,
            EfficiencyThreshold = 40.0,
            EfficiencyHistory = historyData.Select(h => new PaEfficiencyPointDTO
            {
                Time = h.Time,
                Efficiency = h.Efficiency,
                Temperature = h.Temperature
            }).ToArray()
        });
    }

    private static PaEfficiencyRecordDTO MapToDTO(PaEfficiencyRecord r) => new()
    {
        Id = r.Id,
        StationId = r.StationId,
        ChannelId = r.ChannelId,
        PaTemperature = r.PaTemperature,
        OutputPowerDbm = r.OutputPowerDbm,
        InputPowerDbm = r.InputPowerDbm,
        EfficiencyPercent = r.EfficiencyPercent,
        EfficiencyDecayRate = r.EfficiencyDecayRate,
        PredictedRemainingHours = r.PredictedRemainingHours,
        NeedsReplacement = r.NeedsReplacement,
        ReplacementReason = r.ReplacementReason,
        EfficiencyHistory = r.EfficiencyHistory,
        MeasurementTime = r.MeasurementTime
    };
}
