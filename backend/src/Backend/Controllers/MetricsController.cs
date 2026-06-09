using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IInfluxDBRepository _influxDBRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IBaseStationRepository _baseStationRepository;
    private readonly ICalibrationRecordRepository _calibrationRecordRepository;
    private readonly IDiagnosisRecordRepository _diagnosisRecordRepository;

    public MetricsController(
        IInfluxDBRepository influxDBRepository,
        IChannelRepository channelRepository,
        IBaseStationRepository baseStationRepository,
        ICalibrationRecordRepository calibrationRecordRepository,
        IDiagnosisRecordRepository diagnosisRecordRepository)
    {
        _influxDBRepository = influxDBRepository;
        _channelRepository = channelRepository;
        _baseStationRepository = baseStationRepository;
        _calibrationRecordRepository = calibrationRecordRepository;
        _diagnosisRecordRepository = diagnosisRecordRepository;
    }

    [HttpGet("channel/{channelId}/raw")]
    public async Task<ActionResult<IEnumerable<ChannelMetricsDTO>>> GetChannelRawMetrics(
        Guid channelId,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId, cancellationToken);
        if (channel == null)
        {
            return NotFound($"Channel with id {channelId} not found");
        }

        var actualStartTime = startTime ?? DateTime.UtcNow.AddHours(-1);
        var actualEndTime = endTime ?? DateTime.UtcNow;

        var metrics = await _influxDBRepository.GetChannelMetricsAsync(
            channelId.ToString(),
            actualStartTime,
            actualEndTime,
            "raw",
            cancellationToken);

        var metricsList = metrics
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .Select(MapToChannelMetricsDTO)
            .ToList();

        return Ok(metricsList);
    }

    [HttpGet("channel/{channelId}/aggregate")]
    public async Task<ActionResult> GetChannelAggregateMetrics(
        Guid channelId,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] string aggregation = "1h",
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId, cancellationToken);
        if (channel == null)
        {
            return NotFound($"Channel with id {channelId} not found");
        }

        var actualStartTime = startTime ?? DateTime.UtcNow.AddHours(-24);
        var actualEndTime = endTime ?? DateTime.UtcNow;

        var metrics = await _influxDBRepository.GetChannelMetricsAsync(
            channelId.ToString(),
            actualStartTime,
            actualEndTime,
            aggregation,
            cancellationToken);

        var metricsList = metrics
            .OrderBy(m => m.Timestamp)
            .Select(MapToChannelMetricsDTO)
            .ToList();

        var avgAmplitude = metricsList.Any() ? metricsList.Average(m => m.Amplitude) : 0;
        var avgSwr = metricsList.Any() ? metricsList.Average(m => m.Swr) : 0;
        var avgTemperature = metricsList.Any() ? metricsList.Average(m => m.PaTemperature) : 0;
        var maxSwr = metricsList.Any() ? metricsList.Max(m => m.Swr) : 0;
        var maxTemperature = metricsList.Any() ? metricsList.Max(m => m.PaTemperature) : 0;

        var result = new
        {
            ChannelId = channelId,
            ChannelIndex = channel.ChannelIndex,
            StartTime = actualStartTime,
            EndTime = actualEndTime,
            Aggregation = aggregation,
            DataPoints = metricsList.Count,
            AverageAmplitude = Math.Round(avgAmplitude, 4),
            AverageSWR = Math.Round(avgSwr, 4),
            AverageTemperature = Math.Round(avgTemperature, 2),
            MaxSWR = Math.Round(maxSwr, 4),
            MaxTemperature = Math.Round(maxTemperature, 2),
            Data = metricsList
        };

        return Ok(result);
    }

    [HttpGet("station/{stationId}/latest")]
    public async Task<ActionResult> GetStationLatestMetrics(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound($"Base station with id {stationId} not found");
        }

        var channels = (await _channelRepository.GetByStationIdAsync(stationId, cancellationToken)).ToList();
        if (!channels.Any())
        {
            return Ok(new
            {
                StationId = stationId,
                StationName = station.StationName,
                StationCode = station.StationCode,
                Channels = new List<object>()
            });
        }

        var latestMetrics = new List<object>();
        foreach (var channel in channels)
        {
            var metrics = await _influxDBRepository.GetLatestChannelMetricsAsync(
                channel.Id.ToString(),
                cancellationToken);

            latestMetrics.Add(new
            {
                ChannelId = channel.Id,
                ChannelIndex = channel.ChannelIndex,
                RowIndex = channel.RowIndex,
                ColumnIndex = channel.ColumnIndex,
                Status = channel.Status,
                Timestamp = metrics?.Timestamp ?? DateTime.UtcNow,
                Amplitude = metrics?.Amplitude ?? 0,
                Phase = metrics?.Phase ?? 0,
                AmplitudeDeviation = metrics?.AmplitudeDeviation ?? 0,
                PhaseDeviation = metrics?.PhaseDeviation ?? 0,
                Swr = metrics?.Swr ?? 0,
                PaTemperature = metrics?.PaTemperature ?? 0,
                TxPower = metrics?.TxPower ?? 0,
                RxPower = metrics?.RxPower ?? 0,
                Ber = metrics?.Ber ?? 0
            });
        }

        var result = new
        {
            StationId = stationId,
            StationName = station.StationName,
            StationCode = station.StationCode,
            TotalChannels = channels.Count,
            OnlineChannels = channels.Count(c => c.Status == "online"),
            WarningChannels = channels.Count(c => c.Status == "warning"),
            FaultChannels = channels.Count(c => c.Status == "fault"),
            Channels = latestMetrics
        };

        return Ok(result);
    }

    [HttpGet("beamforming/{stationId}")]
    public async Task<ActionResult> GetBeamformingMetrics(
        Guid stationId,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound($"Base station with id {stationId} not found");
        }

        var actualStartTime = startTime ?? DateTime.UtcNow.AddDays(-7);
        var actualEndTime = endTime ?? DateTime.UtcNow;

        var calibrationRecords = await _calibrationRecordRepository.GetByStationIdAsync(
            stationId,
            actualStartTime,
            actualEndTime,
            limit,
            cancellationToken);

        var records = calibrationRecords.ToList();

        var beamPatternHistory = await _influxDBRepository.GetBeamPatternHistoryAsync(
            stationId,
            Math.Min(10, limit),
            cancellationToken);

        var latestSLL = await _calibrationRecordRepository.GetLatestSLLAsync(stationId, cancellationToken);

        var avgSLLBefore = records.Any() && records.All(r => r.SllBefore.HasValue)
            ? records.Average(r => r.SllBefore!.Value)
            : 0;
        var avgSLLAfter = records.Any() && records.All(r => r.SllAfter.HasValue)
            ? records.Average(r => r.SllAfter!.Value)
            : 0;
        var avgImprovement = avgSLLBefore != 0 ? (avgSLLAfter - avgSLLBefore) : 0;

        var result = new
        {
            StationId = stationId,
            StationName = station.StationName,
            StationCode = station.StationCode,
            LatestSLL = latestSLL,
            AverageSLLBefore = Math.Round((double)avgSLLBefore, 4),
            AverageSLLAfter = Math.Round((double)avgSLLAfter, 4),
            AverageImprovement = Math.Round((double)avgImprovement, 4),
            TotalCalibrations = records.Count,
            CalibrationRecords = records.Select(r => new
            {
                r.Id,
                r.ChannelId,
                r.ChannelIndex,
                r.CalibrationTime,
                r.SllBefore,
                r.SllAfter,
                r.Algorithm
            }).ToList(),
            BeamPatternHistory = beamPatternHistory.ToList()
        };

        return Ok(result);
    }

    [HttpGet("diagnosis/{channelId}")]
    public async Task<ActionResult<IEnumerable<DiagnosisResultDTO>>> GetDiagnosisHistory(
        Guid channelId,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId, cancellationToken);
        if (channel == null)
        {
            return NotFound($"Channel with id {channelId} not found");
        }

        var actualStartTime = startTime ?? DateTime.UtcNow.AddDays(-7);
        var actualEndTime = endTime ?? DateTime.UtcNow;

        var diagnosisRecords = await _diagnosisRecordRepository.GetByChannelIdAsync(
            channelId,
            actualStartTime,
            actualEndTime,
            limit,
            cancellationToken);

        var result = diagnosisRecords
            .OrderByDescending(d => d.DiagnosisTime)
            .Select(r => new DiagnosisResultDTO
            {
                ChannelId = r.ChannelId,
                ChannelIndex = channel.ChannelIndex,
                SwrValue = (double)r.SwrValue,
                TemperatureValue = (double)r.TemperatureValue,
                FailureProbability = (double)r.FailureProbability,
                ModelType = r.ModelType,
                PredictionHorizonHours = r.PredictedFailureHours,
                Recommendation = r.Recommendation ?? string.Empty,
                DiagnosisTime = r.DiagnosisTime
            })
            .ToList();

        return Ok(result);
    }

    private static ChannelMetricsDTO MapToChannelMetricsDTO(ChannelMetrics metrics)
    {
        return new ChannelMetricsDTO
        {
            ChannelId = Guid.TryParse(metrics.ChannelId, out var channelId) ? channelId : Guid.Empty,
            ChannelIndex = metrics.ChannelIndex,
            Timestamp = metrics.Timestamp,
            Amplitude = metrics.Amplitude,
            Phase = metrics.Phase,
            AmplitudeDeviation = metrics.AmplitudeDeviation,
            PhaseDeviation = metrics.PhaseDeviation,
            Swr = metrics.Swr,
            PaTemperature = metrics.PaTemperature,
            TxPower = metrics.TxPower
        };
    }
}
