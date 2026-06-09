using AntennaMonitoring.Algorithms;
using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosisController : ControllerBase
{
    private readonly IDiagnosisRecordRepository _diagnosisRecordRepository;
    private readonly IDiagnosisService _diagnosisService;
    private readonly IBaseStationRepository _baseStationRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEnumerable<IHealthDiagnosis> _diagnosisModels;
    private readonly IInfluxDBRepository _influxDBRepository;
    private readonly DiagnosisOptions _options;

    public DiagnosisController(
        IDiagnosisRecordRepository diagnosisRecordRepository,
        IDiagnosisService diagnosisService,
        IBaseStationRepository baseStationRepository,
        IChannelRepository channelRepository,
        IEnumerable<IHealthDiagnosis> diagnosisModels,
        IInfluxDBRepository influxDBRepository,
        IOptions<DiagnosisOptions> options)
    {
        _diagnosisRecordRepository = diagnosisRecordRepository;
        _diagnosisService = diagnosisService;
        _baseStationRepository = baseStationRepository;
        _channelRepository = channelRepository;
        _diagnosisModels = diagnosisModels;
        _influxDBRepository = influxDBRepository;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<ActionResult> GetDiagnosisRecords(
        Guid? stationId = null,
        Guid? channelId = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var records = await _diagnosisRecordRepository.GetAllAsync(
            stationId, channelId, startTime, endTime,
            pageNumber, pageSize, cancellationToken);

        var totalCount = await _diagnosisRecordRepository.GetCountAsync(
            stationId, channelId, startTime, endTime, cancellationToken);

        var dtos = records.Select(MapToDiagnosisResultDTO).ToList();

        return Ok(new
        {
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Items = dtos
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DiagnosisResultDTO>> GetDiagnosisRecord(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await _diagnosisRecordRepository.GetByIdAsync(id, cancellationToken);
        if (record == null)
        {
            return NotFound();
        }

        var dto = MapToDiagnosisResultDTO(record);
        return Ok(dto);
    }

    [HttpPost("run")]
    public async Task<ActionResult<IEnumerable<DiagnosisResultDTO>>> RunDiagnosis(
        RunDiagnosisRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null)
        {
            return NotFound(new { message = $"基站 {request.StationId} 不存在" });
        }

        var model = string.IsNullOrEmpty(request.ModelType)
            ? _diagnosisModels.FirstOrDefault(m => m.ModelName == _options.ModelType) ?? _diagnosisModels.First()
            : _diagnosisModels.FirstOrDefault(m => m.ModelName.Equals(request.ModelType, StringComparison.OrdinalIgnoreCase));

        if (model == null)
        {
            return BadRequest(new { message = $"无效的诊断模型类型: {request.ModelType}" });
        }

        var channels = (await _channelRepository.GetByStationIdAsync(request.StationId, cancellationToken)).ToList();
        if (!channels.Any())
        {
            return BadRequest(new { message = "该基站没有通道" });
        }

        var startTime = DateTime.UtcNow.AddHours(-24);
        var endTime = DateTime.UtcNow;
        var allMetrics = (await _influxDBRepository.GetStationMetricsAsync(
            request.StationId.ToString(), startTime, endTime, cancellationToken)).ToList();

        var results = new List<DiagnosisResult>();
        var diagnosisRecords = new List<DiagnosisRecord>();

        foreach (var channel in channels)
        {
            var channelMetrics = allMetrics.Where(m => m.ChannelId == channel.Id.ToString()).ToList();
            var result = await model.DiagnoseAsync(request.StationId, channel, channelMetrics, cancellationToken);

            results.Add(result);

            diagnosisRecords.Add(new DiagnosisRecord
            {
                StationId = request.StationId,
                ChannelId = channel.Id,
                DiagnosisTime = result.DiagnosisTime,
                SwrValue = (decimal)result.SwrValue,
                TemperatureValue = (decimal)result.TemperatureValue,
                FailureProbability = (decimal)result.FailureProbability,
                ModelType = result.ModelType,
                PredictionHorizonHours = result.PredictionHorizonHours,
                Recommendation = result.Recommendation
            });

            await _channelRepository.UpdateFailureProbabilityAsync(
                channel.Id, (decimal)result.FailureProbability, cancellationToken);

            await _influxDBRepository.WriteDiagnosisMetricsAsync(
                request.StationId, channel.Id, result.ModelType,
                result.FailureProbability, result.SwrPredicted,
                result.TemperaturePredicted, result.AnomalyScore,
                result.PredictedFailureHours, result.HealthScore,
                cancellationToken);
        }

        await _diagnosisRecordRepository.BulkCreateAsync(diagnosisRecords, cancellationToken);

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

    [HttpGet("station/{stationId}/latest")]
    public async Task<ActionResult<IEnumerable<DiagnosisResultDTO>>> GetStationLatestDiagnosis(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound(new { message = $"基站 {stationId} 不存在" });
        }

        var channels = (await _channelRepository.GetByStationIdAsync(stationId, cancellationToken)).ToList();
        var results = new List<DiagnosisResultDTO>();

        foreach (var channel in channels)
        {
            var latestRecord = await _diagnosisRecordRepository.GetLatestAsync(channel.Id, cancellationToken);
            if (latestRecord != null)
            {
                results.Add(MapToDiagnosisResultDTO(latestRecord));
            }
        }

        return Ok(results);
    }

    [HttpGet("channel/{channelId}/history")]
    public async Task<ActionResult<IEnumerable<DiagnosisResultDTO>>> GetChannelDiagnosisHistory(
        Guid channelId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId, cancellationToken);
        if (channel == null)
        {
            return NotFound(new { message = $"通道 {channelId} 不存在" });
        }

        var records = await _diagnosisRecordRepository.GetByChannelIdAsync(
            channelId, startTime, endTime, limit, cancellationToken);

        var dtos = records.Select(MapToDiagnosisResultDTO).ToList();
        return Ok(dtos);
    }

    [HttpGet("models")]
    public ActionResult<IEnumerable<string>> GetDiagnosisModels()
    {
        var modelNames = _diagnosisModels.Select(m => m.ModelName).ToList();
        return Ok(modelNames);
    }

    [HttpGet("highrisk")]
    public async Task<ActionResult<IEnumerable<DiagnosisResultDTO>>> GetHighRiskChannels(
        CancellationToken cancellationToken = default)
    {
        var stations = await _baseStationRepository.GetAllAsync(cancellationToken);
        var highRiskResults = new List<DiagnosisResultDTO>();

        foreach (var station in stations)
        {
            var channels = (await _channelRepository.GetByStationIdAsync(station.Id, cancellationToken))
                .Where(c => (double)c.FailureProbability > 0.7)
                .ToList();

            foreach (var channel in channels)
            {
                var latestRecord = await _diagnosisRecordRepository.GetLatestAsync(channel.Id, cancellationToken);
                if (latestRecord != null && (double?)latestRecord.FailureProbability > 0.7)
                {
                    highRiskResults.Add(MapToDiagnosisResultDTO(latestRecord));
                }
            }
        }

        return Ok(highRiskResults.OrderByDescending(r => r.FailureProbability).ToList());
    }

    private static DiagnosisResultDTO MapToDiagnosisResultDTO(DiagnosisRecord record)
    {
        return new DiagnosisResultDTO
        {
            ChannelId = record.ChannelId,
            ChannelIndex = record.Channel?.ChannelIndex ?? 0,
            SwrValue = (double?)(record.SwrValue ?? 0) ?? 0,
            TemperatureValue = (double?)(record.TemperatureValue ?? 0) ?? 0,
            FailureProbability = (double?)(record.FailureProbability ?? 0) ?? 0,
            ModelType = record.ModelType ?? string.Empty,
            PredictionHorizonHours = record.PredictionHorizonHours ?? 0,
            Recommendation = record.Recommendation ?? string.Empty,
            DiagnosisTime = record.DiagnosisTime
        };
    }
}

public class RunDiagnosisRequest
{
    public Guid StationId { get; set; }
    public string? ModelType { get; set; }
}
