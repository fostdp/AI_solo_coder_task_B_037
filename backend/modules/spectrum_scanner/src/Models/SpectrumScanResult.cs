namespace SpectrumScanner.Module.Models;

public record SpectrumScanResult
{
    public Guid StationId { get; set; }
    public double[] FrequencyPointsMhz { get; set; } = Array.Empty<double>();
    public double[] PowerLevelsDbm { get; set; } = Array.Empty<double>();
    public int InterferenceCount { get; set; }
    public string InterferenceDetails { get; set; } = string.Empty;
    public double[] InterferenceFrequenciesMhz { get; set; } = Array.Empty<double>();
    public double[] InterferencePowersDbm { get; set; } = Array.Empty<double>();
    public double[] InterferenceDirectionsDeg { get; set; } = Array.Empty<double>();
    public bool NullSteeringApplied { get; set; }
    public double[] NullAnglesDeg { get; set; } = Array.Empty<double>();
    public double[] NullDepthsDb { get; set; } = Array.Empty<double>();
    public double NoiseFloorDbm { get; set; }
    public double SpuriousFreeDynamicRangeDb { get; set; }
    public bool GpuAccelerated { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}
