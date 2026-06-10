namespace SpectrumScanner.Module.Models;

public class SpectrumScanOptions
{
    public double StartFrequencyMhz { get; set; } = 3400.0;
    public double EndFrequencyMhz { get; set; } = 3600.0;
    public double ResolutionBandwidthKhz { get; set; } = 100.0;
    public double InterferencePowerThresholdDbm { get; set; } = -80.0;
    public double NullDepthTargetDb { get; set; } = 25.0;
    public int MaxNullCount { get; set; } = 3;
    public int IntervalMinutes { get; set; } = 15;
    public bool AutoNullSteering { get; set; } = true;
    public double DoaEstimationAccuracy { get; set; } = 0.9;
    public double WidebandThresholdMhz { get; set; } = 5.0;
    public double SubbandWidthMhz { get; set; } = 2.0;
    public int MaxSubbands { get; set; } = 8;
    public double DiagonalLoadingLevel { get; set; } = 0.1;
    public bool EnableGpuAcceleration { get; set; } = true;
    public int GpuBatchSize { get; set; } = 64;
}
