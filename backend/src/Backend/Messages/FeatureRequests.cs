namespace AntennaMonitoring.Messages;

public record DeformationMonitoredRequest
{
    public Guid StationId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public record CoSiteInterferenceAnalysisRequest
{
    public Guid StationId { get; set; }
}

public record PaEfficiencyEvaluationRequest
{
    public Guid StationId { get; set; }
    public Guid? ChannelId { get; set; }
}

public record SpectrumScanTriggerRequest
{
    public Guid StationId { get; set; }
    public double? StartFrequencyMhz { get; set; }
    public double? EndFrequencyMhz { get; set; }
}

public record BeamCorrectionRequest
{
    public Guid StationId { get; set; }
    public double CorrectionAzimuthDeg { get; set; }
    public double CorrectionElevationDeg { get; set; }
}

public record NullSteeringRequest
{
    public Guid StationId { get; set; }
    public double[] InterferenceDirectionsDeg { get; set; } = Array.Empty<double>();
}
