namespace AntennaMonitoring.Models;

public class CoSiteAntennaEntity
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string? AntennaType { get; set; }
    public double FrequencyBandStartMhz { get; set; }
    public double FrequencyBandEndMhz { get; set; }
    public double TransmitPowerDbm { get; set; }
    public double SeparationDistanceMeters { get; set; }
    public double AzimuthAngleDeg { get; set; }
    public double ElevationAngleDeg { get; set; }
    public double HeightOffsetMeters { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
}

public class SensorMetric
{
    public string StationId { get; set; } = string.Empty;
    public int SensorIndex { get; set; }
    public string SensorType { get; set; } = string.Empty;
    public double TiltAngleX { get; set; }
    public double TiltAngleY { get; set; }
    public double TiltAngleZ { get; set; }
    public double StrainValue { get; set; }
    public double Temperature { get; set; }
    public double WindSpeed { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
