using AntennaMonitoring.Messages;

namespace AntennaMonitoring.Modules.EcpriIngestor;

public interface IEcpriIngestor
{
    Task ProcessTcpPacketAsync(ReadOnlyMemory<byte> packetData, string remoteEndPoint, CancellationToken cancellationToken);
    Task ProcessHttpPacketAsync(Models.ECPRIDataPacket packet, CancellationToken cancellationToken);
    Task ProcessMqttPacketAsync(Models.ECPRIDataPacket packet, string topic, CancellationToken cancellationToken);
}
