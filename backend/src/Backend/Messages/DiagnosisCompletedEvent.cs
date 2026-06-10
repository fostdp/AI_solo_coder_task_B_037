using MediatR;

namespace AntennaMonitoring.Messages;

public class DiagnosisCompletedEvent : INotification
{
    public Guid StationId { get; set; }
    public string ModelType { get; set; } = string.Empty;
    public DateTime DiagnosisTime { get; set; }
    public int ChannelCount { get; set; }
    public int HighRiskChannelCount { get; set; }
    public double AverageFailureProbability { get; set; }
    public IReadOnlyList<Guid> HighRiskChannelIds { get; set; } = Array.Empty<Guid>();
}
