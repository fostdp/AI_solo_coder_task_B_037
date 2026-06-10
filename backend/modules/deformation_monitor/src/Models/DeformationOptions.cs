namespace DeformationMonitor.Module.Models;

public class DeformationOptions
{
    public double ThresholdMm { get; init; } = 0.5;
    public int MemSensorCount { get; init; } = 9;
    public int StrainGaugeCount { get; init; } = 16;
    public double YoungModulusGpa { get; init; } = 70.0;
    public double PoissonRatio { get; init; } = 0.33;
    public double PlateThicknessMm { get; init; } = 15.0;
    public int IntervalMinutes { get; init; } = 5;
    public bool AutoBeamCorrection { get; init; } = true;
}
