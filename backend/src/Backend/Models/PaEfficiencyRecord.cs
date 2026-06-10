namespace AntennaMonitoring.Models;

public class PaEfficiencyRecord
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double PaTemperature { get; set; }
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
    public string? ReplacementReason { get; set; }
    public double[]? EfficiencyHistory { get; set; }
    public DateTime[]? HistoryTimestamps { get; set; }
    public DateTime MeasurementTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
    public Channel? Channel { get; set; }
}
