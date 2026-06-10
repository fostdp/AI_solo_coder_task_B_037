using MediatR;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Messages;

public class EcpriDataReceivedEvent : INotification
{
    public Guid StationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public DateTime Timestamp { get; set; }
    public IReadOnlyList<EcpriChannelData> ChannelData { get; set; } = Array.Empty<EcpriChannelData>();
    public string SourceProtocol { get; set; } = string.Empty;
}

public class EcpriChannelData
{
    public int ChannelIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public double Amplitude { get; set; }
    public double Phase { get; set; }
    public double Swr { get; set; }
    public double Temperature { get; set; }
    public double TxPower { get; set; }
    public double RxPower { get; set; }
    public double Ber { get; set; }
}
