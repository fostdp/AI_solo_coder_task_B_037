namespace AntennaMonitoring.Messages;

public record SensorData
{
    public Guid StationId { get; init; }
    public int SensorIndex { get; init; }
    public string SensorType { get; init; } = string.Empty;
    public double TiltAngleX { get; init; }
    public double TiltAngleY { get; init; }
    public double TiltAngleZ { get; init; }
    public double StrainValue { get; init; }
    public double Temperature { get; init; }
    public double WindSpeed { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsAnomaly { get; init; }
    public bool IsInterpolated { get; init; }
}

public record DeformationRequest
{
    public Guid StationId { get; set; }
    public IReadOnlyList<SensorData> SensorData { get; set; } = Array.Empty<SensorData>();
    public IReadOnlyList<AntennaMonitoring.Models.Channel> Channels { get; set; } = Array.Empty<AntennaMonitoring.Models.Channel>();
}

public record DeformationResult
{
    public Guid StationId { get; set; }
    public int SensorIndex { get; set; }
    public double CalculatedDisplacementMm { get; set; }
    public double StressMpa { get; set; }
    public string DeformationZone { get; set; } = string.Empty;
    public bool ExceedsThreshold { get; set; }
    public double CorrectionAngleAzimuth { get; set; }
    public double CorrectionAngleElevation { get; set; }
    public bool CorrectionApplied { get; set; }
    public double TiltAngleX { get; set; }
    public double TiltAngleY { get; set; }
    public double TiltAngleZ { get; set; }
    public double StrainValue { get; set; }
    public bool IsInterpolated { get; set; }
    public bool IsAnomaly { get; set; }
}

public record CoSiteAntenna
{
    public Guid Id { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string AntennaType { get; set; } = string.Empty;
    public double FrequencyStartMhz { get; set; }
    public double FrequencyEndMhz { get; set; }
    public double TransmitPowerDbm { get; set; }
    public double SeparationDistanceMeters { get; set; }
    public double AzimuthAngleDeg { get; set; }
    public double ElevationAngleDeg { get; set; }
    public double HeightOffsetMeters { get; set; }
    public bool IsApproximated { get; set; }
}

public record CoSiteInterferenceRequest
{
    public Guid StationId { get; set; }
    public IReadOnlyList<CoSiteAntenna> CoSiteAntennas { get; set; } = Array.Empty<CoSiteAntenna>();
    public double SelfFrequencyStartMhz { get; set; }
    public double SelfFrequencyEndMhz { get; set; }
}

public record CoSiteInterferenceResult
{
    public Guid StationId { get; set; }
    public Guid InterferingAntennaId { get; set; }
    public string InterferingOperator { get; set; } = string.Empty;
    public double IsolationDb { get; set; }
    public double CouplingCoefficient { get; set; }
    public double InterferenceMarginDb { get; set; }
    public bool IsIsolationSufficient { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public double VectorX { get; set; }
    public double VectorY { get; set; }
    public double VectorZ { get; set; }
    public bool IsApproximated { get; set; }
}

public record ChannelMetric
{
    public string ChannelId { get; set; } = string.Empty;
    public int ChannelIndex { get; set; }
    public double TxPower { get; set; }
    public double PaTemperature { get; set; }
    public double Amplitude { get; set; }
    public double Phase { get; set; }
    public double Swr { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public record PaEfficiencyRequest
{
    public Guid StationId { get; set; }
    public IReadOnlyList<AntennaMonitoring.Models.Channel> Channels { get; set; } = Array.Empty<AntennaMonitoring.Models.Channel>();
    public IReadOnlyList<ChannelMetric> RecentMetrics { get; set; } = Array.Empty<ChannelMetric>();
}

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

public record SpectrumScanRequest
{
    public Guid StationId { get; set; }
    public double StartFrequencyMhz { get; set; }
    public double EndFrequencyMhz { get; set; }
    public double ResolutionBandwidthKhz { get; set; }
    public IReadOnlyList<AntennaMonitoring.Models.Channel> Channels { get; set; } = Array.Empty<AntennaMonitoring.Models.Channel>();
}

public record SpectrumScanResult
{
    public Guid StationId { get; set; }
    public double[] FrequencyPointsMhz { get; set; } = Array.Empty<double>();
    public double[] PowerLevelsDbm { get; set; } = Array.Empty<double>();
    public int InterferenceCount { get; set; }
    public string InterferenceDetails { get; set; } = string.Empty;
    public double[] InterferenceFrequenciesMhz { get; set; } = Array.Empty<double>();
    public double[] InterferencePowersDbm { get; set; } = Array.Empty<double>();
    public double[] InterferenceDirectionsDeg { get; set; } = Array.Empty<double>();
    public bool NullSteeringApplied { get; set; }
    public double[] NullAnglesDeg { get; set; } = Array.Empty<double>();
    public double[] NullDepthsDb { get; set; } = Array.Empty<double>();
    public double NoiseFloorDbm { get; set; }
    public double SpuriousFreeDynamicRangeDb { get; set; }
}
