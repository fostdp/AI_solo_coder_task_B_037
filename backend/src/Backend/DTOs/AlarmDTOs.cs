namespace AntennaMonitoring.DTOs;

public class AlarmDTO
{
    public Guid Id { get; set; }
    public string AlarmCode { get; set; } = string.Empty;
    public string AlarmType { get; set; } = string.Empty;
    public string AlarmLevel { get; set; } = string.Empty;
    public Guid StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public Guid? ChannelId { get; set; }
    public int? ChannelIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? ThresholdValue { get; set; }
    public decimal? ActualValue { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AlarmSummaryDTO
{
    public int TotalActive { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Info { get; set; }
    public int Acknowledged { get; set; }
    public int Unacknowledged { get; set; }
}

public class CreateAlarmDTO
{
    public string AlarmCode { get; set; } = string.Empty;
    public string AlarmType { get; set; } = string.Empty;
    public string AlarmLevel { get; set; } = string.Empty;
    public Guid StationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? ThresholdValue { get; set; }
    public decimal? ActualValue { get; set; }
}

public class AcknowledgeAlarmDTO
{
    public string AcknowledgedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class ClearAlarmDTO
{
    public string ClearedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
