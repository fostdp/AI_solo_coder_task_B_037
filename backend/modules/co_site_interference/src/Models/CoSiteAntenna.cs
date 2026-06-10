namespace CoSiteInterference.Module.Models;

public record CoSiteAntenna
{
    public Guid Id { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string AntennaType { get; set; } = string.Empty;
    public double FrequencyStartMhz { get; set; }
    public double FrequencyEndMhz { get; set; }
    public double TransmitPowerDbm { get; set; }
    public double SeparationDistanceMeters { get; set; }
    public double AzimuthAngleDeg { get; set; }
    public double ElevationAngleDeg { get; set; }
    public double HeightOffsetMeters { get; set; }
    public bool IsApproximated { get; set; }
}
