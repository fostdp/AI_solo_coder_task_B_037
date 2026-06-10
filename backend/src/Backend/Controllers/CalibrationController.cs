using AntennaMonitoring.Algorithms;
using AntennaMonitoring.DTOs;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using AntennaMonitoring.Modules.CalibrationEngine;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CalibrationController : ControllerBase
{
    private readonly ICalibrationRecordRepository _calibrationRecordRepository;
    private readonly ICalibrationService _calibrationService;
    private readonly IBaseStationRepository _baseStationRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IInfluxDBRepository _influxDBRepository;
    private readonly IEnumerable<IBeamformingCalibration> _beamformingAlgorithms;
    private readonly ICalibrationEngine _calibrationEngine;

    public CalibrationController(
        ICalibrationRecordRepository calibrationRecordRepository,
        ICalibrationService calibrationService,
        IBaseStationRepository baseStationRepository,
        IChannelRepository channelRepository,
        IInfluxDBRepository influxDBRepository,
        IEnumerable<IBeamformingCalibration> beamformingAlgorithms,
        ICalibrationEngine calibrationEngine)
    {
        _calibrationRecordRepository = calibrationRecordRepository;
        _calibrationService = calibrationService;
        _baseStationRepository = baseStationRepository;
        _channelRepository = channelRepository;
        _influxDBRepository = influxDBRepository;
        _beamformingAlgorithms = beamformingAlgorithms;
        _calibrationEngine = calibrationEngine;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CalibrationRecordDTO>>> GetCalibrationRecords(
        Guid? stationId = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var records = await _calibrationRecordRepository.GetAllAsync(stationId, cancellationToken);
        var pagedRecords = records
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);

        var dtos = pagedRecords.Select(MapToCalibrationRecordDTO);
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CalibrationRecordDTO>> GetCalibrationRecord(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await _calibrationRecordRepository.GetByIdAsync(id, cancellationToken);
        if (record == null)
        {
            return NotFound();
        }

        var dto = MapToCalibrationRecordDTO(record);
        return Ok(dto);
    }

    [HttpPost("run")]
    public async Task<ActionResult<IEnumerable<CalibrationResultDTO>>> RunCalibration(
        [FromBody] RunCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null)
        {
            return NotFound(new { message = $"基站 {request.StationId} 不存在" });
        }

        var channels = (await _channelRepository.GetByStationIdAsync(request.StationId, cancellationToken)).ToList();
        if (!channels.Any())
        {
            return BadRequest(new { message = $"基站 {request.StationId} 没有通道配置" });
        }

        var startTime = DateTime.UtcNow.AddHours(-1);
        var endTime = DateTime.UtcNow;
        var metrics = (await _influxDBRepository.GetStationMetricsAsync(
            request.StationId.ToString(), startTime, endTime, cancellationToken)).ToList();

        if (!metrics.Any())
        {
            var latestMetrics = await _influxDBRepository.GetLatestStationMetricsAsync(
                request.StationId.ToString(), cancellationToken);
            metrics = latestMetrics.ToList();
        }

        if (!metrics.Any())
        {
            return BadRequest(new { message = "无法获取通道度量数据，请稍后重试" });
        }

        var engineRequest = new CalibrationRequest
        {
            StationId = request.StationId,
            AlgorithmType = request.AlgorithmType ?? string.Empty,
            Channels = channels.AsReadOnly(),
            Metrics = metrics.AsReadOnly()
        };

        var engineResponse = await _calibrationEngine.RunCalibrationAsync(engineRequest, cancellationToken);

        if (!engineResponse.Success && !engineResponse.Converged)
        {
            return BadRequest(new { message = engineResponse.ErrorMessage ?? "校准失败，请检查基站通道和数据" });
        }

        var dtos = engineResponse.Results.Select(cc => new CalibrationResultDTO
        {
            ChannelId = cc.ChannelId,
            ChannelIndex = cc.ChannelIndex,
            AmplitudeDeviation = cc.AmplitudeDeviation,
            PhaseDeviation = cc.PhaseDeviation,
            CalibrationCoeffAmplitude = cc.CalibrationCoeffAmplitude,
            CalibrationCoeffPhase = cc.CalibrationCoeffPhase,
            SllBefore = engineResponse.SllBefore,
            SllAfter = engineResponse.SllAfter,
            Algorithm = engineResponse.Algorithm,
            CalibrationTime = engineResponse.CalibrationTime
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("station/{stationId}/latest")]
    public async Task<ActionResult<IEnumerable<CalibrationRecordDTO>>> GetLatestCalibrationByStation(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound(new { message = $"基站 {stationId} 不存在" });
        }

        var records = await _calibrationRecordRepository.GetByStationIdAsync(
            stationId, limit: 1000, cancellationToken: cancellationToken);

        var latestTime = records.Max(r => r.CalibrationTime);
        var latestRecords = records.Where(r => r.CalibrationTime == latestTime).ToList();

        if (!latestRecords.Any())
        {
            return NotFound(new { message = $"基站 {stationId} 暂无校准记录" });
        }

        var dtos = latestRecords.Select(MapToCalibrationRecordDTO);
        return Ok(dtos);
    }

    [HttpGet("station/{stationId}/history")]
    public async Task<ActionResult<IEnumerable<CalibrationRecordDTO>>> GetStationCalibrationHistory(
        Guid stationId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound(new { message = $"基站 {stationId} 不存在" });
        }

        var records = await _calibrationRecordRepository.GetByStationIdAsync(
            stationId, startTime, endTime, limit, cancellationToken);

        var dtos = records.Select(MapToCalibrationRecordDTO);
        return Ok(dtos);
    }

    [HttpGet("algorithms")]
    public ActionResult<IEnumerable<AlgorithmInfoDTO>> GetAvailableAlgorithms()
    {
        var algorithms = _beamformingAlgorithms.Select(a => new AlgorithmInfoDTO
        {
            Name = a.AlgorithmName,
            DisplayName = a.AlgorithmName switch
            {
                "LeastSquares" => "最小二乘法",
                "KalmanFilter" => "卡尔曼滤波法",
                _ => a.AlgorithmName
            }
        }).ToList();

        return Ok(algorithms);
    }

    private async Task SaveCalibrationResultsAsync(
        Guid stationId,
        CalibrationResult result,
        CancellationToken cancellationToken)
    {
        var records = new List<CalibrationRecord>();

        foreach (var cc in result.ChannelCalibrations)
        {
            records.Add(new CalibrationRecord
            {
                StationId = stationId,
                ChannelId = cc.ChannelId,
                CalibrationTime = result.CalibrationTime,
                AmplitudeDeviation = (decimal)cc.AmplitudeDeviation,
                PhaseDeviation = (decimal)cc.PhaseDeviation,
                CalibrationCoeffAmplitude = (decimal)cc.CalibrationCoeffAmplitude,
                CalibrationCoeffPhase = (decimal)cc.CalibrationCoeffPhase,
                SllBefore = (decimal)result.SllBefore,
                SllAfter = (decimal)result.SllAfter,
                Algorithm = result.Algorithm
            });

            await _channelRepository.UpdateCalibrationCoeffAsync(
                cc.ChannelId,
                (decimal)cc.CalibrationCoeffAmplitude,
                (decimal)cc.CalibrationCoeffPhase,
                cancellationToken);
        }

        await _calibrationRecordRepository.BulkCreateAsync(records, cancellationToken);

        await _influxDBRepository.WriteBeamformingMetricsAsync(
            stationId, result.Algorithm,
            (result.SllBefore + result.SllAfter) / 2,
            result.SllBefore, result.SllAfter,
            10.0, 8.0,
            result.Converged, cancellationToken);
    }

    private static CalibrationRecordDTO MapToCalibrationRecordDTO(CalibrationRecord record)
    {
        return new CalibrationRecordDTO
        {
            Id = record.Id,
            StationId = record.StationId,
            StationName = record.Station?.StationName,
            StationCode = record.Station?.StationCode,
            ChannelId = record.ChannelId,
            ChannelIndex = record.Channel?.ChannelIndex,
            CalibrationTime = record.CalibrationTime,
            AmplitudeDeviation = record.AmplitudeDeviation,
            PhaseDeviation = record.PhaseDeviation,
            CalibrationCoeffAmplitude = record.CalibrationCoeffAmplitude,
            CalibrationCoeffPhase = record.CalibrationCoeffPhase,
            SllBefore = record.SllBefore,
            SllAfter = record.SllAfter,
            Algorithm = record.Algorithm,
            CreatedAt = record.CreatedAt
        };
    }
}
