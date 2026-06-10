namespace CoSiteInterference.Module.Models;

public record CoSiteInterferenceRequest
{
    public Guid StationId { get; set; }
    public IReadOnlyList<CoSiteAntenna> CoSiteAntennas { get; set; } = Array.Empty<CoSiteAntenna>();
    public double SelfFrequencyStartMhz { get; set; }
    public double SelfFrequencyEndMhz { get; set; }
}
