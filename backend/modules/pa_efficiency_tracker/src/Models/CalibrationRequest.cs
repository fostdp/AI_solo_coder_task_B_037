namespace PaEfficiencyTracker.Module.Models;

public class CalibrationRequest
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double RawTemperature { get; set; }
    public IReadOnlyList<ChannelMetric> ChannelMetrics { get; set; } = Array.Empty<ChannelMetric>();
    public IReadOnlyList<ChannelMetric> AllMetrics { get; set; } = Array.Empty<ChannelMetric>();
    public IReadOnlyList<PaEfficiencyRecord> History { get; set; } = Array.Empty<PaEfficiencyRecord>();
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}

public class CalibrationResult
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double RawTemperature { get; set; }
    public double CalibratedTemperature { get; set; }
    public bool DriftDetected { get; set; }
    public double DriftAmount { get; set; }
    public double KalmanAlpha { get; set; }
    public double KalmanBeta { get; set; }
    public DateTime CalibrationTime { get; set; } = DateTime.UtcNow;
}
