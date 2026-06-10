using MediatR;

namespace AntennaMonitoring.Messages;

public class AlarmTriggeredEvent : INotification
{
    public Guid AlarmId { get; set; }
    public Guid StationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string AlarmCode { get; set; } = string.Empty;
    public string AlarmLevel { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double ActualValue { get; set; }
    public double ThresholdValue { get; set; }
    public DateTime TriggeredAt { get; set; }
    public bool IsSectorLevel { get; set; }
    public int AffectedChannelCount { get; set; }
}
