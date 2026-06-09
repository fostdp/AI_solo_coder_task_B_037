namespace AntennaMonitoring.Models;

public class Alarm
{
    public Guid Id { get; set; }
    public string AlarmCode { get; set; } = string.Empty;
    public string AlarmType { get; set; } = string.Empty;
    public string AlarmLevel { get; set; } = string.Empty;
    public Guid StationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? ThresholdValue { get; set; }
    public decimal? ActualValue { get; set; }
    public string Status { get; set; } = "active";
    public bool Acknowledged { get; set; } = false;
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
    public bool MqttPublished { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
    public Channel? Channel { get; set; }
}
