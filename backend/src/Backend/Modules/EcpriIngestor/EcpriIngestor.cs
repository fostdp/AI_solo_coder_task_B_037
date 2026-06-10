using MediatR;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.EcpriIngestor;

public class EcpriIngestor : IEcpriIngestor
{
    private readonly ILogger<EcpriIngestor> _logger;
    private readonly IMediator _mediator;
    private readonly IDataChannels _dataChannels;
    private readonly EcpriPacketParser _parser;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly ECPRIOptions _options;

    public EcpriIngestor(
        ILogger<EcpriIngestor> logger,
        IMediator mediator,
        IDataChannels dataChannels,
        EcpriPacketParser parser,
        IInfluxDBRepository influxRepo,
        IOptions<ECPRIOptions> options)
    {
        _logger = logger;
        _mediator = mediator;
        _dataChannels = dataChannels;
        _parser = parser;
        _influxRepo = influxRepo;
        _options = options.Value;
    }

    public async Task ProcessTcpPacketAsync(ReadOnlyMemory<byte> packetData, string remoteEndPoint, CancellationToken cancellationToken)
    {
        try
        {
            var parseResult = _parser.TryParseTcpFrame(packetData.Span);
            if (!parseResult.Success || parseResult.Packet == null)
            {
                _logger.LogWarning("Failed to parse TCP packet from {EndPoint}: {Error}", remoteEndPoint, parseResult.Error);
                return;
            }

            await ProcessParsedPacketAsync(parseResult.Packet, "TCP", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TCP packet from {EndPoint}", remoteEndPoint);
        }
    }

    public async Task ProcessHttpPacketAsync(ECPRIDataPacket packet, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessParsedPacketAsync(packet, "HTTP", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HTTP packet from station {StationId}", packet.StationId);
        }
    }

    public async Task ProcessMqttPacketAsync(ECPRIDataPacket packet, string topic, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessParsedPacketAsync(packet, "MQTT", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT packet from topic {Topic}", topic);
        }
    }

    private async Task ProcessParsedPacketAsync(ECPRIDataPacket packet, string sourceProtocol, CancellationToken cancellationToken)
    {
        var eventData = _parser.MapToEvent(packet, sourceProtocol);

        await _parser.WriteToInfluxDbAsync(eventData, _influxRepo, cancellationToken);

        await _mediator.Publish(eventData, cancellationToken);

        await _dataChannels.EcpriDataWriter.WriteAsync(eventData, cancellationToken);

        _logger.LogInformation(
            "eCPRI data processed: Station={StationId}, Seq={Seq}, Channels={Count}, Protocol={Protocol}",
            eventData.StationId, eventData.SequenceNumber, eventData.ChannelData.Count, sourceProtocol);
    }
}
