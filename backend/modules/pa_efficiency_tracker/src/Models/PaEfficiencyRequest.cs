namespace PaEfficiencyTracker.Module.Models;

public record PaEfficiencyRequest
{
    public Guid StationId { get; set; }
    public IReadOnlyList<Channel> Channels { get; set; } = Array.Empty<Channel>();
    public IReadOnlyList<ChannelMetric> RecentMetrics { get; set; } = Array.Empty<ChannelMetric>();
}
