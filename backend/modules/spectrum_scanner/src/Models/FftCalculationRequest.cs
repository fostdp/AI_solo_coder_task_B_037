using System.Numerics;

namespace SpectrumScanner.Module.Models;

public class FftCalculationRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public double[] TimeDomainData { get; set; } = Array.Empty<double>();
    public int FftSize { get; set; }
    public bool ApplyWindow { get; set; } = true;
    public WindowType WindowType { get; set; } = WindowType.Hanning;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public TaskCompletionSource<FftCalculationResult> CompletionSource { get; set; } = new();
}

public class FftCalculationResult
{
    public Guid RequestId { get; set; }
    public double[] FrequencySpectrum { get; set; } = Array.Empty<double>();
    public Complex[] ComplexSpectrum { get; set; } = Array.Empty<Complex>();
    public bool GpuAccelerated { get; set; }
    public TimeSpan CalculationTime { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public enum WindowType
{
    Rectangular,
    Hanning,
    Hamming,
    Blackman,
    Kaiser
}
