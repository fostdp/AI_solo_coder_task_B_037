namespace CoSiteInterference.Module.Models;

public class CoSiteInterferenceOptions
{
    public double IsolationThresholdDb { get; set; } = 30.0;
    public int IntervalMinutes { get; set; } = 10;
    public double FrequencyOverlapThreshold { get; set; } = 0.1;
    public double CouplingModelAccuracy { get; set; } = 0.85;
    public double FastCalculationDistanceThresholdMeters { get; set; } = 100.0;
    public int PcaDimensions { get; set; } = 3;
    public int CacheCapacity { get; set; } = 1000;
}
