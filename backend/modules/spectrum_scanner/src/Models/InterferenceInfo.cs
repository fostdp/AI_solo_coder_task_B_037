namespace SpectrumScanner.Module.Models;

public class InterferenceInfo
{
    public double Frequency { get; set; }
    public double Power { get; set; }
    public double Bandwidth { get; set; }
    public double StartFrequency { get; set; }
    public double EndFrequency { get; set; }
    public bool IsWideband { get; set; }
    public double SpectralFlatness { get; set; }
}
