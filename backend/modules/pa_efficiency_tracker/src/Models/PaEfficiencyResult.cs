namespace PaEfficiencyTracker.Module.Models;

public record PaEfficiencyResult
{
    public Guid StationId { get; set; }
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double PaTemperature { get; set; }
    public double RawPaTemperature { get; set; }
    public bool TemperatureDriftDetected { get; set; }
    public double TemperatureDriftAmount { get; set; }
    public double OutputPowerDbm { get; set; }
    public double InputPowerDbm { get; set; }
    public double GainDb { get; set; }
    public double EfficiencyPercent { get; set; }
    public double PowerAddedEfficiencyPercent { get; set; }
    public double DcCurrentA { get; set; }
    public double DcVoltageV { get; set; }
    public double DcPowerW { get; set; }
    public double RfPowerW { get; set; }
    public double EfficiencyDecayRate { get; set; }
    public double PredictedRemainingHours { get; set; }
    public bool NeedsReplacement { get; set; }
    public string ReplacementReason { get; set; } = string.Empty;
    public double[] EfficiencyHistory { get; set; } = Array.Empty<double>();
    public DateTime[] HistoryTimestamps { get; set; } = Array.Empty<DateTime>();
}
