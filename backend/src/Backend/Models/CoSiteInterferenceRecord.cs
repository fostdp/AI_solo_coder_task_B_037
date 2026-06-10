namespace AntennaMonitoring.Models;

public class CoSiteInterferenceRecord
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public string? InterferingOperator { get; set; }
    public string? InterferingAntennaType { get; set; }
    public double InterferingFrequencyMhz { get; set; }
    public double InterferingPowerDbm { get; set; }
    public double SeparationDistanceMeters { get; set; }
    public double AzimuthAngleDeg { get; set; }
    public double ElevationAngleDeg { get; set; }
    public double IsolationDb { get; set; }
    public double CouplingCoefficient { get; set; }
    public double InterferenceMarginDb { get; set; }
    public bool IsIsolationSufficient { get; set; }
    public string? Recommendation { get; set; }
    public double InterferenceVectorX { get; set; }
    public double InterferenceVectorY { get; set; }
    public double InterferenceVectorZ { get; set; }
    public double AffectedBandStartMhz { get; set; }
    public double AffectedBandEndMhz { get; set; }
    public DateTime MeasurementTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
}
