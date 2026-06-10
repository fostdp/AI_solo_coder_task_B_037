namespace PaEfficiencyTracker.Module.Models;

public class Channel
{
    public Guid Id { get; set; }
    public int ChannelIndex { get; set; }
    public double? TxPower { get; set; }
    public double Frequency { get; set; }
    public double Bandwidth { get; set; }
    public bool IsActive { get; set; }
}
