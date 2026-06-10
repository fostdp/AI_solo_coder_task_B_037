namespace DeformationMonitor.Module.Models;

public record DeformationResult
{
    public Guid StationId { get; init; }
    public int SensorIndex { get; init; }
    public double CalculatedDisplacementMm { get; init; }
    public double StressMpa { get; init; }
    public string DeformationZone { get; init; } = string.Empty;
    public bool ExceedsThreshold { get; init; }
    public double CorrectionAngleAzimuth { get; init; }
    public double CorrectionAngleElevation { get; init; }
    public bool CorrectionApplied { get; init; }
    public double TiltAngleX { get; init; }
    public double TiltAngleY { get; init; }
    public double TiltAngleZ { get; init; }
    public double StrainValue { get; init; }
    public bool IsInterpolated { get; init; }
    public bool IsAnomaly { get; init; }
}

public record DeformationRecord
{
    public Guid Id { get; init; }
    public Guid StationId { get; init; }
    public int SensorIndex { get; init; }
    public double TiltAngleX { get; init; }
    public double TiltAngleY { get; init; }
    public double TiltAngleZ { get; init; }
    public double StrainValue { get; init; }
    public double Temperature { get; init; }
    public double CalculatedDisplacementMm { get; init; }
    public double StressMpa { get; init; }
    public string DeformationZone { get; init; } = string.Empty;
    public bool BeamCorrectionApplied { get; init; }
    public double CorrectionAngleAzimuth { get; init; }
    public double CorrectionAngleElevation { get; init; }
    public double WindSpeed { get; init; }
    public DateTime MeasurementTime { get; init; }
    public DateTime CreatedAt { get; init; }
}
