namespace AntennaMonitoring.DTOs;

public class ECPRIDataPacket
{
    public string Version { get; set; } = "1.0";
    public string MessageType { get; set; } = "channel_metrics";
    public string StationId { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public int SequenceNumber { get; set; }
    public List<ChannelData> Channels { get; set; } = new();
}

public class ChannelData
{
    public int ChannelIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public double Amplitude { get; set; }
    public double Phase { get; set; }
    public double Swr { get; set; }
    public double PaTemperature { get; set; }
    public double TxPower { get; set; }
    public double RxPower { get; set; }
    public double Ber { get; set; }
}

public class ECPRIResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ProcessedChannels { get; set; }
    public long ReceivedTimestamp { get; set; }
}

public class ECPRIBatchResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalPackets { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalProcessedChannels { get; set; }
    public long ReceivedTimestamp { get; set; }
    public List<ECPRIResponse> Results { get; set; } = new();
}

public class ECPRIServiceStatus
{
    public bool IsRunning { get; set; }
    public int ListenPort { get; set; }
    public int BufferSize { get; set; }
    public int SequenceNumber { get; set; }
    public long CurrentTime { get; set; }
}

public class BeamPatternDTO
{
    public Guid StationId { get; set; }
    public double Azimuth { get; set; }
    public double Elevation { get; set; }
    public List<double> GainPattern { get; set; } = new();
    public double Sll { get; set; }
    public double BeamwidthAzimuth { get; set; }
    public double BeamwidthElevation { get; set; }
    public DateTime CalculatedAt { get; set; }
}
