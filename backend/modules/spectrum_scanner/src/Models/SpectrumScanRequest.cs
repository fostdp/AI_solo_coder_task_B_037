namespace SpectrumScanner.Module.Models;

public record SpectrumScanRequest
{
    public Guid StationId { get; set; }
    public double StartFrequencyMhz { get; set; }
    public double EndFrequencyMhz { get; set; }
    public double ResolutionBandwidthKhz { get; set; }
    public IReadOnlyList<Channel> Channels { get; set; } = Array.Empty<Channel>();
}
