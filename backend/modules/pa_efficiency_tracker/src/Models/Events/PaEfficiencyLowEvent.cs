namespace PaEfficiencyTracker.Module.Models.Events;

public class PaEfficiencyLowEvent : INotification
{
    public Guid StationId { get; }
    public Guid ChannelId { get; }
    public int ChannelIndex { get; }
    public double EfficiencyPercent { get; }
    public double ThresholdPercent { get; }
    public string Reason { get; }
    public DateTime Timestamp { get; }

    public PaEfficiencyLowEvent(
        Guid stationId,
        Guid channelId,
        int channelIndex,
        double efficiencyPercent,
        double thresholdPercent,
        string reason,
        DateTime timestamp)
    {
        StationId = stationId;
        ChannelId = channelId;
        ChannelIndex = channelIndex;
        EfficiencyPercent = efficiencyPercent;
        ThresholdPercent = thresholdPercent;
        Reason = reason;
        Timestamp = timestamp;
    }
}
