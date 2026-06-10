namespace PaEfficiencyTracker.Module.Models;

public class PaEfficiencyOptions
{
    public double ThresholdPercent { get; set; } = 40.0;
    public double NominalGainDb { get; set; } = 28.0;
    public double NominalEfficiencyPercent { get; set; } = 45.0;
    public double NominalDcVoltageV { get; set; } = 28.0;
    public int HistoryPoints { get; set; } = 24;
    public int IntervalMinutes { get; set; } = 5;
    public double DecayRateAlarmThreshold { get; set; } = 0.001;
    public double MinimumRemainingHours { get; set; } = 720;
    public double TemperatureDriftThreshold { get; set; } = 5.0;
    public double KalmanFilterAlpha { get; set; } = 0.3;
}
