namespace DeformationMonitor.Module.Models;

public record DeformationRequest
{
    public Guid StationId { get; init; }
    public IReadOnlyList<SensorData> SensorData { get; init; } = Array.Empty<SensorData>();
    public IReadOnlyList<Channel> Channels { get; init; } = Array.Empty<Channel>();
}

public record Channel
{
    public Guid Id { get; init; }
    public int ColumnIndex { get; init; }
    public int RowIndex { get; init; }
    public double? CalibrationCoeffPhase { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
