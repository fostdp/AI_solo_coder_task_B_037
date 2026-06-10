namespace PaEfficiencyTracker.Module.Models.Events;

public class PaEfficiencyCompletedEvent : INotification
{
    public Guid StationId { get; }
    public IReadOnlyList<PaEfficiencyResult> Results { get; }
    public DateTime Timestamp { get; }

    public PaEfficiencyCompletedEvent(
        Guid stationId,
        IReadOnlyList<PaEfficiencyResult> results,
        DateTime timestamp)
    {
        StationId = stationId;
        Results = results;
        Timestamp = timestamp;
    }
}
