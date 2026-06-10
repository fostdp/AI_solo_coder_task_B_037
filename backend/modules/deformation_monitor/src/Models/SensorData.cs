namespace DeformationMonitor.Module.Models;

public record SensorData
{
    public Guid StationId { get; init; }
    public int SensorIndex { get; init; }
    public string SensorType { get; init; } = string.Empty;
    public double TiltAngleX { get; init; }
    public double TiltAngleY { get; init; }
    public double TiltAngleZ { get; init; }
    public double StrainValue { get; init; }
    public double Temperature { get; init; }
    public double WindSpeed { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsAnomaly { get; init; }
    public bool IsInterpolated { get; init; }
}
