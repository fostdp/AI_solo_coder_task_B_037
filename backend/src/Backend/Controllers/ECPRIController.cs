using AntennaMonitoring.DTOs;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Services;
using Microsoft.AspNetCore.Mvc;

namespace AntennaMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ECPRIController : ControllerBase
{
    private readonly IECPRIService _ecpriService;
    private readonly IECPRIDataRepository _ecpriDataRepository;
    private readonly IBaseStationRepository _baseStationRepository;

    public ECPRIController(
        IECPRIService ecpriService,
        IECPRIDataRepository ecpriDataRepository,
        IBaseStationRepository baseStationRepository)
    {
        _ecpriService = ecpriService;
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

        var response = await _ecpriService.ProcessDataPacketAsync(packet, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response);
        }

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
        foreach (var packet in packetList)
        {
            if (string.IsNullOrEmpty(packet.StationId))
            {
                return BadRequest("StationId is required for all packets");
            }

            if (!Guid.TryParse(packet.StationId, out var stationId))
            {
                return BadRequest($"Invalid StationId format: {packet.StationId}");
            }

            var station = await _baseStationRepository.GetByIdAsync(stationId, cancellationToken);
            if (station == null)
            {
                return NotFound($"Base station with id {packet.StationId} not found");
            }
        }

        var response = await _ecpriService.ProcessBatchDataAsync(packetList, cancellationToken);

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
