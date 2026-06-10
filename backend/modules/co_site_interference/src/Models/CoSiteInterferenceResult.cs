namespace CoSiteInterference.Module.Models;

public record CoSiteInterferenceResult
{
    public Guid StationId { get; set; }
    public Guid InterferingAntennaId { get; set; }
    public string InterferingOperator { get; set; } = string.Empty;
    public double IsolationDb { get; set; }
    public double CouplingCoefficient { get; set; }
    public double InterferenceMarginDb { get; set; }
    public bool IsIsolationSufficient { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public double VectorX { get; set; }
    public double VectorY { get; set; }
    public double VectorZ { get; set; }
    public bool IsApproximated { get; set; }
    public double AffectedBandStartMhz { get; set; }
    public double AffectedBandEndMhz { get; set; }
}
