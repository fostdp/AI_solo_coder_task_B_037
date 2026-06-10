using AntennaMonitoring.DTOs;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Modules.EcpriIngestor;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ECPRIController : ControllerBase
{
    private readonly IEcpriIngestor _ecpriIngestor;
    private readonly IECPRIDataRepository _ecpriDataRepository;
    private readonly IBaseStationRepository _baseStationRepository;

    public ECPRIController(
        IEcpriIngestor ecpriIngestor,
        IECPRIDataRepository ecpriDataRepository,
        IBaseStationRepository baseStationRepository)
    {
        _ecpriIngestor = ecpriIngestor;
        _ecpriDataRepository = ecpriDataRepository;
        _baseStationRepository = baseStationRepository;
    }

    [HttpPost("data")]
    public async Task<ActionResult<ECPRIResponse>> ReceiveData(
        [FromBody] ECPRIDataPacket packet,
        CancellationToken cancellationToken = default)
    {
        if (packet == null)
        {
            return BadRequest("Packet data is required");
        }

        if (string.IsNullOrEmpty(packet.StationId))
        {
            return BadRequest("StationId is required");
        }

        if (!Guid.TryParse(packet.StationId, out var stationId))
        {
            return BadRequest("Invalid StationId format");
        }

        var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station == null)
        {
            return NotFound($"Base station with id {packet.StationId} not found");
        }

        await _ecpriIngestor.ProcessHttpPacketAsync(packet, cancellationToken);

        var response = new ECPRIResponse
        {
            Success = true,
            Message = "Packet processed successfully",
            SequenceNumber = packet.SequenceNumber,
            ProcessedAt = DateTime.UtcNow
        };

        return Ok(response);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<ECPRIBatchResponse>> ReceiveBatchData(
        [FromBody] IEnumerable<ECPRIDataPacket> packets,
        CancellationToken cancellationToken = default)
    {
        if (packets == null || !packets.Any())
        {
            return BadRequest("At least one packet is required");
        }

        var packetList = packets.ToList();
        var processedCount = 0;
        var failedPackets = new List<string>();

        foreach (var packet in packetList)
        {
            if (string.IsNullOrEmpty(packet.StationId))
            {
                failedPackets.Add($"Packet missing StationId");
                continue;
            }

            if (!Guid.TryParse(packet.StationId, out var stationId))
            {
                failedPackets.Add($"Invalid StationId: {packet.StationId}");
                continue;
            }

            var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
            if (station == null)
            {
                failedPackets.Add($"Station not found: {packet.StationId}");
                continue;
            }

            try
            {
                await _ecpriIngestor.ProcessHttpPacketAsync(packet, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                failedPackets.Add($"Packet {packet.SequenceNumber}: {ex.Message}");
            }
        }

        var response = new ECPRIBatchResponse
        {
            TotalPackets = packetList.Count,
            ProcessedCount = processedCount,
            FailedCount = failedPackets.Count,
            Errors = failedPackets
        };

        return Ok(response);
    }

    [HttpGet("status")]
    public async Task<ActionResult<ECPRIServiceStatus>> GetStatus(CancellationToken cancellationToken = default)
    {
        var status = _ecpriService.GetStatus();

        var totalPackets = await _ecpriDataRepository.GetTotalPacketsAsync(cancellationToken);
        var lastPacketTime = await _ecpriDataRepository.GetLastPacketTimeAsync(cancellationToken);

        var extendedStatus = new
        {
            status.IsRunning,
            status.ListenPort,
            status.BufferSize,
            status.SequenceNumber,
            status.CurrentTime,
            TotalProcessedPackets = totalPackets,
            LastPacketTime = lastPacketTime
        };

        return Ok(extendedStatus);
    }
}
