using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using CoSiteInterference.Module;
using CoSiteInterference.Module.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InterferenceController : ControllerBase
{
    private readonly ICoSiteInterferenceRecordRepository _interferenceRepo;
    private readonly ICoSiteAntennaRepository _antennaRepo;
    private readonly IBaseStationRepository _stationRepo;
    private readonly ICoSiteInterferenceAnalyzer _interferenceAnalyzer;
    private readonly IMediator _mediator;

    public InterferenceController(
        ICoSiteInterferenceRecordRepository interferenceRepo,
        ICoSiteAntennaRepository antennaRepo,
        IBaseStationRepository stationRepo,
        ICoSiteInterferenceAnalyzer interferenceAnalyzer,
        IMediator mediator)
    {
        _interferenceRepo = interferenceRepo;
        _antennaRepo = antennaRepo;
        _stationRepo = stationRepo;
        _interferenceAnalyzer = interferenceAnalyzer;
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CoSiteInterferenceRecordDTO>>> GetRecords(
        Guid? stationId = null,
        bool? insufficientOnly = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<CoSiteInterferenceRecord> records;
        if (insufficientOnly == true && stationId.HasValue)
        {
            records = await _interferenceRepo.GetWithInsufficientIsolationAsync(
                stationId.Value, pageSize, cancellationToken);
        }
        else if (stationId.HasValue)
        {
            records = await _interferenceRepo.GetByStationIdAsync(stationId.Value, pageSize, cancellationToken);
        }
        else
        {
            records = await _interferenceRepo.GetRecentAsync(pageSize, cancellationToken);
        }

        var paged = records
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);

        var dtos = paged.Select(MapToDTO);
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CoSiteInterferenceRecordDTO>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await _interferenceRepo.GetByIdAsync(id, cancellationToken);
        if (record == null) return NotFound();

        return Ok(MapToDTO(record));
    }

    [HttpGet("antennas")]
    public async Task<ActionResult<IEnumerable<CoSiteAntennaDTO>>> GetCoSiteAntennas(
        [FromQuery] Guid? stationId = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<CoSiteAntennaEntity> antennas;
        if (stationId.HasValue)
        {
            antennas = await _antennaRepo.GetByStationIdAsync(stationId.Value, cancellationToken);
        }
        else
        {
            antennas = await _antennaRepo.GetAllAsync(cancellationToken);
        }

        var dtos = antennas.Select(a => new CoSiteAntennaDTO
        {
            Id = a.Id,
            StationId = a.StationId,
            OperatorName = a.OperatorName,
            AntennaType = a.AntennaType,
            FrequencyBandStartMhz = a.FrequencyBandStartMhz,
            FrequencyBandEndMhz = a.FrequencyBandEndMhz,
            TransmitPowerDbm = a.TransmitPowerDbm,
            SeparationDistanceMeters = a.SeparationDistanceMeters,
            AzimuthAngleDeg = a.AzimuthAngleDeg,
            ElevationAngleDeg = a.ElevationAngleDeg,
            HeightOffsetMeters = a.HeightOffsetMeters,
            Status = a.Status
        });

        return Ok(dtos);
    }

    [HttpPost("antennas")]
    public async Task<ActionResult<CoSiteAntennaDTO>> CreateAntenna(
        [FromBody] CreateCoSiteAntennaRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var antenna = new CoSiteAntennaEntity
        {
            Id = Guid.NewGuid(),
            StationId = request.StationId,
            OperatorName = request.OperatorName,
            AntennaType = request.AntennaType,
            FrequencyBandStartMhz = request.FrequencyBandStartMhz,
            FrequencyBandEndMhz = request.FrequencyBandEndMhz,
            TransmitPowerDbm = request.TransmitPowerDbm,
            SeparationDistanceMeters = request.SeparationDistanceMeters,
            AzimuthAngleDeg = request.AzimuthAngleDeg,
            ElevationAngleDeg = request.ElevationAngleDeg,
            HeightOffsetMeters = request.HeightOffsetMeters,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _antennaRepo.AddAsync(antenna, cancellationToken);

        return CreatedAtAction(nameof(GetCoSiteAntennas), new { id = antenna.Id },
            new CoSiteAntennaDTO
            {
                Id = antenna.Id,
                StationId = antenna.StationId,
                OperatorName = antenna.OperatorName,
                AntennaType = antenna.AntennaType,
                FrequencyBandStartMhz = antenna.FrequencyBandStartMhz,
                FrequencyBandEndMhz = antenna.FrequencyBandEndMhz,
                TransmitPowerDbm = antenna.TransmitPowerDbm,
                SeparationDistanceMeters = antenna.SeparationDistanceMeters,
                AzimuthAngleDeg = antenna.AzimuthAngleDeg,
                ElevationAngleDeg = antenna.ElevationAngleDeg,
                HeightOffsetMeters = antenna.HeightOffsetMeters,
                Status = antenna.Status
            });
    }

    [HttpPut("antennas/{id}")]
    public async Task<IActionResult> UpdateAntenna(
        Guid id,
        [FromBody] UpdateCoSiteAntennaRequest request,
        CancellationToken cancellationToken = default)
    {
        var antenna = await _antennaRepo.GetByIdAsync(id, cancellationToken);
        if (antenna == null) return NotFound();

        antenna.OperatorName = request.OperatorName ?? antenna.OperatorName;
        antenna.AntennaType = request.AntennaType ?? antenna.AntennaType;
        antenna.FrequencyBandStartMhz = request.FrequencyBandStartMhz ?? antenna.FrequencyBandStartMhz;
        antenna.FrequencyBandEndMhz = request.FrequencyBandEndMhz ?? antenna.FrequencyBandEndMhz;
        antenna.TransmitPowerDbm = request.TransmitPowerDbm ?? antenna.TransmitPowerDbm;
        antenna.SeparationDistanceMeters = request.SeparationDistanceMeters ?? antenna.SeparationDistanceMeters;
        antenna.AzimuthAngleDeg = request.AzimuthAngleDeg ?? antenna.AzimuthAngleDeg;
        antenna.ElevationAngleDeg = request.ElevationAngleDeg ?? antenna.ElevationAngleDeg;
        antenna.HeightOffsetMeters = request.HeightOffsetMeters ?? antenna.HeightOffsetMeters;
        antenna.Status = request.Status ?? antenna.Status;
        antenna.UpdatedAt = DateTime.UtcNow;

        await _antennaRepo.UpdateAsync(antenna, cancellationToken);
        return NoContent();
    }

    [HttpDelete("antennas/{id}")]
    public async Task<IActionResult> DeleteAntenna(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var antenna = await _antennaRepo.GetByIdAsync(id, cancellationToken);
        if (antenna == null) return NotFound();

        await _antennaRepo.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<IEnumerable<CoSiteInterferenceResultDTO>>> AnalyzeInterference(
        [FromBody] AnalyzeInterferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var coSiteAntennas = (await _antennaRepo.GetByStationIdAsync(request.StationId, cancellationToken))
            .Where(a => a.Status == "active")
            .ToList();

        if (!coSiteAntennas.Any())
        {
            return BadRequest("No active co-site antennas configured for this station");
        }

        var selfAntenna = new CoSiteAntenna
        {
            Id = Guid.Empty,
            OperatorName = "self",
            FrequencyBandStartMhz = station.FrequencyBandStartMhz,
            FrequencyBandEndMhz = station.FrequencyBandEndMhz,
            TransmitPowerDbm = station.MaxTransmitPowerDbm,
            SeparationDistanceMeters = 0,
            AzimuthAngleDeg = station.AzimuthAngle ?? 0,
            ElevationAngleDeg = station.ElevationAngle ?? 0,
            HeightOffsetMeters = 0
        };

        var results = new List<CoSiteInterferenceResultDTO>();
        foreach (var antennaEntity in coSiteAntennas)
        {
            var interferingAntenna = new CoSiteAntenna
            {
                Id = antennaEntity.Id,
                OperatorName = antennaEntity.OperatorName,
                AntennaType = antennaEntity.AntennaType,
                FrequencyBandStartMhz = antennaEntity.FrequencyBandStartMhz,
                FrequencyBandEndMhz = antennaEntity.FrequencyBandEndMhz,
                TransmitPowerDbm = antennaEntity.TransmitPowerDbm,
                SeparationDistanceMeters = antennaEntity.SeparationDistanceMeters,
                AzimuthAngleDeg = antennaEntity.AzimuthAngleDeg,
                ElevationAngleDeg = antennaEntity.ElevationAngleDeg,
                HeightOffsetMeters = antennaEntity.HeightOffsetMeters
            };

            var result = await _interferenceAnalyzer.AnalyzeInterferenceAsync(
                request.StationId, selfAntenna, interferingAntenna, cancellationToken);

            await _mediator.Publish(new InterferenceAnalyzedEvent
            {
                StationId = request.StationId,
                Result = result,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            results.Add(new CoSiteInterferenceResultDTO
            {
                InterferingAntennaId = result.InterferingAntennaId,
                InterferingOperator = result.InterferingOperator,
                InterferingAntennaType = result.InterferingAntennaType,
                DistanceMeters = result.DistanceMeters,
                IsolationDb = result.IsolationDb,
                CouplingCoefficient = result.CouplingCoefficient,
                IsIsolationSufficient = result.IsIsolationSufficient,
                InterferenceVectorX = result.InterferenceVectorX,
                InterferenceVectorY = result.InterferenceVectorY,
                InterferenceVectorZ = result.InterferenceVectorZ,
                Recommendation = result.Recommendation,
                AnalysisTime = DateTime.UtcNow
            });
        }

        return Ok(results);
    }

    [HttpGet("3d-vectors")]
    public async Task<ActionResult<IEnumerable<InterferenceVector3DDTO>>> Get3DVectors(
        [FromQuery] Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var records = await _interferenceRepo.GetByStationIdAsync(stationId, 10, cancellationToken);
        var station = await _stationRepo.GetByIdAsync(stationId, cancellationToken);
        if (station == null) return NotFound();

        var vectors = records
            .GroupBy(r => r.InterferingAntennaId)
            .Select(g => new InterferenceVector3DDTO
            {
                StationId = stationId,
                InterferingAntennaId = g.Key,
                InterferingOperator = g.First().InterferingOperator,
                StartX = 0,
                StartY = 0,
                StartZ = 0,
                EndX = g.First().InterferenceVectorX * 5,
                EndY = g.First().InterferenceVectorY * 5,
                EndZ = g.First().InterferenceVectorZ * 5,
                IsolationDb = g.Max(r => r.IsolationDb),
                IsIsolationSufficient = g.All(r => r.IsIsolationSufficient),
                LatestMeasurementTime = g.Max(r => r.MeasurementTime)
            });

        return Ok(vectors);
    }

    private static CoSiteInterferenceRecordDTO MapToDTO(CoSiteInterferenceRecord r) => new()
    {
        Id = r.Id,
        StationId = r.StationId,
        InterferingAntennaId = r.InterferingAntennaId,
        InterferingOperator = r.InterferingOperator,
        InterferingAntennaType = r.InterferingAntennaType,
        DistanceMeters = r.DistanceMeters,
        IsolationDb = r.IsolationDb,
        CouplingCoefficient = r.CouplingCoefficient,
        IsIsolationSufficient = r.IsIsolationSufficient,
        InterferenceVectorX = r.InterferenceVectorX,
        InterferenceVectorY = r.InterferenceVectorY,
        InterferenceVectorZ = r.InterferenceVectorZ,
        Recommendation = r.Recommendation,
        MeasurementTime = r.MeasurementTime
    };
}
