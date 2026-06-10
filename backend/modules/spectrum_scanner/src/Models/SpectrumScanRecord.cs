namespace SpectrumScanner.Module.Models;

public class SpectrumScanRecord
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public double StartFrequencyMhz { get; set; }
    public double EndFrequencyMhz { get; set; }
    public double ResolutionBandwidthKhz { get; set; }
    public double[]? FrequencyPointsMhz { get; set; }
    public double[]? PowerLevelsDbm { get; set; }
    public int InterferenceCount { get; set; }
    public string? InterferenceDetails { get; set; }
    public double[]? InterferenceFrequenciesMhz { get; set; }
    public double[]? InterferencePowersDbm { get; set; }
    public double[]? InterferenceDirectionsDeg { get; set; }
    public bool NullSteeringApplied { get; set; }
    public double[]? NullAnglesDeg { get; set; }
    public double[]? NullDepthsDb { get; set; }
    public double NoiseFloorDbm { get; set; }
    public double SpuriousFreeDynamicRangeDb { get; set; }
    public DateTime ScanTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
