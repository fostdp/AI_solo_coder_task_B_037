using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using SpectrumScanner.Module;
using SpectrumScanner.Module.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpectrumController : ControllerBase
{
    private readonly ISpectrumScanRecordRepository _spectrumRepo;
    private readonly IBaseStationRepository _stationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly ISpectrumScanner _spectrumScanner;
    private readonly IMediator _mediator;

    public SpectrumController(
        ISpectrumScanRecordRepository spectrumRepo,
        IBaseStationRepository stationRepo,
        IChannelRepository channelRepo,
        ISpectrumScanner spectrumScanner,
        IMediator mediator)
    {
        _spectrumRepo = spectrumRepo;
        _stationRepo = stationRepo;
        _channelRepo = channelRepo;
        _spectrumScanner = spectrumScanner;
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SpectrumScanRecordDTO>>> GetRecords(
        Guid? stationId = null,
        bool? withInterferenceOnly = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<SpectrumScanRecord> records;
        if (withInterferenceOnly == true && stationId.HasValue)
        {
            records = await _spectrumRepo.GetWithInterferenceAsync(
                stationId.Value, pageSize, cancellationToken);
        }
        else if (stationId.HasValue)
        {
            records = await _spectrumRepo.GetByStationIdAsync(stationId.Value, pageSize, cancellationToken);
        }
        else
        {
            records = await _spectrumRepo.GetRecentAsync(pageSize, cancellationToken);
        }

        var paged = records
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);

        var dtos = paged.Select(MapToDTO);
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SpectrumScanRecordDTO>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await _spectrumRepo.GetByIdAsync(id, cancellationToken);
        if (record == null) return NotFound();

        return Ok(MapToDTO(record));
    }

    [HttpGet("latest")]
    public async Task<ActionResult<SpectrumScanRecordDTO>> GetLatest(
        [FromQuery] Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var records = await _spectrumRepo.GetByStationIdAsync(stationId, 1, cancellationToken);
        var record = records.FirstOrDefault();
        if (record == null) return NotFound();

        return Ok(MapToDTO(record));
    }

    [HttpPost("scan")]
    public async Task<ActionResult<SpectrumScanResultDTO>> RunSpectrumScan(
        [FromBody] RunSpectrumScanRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var channels = (await _channelRepo.GetByStationIdAsync(request.StationId, cancellationToken))
            .Where(c => c.Status == "normal")
            .ToList();

        if (!channels.Any())
        {
            return BadRequest("No active channels available for this station");
        }

        var result = await _spectrumScanner.PerformSpectrumScanAsync(
            request.StationId,
            request.StartFrequencyMhz ?? 1800,
            request.EndFrequencyMhz ?? 2700,
            request.StepFrequencyMhz ?? 1,
            channels.AsReadOnly(),
            request.ApplyNullSteering,
            cancellationToken);

        await _mediator.Publish(new SpectrumScannedEvent
        {
            StationId = request.StationId,
            Result = result,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return Ok(new SpectrumScanResultDTO
        {
            StationId = request.StationId,
            FrequencyPointsMhz = result.FrequencyPointsMhz,
            PowerLevelsDbm = result.PowerLevelsDbm,
            InterferenceCount = result.InterferenceFrequenciesMhz?.Length ?? 0,
            InterferenceFrequenciesMhz = result.InterferenceFrequenciesMhz,
            InterferenceDirectionsDeg = result.InterferenceDirectionsDeg,
            InterferencePowerDbm = result.InterferencePowerDbm,
            NullAnglesDeg = result.NullAnglesDeg,
            NullDepthsDb = result.NullDepthsDb,
            AutoNullSteeringApplied = result.AutoNullSteeringApplied,
            ScanTime = DateTime.UtcNow
        });
    }

    [HttpPost("null-steering")]
    public async Task<ActionResult<NullSteeringResultDTO>> ApplyNullSteering(
        [FromBody] ApplyNullSteeringRequest request,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(request.StationId, cancellationToken);
        if (station == null) return NotFound($"Station {request.StationId} not found");

        var channels = (await _channelRepo.GetByStationIdAsync(request.StationId, cancellationToken))
            .Where(c => c.Status == "normal")
            .ToList();

        if (!channels.Any())
        {
            return BadRequest("No active channels available");
        }

        var (angles, depths) = await _spectrumScanner.CalculateNullSteeringWeightsAsync(
            request.StationId,
            request.InterferenceDirectionsDeg,
            channels.AsReadOnly(),
            request.NullDepthTargetDb ?? 25,
            cancellationToken);

        await _channelRepo.BulkUpdateAsync(channels, cancellationToken);

        return Ok(new NullSteeringResultDTO
        {
            StationId = request.StationId,
            InterferenceDirectionsDeg = request.InterferenceDirectionsDeg,
            NullAnglesDeg = angles,
            NullDepthsDb = depths,
            ChannelCountUpdated = channels.Count,
            AppliedSuccessfully = true,
            Timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("chart-data/{scanId}")]
    public async Task<ActionResult<SpectrumChartDTO>> GetSpectrumChartData(
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        var record = await _spectrumRepo.GetByIdAsync(scanId, cancellationToken);
        if (record == null) return NotFound();

        var interferenceAnnotations = new List<SpectrumInterferenceAnnotationDTO>();
        if (record.InterferenceFrequenciesMhz != null)
        {
            for (int i = 0; i < record.InterferenceFrequenciesMhz.Length; i++)
            {
                var freq = record.InterferenceFrequenciesMhz[i];
                var powerIdx = Array.FindIndex(record.FrequencyPointsMhz, f => Math.Abs(f - freq) < 1);
                var power = powerIdx >= 0 ? record.PowerLevelsDbm[powerIdx] : -80;
                var direction = record.InterferenceDirectionsDeg != null && i < record.InterferenceDirectionsDeg.Length
                    ? record.InterferenceDirectionsDeg[i] : 0;
                var nullAngle = record.NullAnglesDeg != null && i < record.NullAnglesDeg.Length
                    ? record.NullAnglesDeg[i] : null;
                var nullDepth = record.NullDepthsDb != null && i < record.NullDepthsDb.Length
                    ? record.NullDepthsDb[i] : null;

                interferenceAnnotations.Add(new SpectrumInterferenceAnnotationDTO
                {
                    FrequencyMhz = freq,
                    PowerDbm = power,
                    DirectionDeg = direction,
                    NullAngleDeg = nullAngle,
                    NullDepthDb = nullDepth,
                    IsNullSteered = nullAngle.HasValue
                });
            }
        }

        return Ok(new SpectrumChartDTO
        {
            ScanId = scanId,
            StationId = record.StationId,
            FrequencyPointsMhz = record.FrequencyPointsMhz,
            PowerLevelsDbm = record.PowerLevelsDbm,
            InterferenceAnnotations = interferenceAnnotations,
            AutoNullSteeringApplied = record.AutoNullSteeringApplied,
            ScanTime = record.ScanTime
        });
    }

    [HttpGet("interference-summary")]
    public async Task<ActionResult<IEnumerable<SpectrumInterferenceSummaryDTO>>> GetInterferenceSummary(
        CancellationToken cancellationToken = default)
    {
        var stations = await _stationRepo.GetAllAsync(cancellationToken);
        var summaries = new List<SpectrumInterferenceSummaryDTO>();

        foreach (var station in stations)
        {
            var records = await _spectrumRepo.GetWithInterferenceAsync(station.Id, 10, cancellationToken);
            var latest = records.OrderByDescending(r => r.ScanTime).FirstOrDefault();

            if (latest != null && latest.InterferenceCount > 0)
            {
                summaries.Add(new SpectrumInterferenceSummaryDTO
                {
                    StationId = station.Id,
                    StationCode = station.StationCode,
                    StationName = station.Name,
                    ActiveInterferenceCount = latest.InterferenceCount,
                    InterferenceFrequenciesMhz = latest.InterferenceFrequenciesMhz,
                    InterferenceDirectionsDeg = latest.InterferenceDirectionsDeg,
                    NullSteeringApplied = latest.AutoNullSteeringApplied,
                    LatestScanTime = latest.ScanTime
                });
            }
        }

        return Ok(summaries);
    }

    [HttpGet("direction-of-arrival")]
    public async Task<ActionResult<DOAEstimationDTO>> EstimateDirectionOfArrival(
        [FromQuery] Guid stationId,
        [FromQuery] double frequencyMhz,
        CancellationToken cancellationToken = default)
    {
        var station = await _stationRepo.GetByIdAsync(stationId, cancellationToken);
        if (station == null) return NotFound();

        var channels = (await _channelRepo.GetByStationIdAsync(stationId, cancellationToken))
            .Where(c => c.Status == "normal")
            .ToList();

        if (channels.Count < 8)
        {
            return BadRequest("Need at least 8 active channels for DOA estimation");
        }

        var samples = channels
            .Select(c => new Complex(
                Math.Cos((double)c.CalibrationCoeffPhase),
                Math.Sin((double)c.CalibrationCoeffPhase)))
            .ToArray();

        var direction = await _spectrumScanner.EstimateDirectionOfArrivalAsync(
            samples, frequencyMhz, station.AntennaRows ?? 8, station.AntennaColumns ?? 8,
            station.ElementSpacingMeters ?? 0.085, cancellationToken);

        return Ok(new DOAEstimationDTO
        {
            StationId = stationId,
            FrequencyMhz = frequencyMhz,
            EstimatedDirectionDeg = direction,
            Confidence = 0.85,
            EstimationMethod = "MUSIC",
            Timestamp = DateTime.UtcNow
        });
    }

    private static SpectrumScanRecordDTO MapToDTO(SpectrumScanRecord r) => new()
    {
        Id = r.Id,
        StationId = r.StationId,
        InterferenceCount = r.InterferenceCount,
        InterferenceFrequenciesMhz = r.InterferenceFrequenciesMhz,
        InterferenceDirectionsDeg = r.InterferenceDirectionsDeg,
        NullAnglesDeg = r.NullAnglesDeg,
        NullDepthsDb = r.NullDepthsDb,
        AutoNullSteeringApplied = r.AutoNullSteeringApplied,
        InterferenceDetails = r.InterferenceDetails,
        ScanTime = r.ScanTime
    };
}
