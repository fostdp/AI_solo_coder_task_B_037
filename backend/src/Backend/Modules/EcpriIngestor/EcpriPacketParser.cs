using System.Buffers;
using System.Text;
using System.Text.Json;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.EcpriIngestor;

public class EcpriPacketParser
{
    private readonly ILogger<EcpriPacketParser> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ArrayPool<byte> _bytePool;

    public EcpriPacketParser(ILogger<EcpriPacketParser> logger, IOptions<JsonOptions> jsonOptions)
    {
        _logger = logger;
        _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
        _bytePool = ArrayPool<byte>.Shared;
    }

    public (bool Success, ECPRIDataPacket? Packet, string? Error) TryParseTcpFrame(ReadOnlySpan<byte> frameData)
    {
        try
        {
            if (frameData.Length < 12)
            {
                return (false, null, "Frame too short, need at least 12 bytes header");
            }

            var header = frameData[..12];
            var payload = frameData[12..];

            var reader = new Utf8JsonReader(payload, new JsonReaderOptions { AllowTrailingCommas = true });
            var packet = JsonSerializer.Deserialize<ECPRIDataPacket>(ref reader, _jsonOptions);

            if (packet == null)
            {
                return (false, null, "Failed to deserialize packet");
            }

            return (true, packet, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing eCPRI TCP frame");
            return (false, null, ex.Message);
        }
    }

    public EcpriDataReceivedEvent MapToEvent(ECPRIDataPacket packet, string sourceProtocol)
    {
        var channelData = packet.Channels.Select(ch => new EcpriChannelData
        {
            ChannelIndex = ch.ChannelIndex,
            RowIndex = ch.RowIndex,
            ColumnIndex = ch.ColumnIndex,
            Amplitude = ch.Amplitude,
            Phase = ch.Phase,
            Swr = ch.Swr,
            Temperature = ch.Temperature,
            TxPower = ch.TxPower,
            RxPower = ch.RxPower,
            Ber = ch.Ber
        }).ToList().AsReadOnly();

        return new EcpriDataReceivedEvent
        {
            StationId = packet.StationId,
            StationCode = packet.StationCode,
            SequenceNumber = packet.SequenceNumber,
            Timestamp = packet.Timestamp,
            ChannelData = channelData,
            SourceProtocol = sourceProtocol
        };
    }

    public async Task WriteToInfluxDbAsync(
        EcpriDataReceivedEvent eventData,
        Repositories.IInfluxDBRepository influxRepo,
        CancellationToken cancellationToken)
    {
        var metrics = eventData.ChannelData.Select(ch => new ChannelMetrics
        {
            StationId = eventData.StationId.ToString(),
            ChannelId = ch.ChannelIndex.ToString(),
            Timestamp = eventData.Timestamp,
            Amplitude = ch.Amplitude,
            Phase = ch.Phase,
            Swr = ch.Swr,
            Temperature = ch.Temperature,
            TxPower = ch.TxPower,
            RxPower = ch.RxPower,
            Ber = ch.Ber
        }).ToList();

        await influxRepo.WriteChannelMetricsAsync(metrics, cancellationToken);
    }
}
