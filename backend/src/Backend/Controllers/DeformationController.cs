using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using DeformationMonitor.Module;
using DeformationMonitor.Module.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeformationController : ControllerBase
{
    private readonly IDeformationRecordRepository _deformationRepo;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IDeformationMonitor _deformationMonitor;
    private readonly IMediator _mediator;
    private readonly IInfluxDBRepository _influxRepo;

    public DeformationController(
        IDeformationRecordRepository deformationRepo,
        IBaseStationRepository stationRepo,
        IDeformationMonitor deformationMonitor,
        IMediator mediator,
        IInfluxDBRepository influxRepo)
    {
        _deformationRepo = deformationRepo;
        _stationRepo = stationRepo;
        _deformationMonitor = deformationMonitor;
        _mediator = mediator;
        _influxRepo = influxRepo;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeformationRecordDTO>>> GetRecords(
        Guid? stationId = null,
        bool? exceedingOnly = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<DeformationRecord> records;
        if (exceedingOnly == true && stationId.HasValue)
        {
            records = await _deformationRepo.GetExceedingThresholdAsync(stationId.Value, pageSize, cancellationToken);
        }
        else if (stationId.HasValue)
        {
            records = await _deformationRepo.GetByStationIdAsync(stationId.Value, pageSize, cancellationToken);
        }
        else
        {
            records = await _deformationRepo.GetRecentAsync(pageSize, cancellationToken);
        }

        var paged = records
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);

        var dtos = paged.Select(MapToDTO);
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DeformationRecordDTO>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await _deformationRepo.GetByIdAsync(id, cancellationToken);
        if (record == null) return NotFound();

        return Ok(MapToDTO(record));
    }

    [HttpGet("latest")]
    public async Task<ActionResult<DeformationRecordDTO>> GetLatest(
        [FromQuery] Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var records = await _deformationRepo.GetByStationIdAsync(stationId, 1, cancellationToken);
        var record = records.FirstOrDefault();
        if (record == null) return NotFound();

        return Ok(MapToDTO(record));
    }

    [HttpPost("sensor-data")]
    public async Task<ActionResult<DeformationResultDTO>> SubmitSensorData(
        [FromBody] SensorDataDTO request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var sensorData = new SensorData
        {
            SensorIndex = request.SensorIndex,
            SensorType = request.SensorType ?? "MEMS",
            TiltAngleX = request.TiltAngleX,
            TiltAngleY = request.TiltAngleY,
            TiltAngleZ = request.TiltAngleZ,
            TiltMagnitude = Math.Sqrt(request.TiltAngleX * request.TiltAngleX +
                                       request.TiltAngleY * request.TiltAngleY +
                                       request.TiltAngleZ * request.TiltAngleZ),
            StrainValue = request.StrainValue,
            Temperature = request.Temperature,
            WindSpeed = request.WindSpeed,
            Timestamp = request.Timestamp ?? DateTime.UtcNow
        };

        await _mediator.Publish(new SensorDataReceivedEvent
        {
            StationId = request.StationId,
            SensorData = sensorData,
            Timestamp = sensorData.Timestamp
        }, cancellationToken);

        return Accepted(new { message = "Sensor data received and queued for processing" });
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<DeformationResultDTO>> RunAnalysis(
        [FromBody] AnalyzeDeformationRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var sensorData = new SensorData
        {
            SensorIndex = request.SensorIndex,
            SensorType = request.SensorType ?? "MEMS",
            TiltAngleX = request.TiltAngleX,
            TiltAngleY = request.TiltAngleY,
            TiltAngleZ = request.TiltAngleZ,
            TiltMagnitude = Math.Sqrt(request.TiltAngleX * request.TiltAngleX +
                                       request.TiltAngleY * request.TiltAngleY),
            StrainValue = request.StrainValue,
            Temperature = request.Temperature,
            WindSpeed = request.WindSpeed,
            Timestamp = DateTime.UtcNow
        };

        var result = await _deformationMonitor.AnalyzeDeformationAsync(
            request.StationId, sensorData, station, request.ApplyBeamCorrection, cancellationToken);

        await _mediator.Publish(new DeformationAnalyzedEvent
        {
            StationId = request.StationId,
            Result = result,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Ok(new DeformationResultDTO
        {
            StationId = request.StationId,
            TiltAngleX = result.TiltAngleX,
            TiltAngleY = result.TiltAngleY,
            TiltMagnitude = result.TiltMagnitude,
            StrainValue = result.StrainValue,
            DisplacementMm = result.DisplacementMm,
            StressMpa = result.StressMpa,
            DeformationZone = result.DeformationZone,
            IsExceedingThreshold = result.IsExceedingThreshold,
            BeamCorrectionApplied = result.BeamCorrectionApplied,
            CorrectionAngleAzimuth = result.CorrectionAngleAzimuth,
            CorrectionAngleElevation = result.CorrectionAngleElevation,
            AnalysisTime = DateTime.UtcNow
        });
    }

    [HttpGet("sensor-history")]
    public async Task<ActionResult<IEnumerable<SensorMetricDTO>>> GetSensorHistory(
        [FromQuery] Guid stationId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var metrics = await _influxRepo.GetSensorMetricsAsync(
            stationId.ToString(), startTime, endTime, cancellationToken);

        var dtos = metrics.Select(m => new SensorMetricDTO
        {
            SensorType = m.SensorType,
            SensorIndex = m.SensorIndex,
            TiltAngleX = m.TiltAngleX,
            TiltAngleY = m.TiltAngleY,
            TiltAngleZ = m.TiltAngleZ,
            StrainValue = m.StrainValue,
            Temperature = m.Temperature,
            WindSpeed = m.WindSpeed,
            Timestamp = m.Timestamp
        });

        return Ok(dtos);
    }

    [HttpGet("map-data")]
    public async Task<ActionResult<IEnumerable<DeformationMapDTO>>> GetMapData(
        CancellationToken cancellationToken = default)
    {
        var records = await _deformationRepo.GetRecentAsync(100, cancellationToken);
        var stations = await _stationRepo.GetAllAsync(cancellationToken);

        var stationDict = stations.ToDictionary(s => s.Id, s => s);

        var mapData = records
            .GroupBy(r => r.StationId)
            .Select(g => new
            {
                StationId = g.Key,
                Latest = g.OrderByDescending(r => r.MeasurementTime).First()
            })
            .Where(x => stationDict.ContainsKey(x.StationId))
            .Select(x =>
            {
                var station = stationDict[x.StationId];
                var coord = station.Location?.Coordinate;
                return new DeformationMapDTO
                {
                    StationId = x.StationId,
                    StationCode = station.StationCode,
                    StationName = station.Name,
                    Longitude = coord?.X ?? 0,
                    Latitude = coord?.Y ?? 0,
                    DisplacementMm = x.Latest.CalculatedDisplacementMm,
                    DeformationZone = x.Latest.DeformationZone,
                    IsExceedingThreshold = x.Latest.IsExceedingThreshold,
                    MeasurementTime = x.Latest.MeasurementTime
                };
            });

        return Ok(mapData);
    }

    private static DeformationRecordDTO MapToDTO(DeformationRecord r) => new()
    {
        Id = r.Id,
        StationId = r.StationId,
        TiltAngleX = r.TiltAngleX,
        TiltAngleY = r.TiltAngleY,
        TiltMagnitude = r.TiltMagnitude,
        StrainValue = r.StrainValue,
        CalculatedDisplacementMm = r.CalculatedDisplacementMm,
        StressMpa = r.StressMpa,
        DeformationZone = r.DeformationZone,
        IsExceedingThreshold = r.IsExceedingThreshold,
        BeamCorrectionApplied = r.BeamCorrectionApplied,
        CorrectionAngleAzimuth = r.CorrectionAngleAzimuth,
        CorrectionAngleElevation = r.CorrectionAngleElevation,
        MeasurementTime = r.MeasurementTime
    };
}
