using MediatR;

namespace AntennaMonitoring.Messages;

public class CalibrationCompletedEvent : INotification
{
    public Guid StationId { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public double SllBefore { get; set; }
    public double SllAfter { get; set; }
    public bool Converged { get; set; }
    public DateTime CalibrationTime { get; set; }
    public int ChannelCount { get; set; }
    public IReadOnlyList<Guid> UpdatedChannelIds { get; set; } = Array.Empty<Guid>();
}
