namespace PaEfficiencyTracker.Module.Models;

public record ChannelMetric
{
    public string ChannelId { get; set; } = string.Empty;
    public int ChannelIndex { get; set; }
    public double TxPower { get; set; }
    public double PaTemperature { get; set; }
    public double Amplitude { get; set; }
    public double Phase { get; set; }
    public double Swr { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
