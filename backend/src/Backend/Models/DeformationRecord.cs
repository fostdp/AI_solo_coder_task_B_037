namespace AntennaMonitoring.Models;

public class DeformationRecord
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public int SensorIndex { get; set; }
    public double TiltAngleX { get; set; }
    public double TiltAngleY { get; set; }
    public double TiltAngleZ { get; set; }
    public double StrainValue { get; set; }
    public double Temperature { get; set; }
    public double CalculatedDisplacementMm { get; set; }
    public double StressMpa { get; set; }
    public string? DeformationZone { get; set; }
    public bool BeamCorrectionApplied { get; set; }
    public double CorrectionAngleAzimuth { get; set; }
    public double CorrectionAngleElevation { get; set; }
    public double WindSpeed { get; set; }
    public DateTime MeasurementTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
}
