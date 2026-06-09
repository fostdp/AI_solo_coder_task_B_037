namespace AntennaMonitoring.Models;

public class ChannelMetrics
{
    public string StationId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public int ChannelIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public double Amplitude { get; set; }
    public double Phase { get; set; }
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double Swr { get; set; }
    public double PaTemperature { get; set; }
    public double TxPower { get; set; }
    public double RxPower { get; set; }
    public double Ber { get; set; }
    public string DataSource { get; set; } = "ecpri";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
