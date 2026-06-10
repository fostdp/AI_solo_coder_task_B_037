namespace AntennaMonitoring.Models;

public class InfluxDBOptions
{
    public string Url { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Org { get; set; } = string.Empty;
    public BucketOptions Buckets { get; set; } = new();
}

public class BucketOptions
{
    public string MetricsRaw { get; set; } = "antenna_metrics_raw";
    public string Metrics1h { get; set; } = "antenna_metrics_1h";
    public string Metrics24h { get; set; } = "antenna_metrics_24h";
    public string Calibration { get; set; } = "antenna_calibration";
    public string Diagnosis { get; set; } = "antenna_diagnosis";
}

public class MQTTOptions
{
    public string Broker { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = "antenna-monitoring-service";
    public TopicOptions Topics { get; set; } = new();
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class TopicOptions
{
    public string Alarm { get; set; } = "5g/antenna/alarm";
    public string Calibration { get; set; } = "5g/antenna/calibration";
    public string ECPRI { get; set; } = "5g/antenna/ecpri/+";
}

public class ECPRIOptions
{
    public int ListenPort { get; set; } = 50000;
    public int BufferSize { get; set; } = 65536;
    public int MaxConnections { get; set; } = 200;
}

public class CalibrationOptions
{
    public string Algorithm { get; set; } = "Kalman";
    public double RequiredSLL { get; set; } = -20.0;
    public int MaxIterations { get; set; } = 100;
    public double ConvergenceThreshold { get; set; } = 1e-6;
    public int IntervalMinutes { get; set; } = 5;
}

public class DiagnosisOptions
{
    public string ModelType { get; set; } = "RandomForest";
    public double FailureProbabilityThreshold { get; set; } = 0.7;
    public double SWRAlarmThreshold { get; set; } = 2.0;
    public double SectorFailureChannelRatio { get; set; } = 0.1;
    public int IntervalMinutes { get; set; } = 5;
    public int PredictionHorizonHours { get; set; } = 24;
}

public class DeformationOptions
{
    public double ThresholdMm { get; set; } = 0.5;
    public int MemSensorCount { get; set; } = 9;
    public int StrainGaugeCount { get; set; } = 16;
    public double YoungModulusGpa { get; set; } = 70.0;
    public double PoissonRatio { get; set; } = 0.33;
    public double PlateThicknessMm { get; set; } = 15.0;
    public int IntervalMinutes { get; set; } = 5;
    public bool AutoBeamCorrection { get; set; } = true;
}

public class CoSiteInterferenceOptions
{
    public double IsolationThresholdDb { get; set; } = 30.0;
    public int IntervalMinutes { get; set; } = 10;
    public double FrequencyOverlapThreshold { get; set; } = 0.1;
    public double CouplingModelAccuracy { get; set; } = 0.85;
    public double FastCalculationDistanceThresholdMeters { get; set; } = 100.0;
}

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

public class SpectrumScanOptions
{
    public double StartFrequencyMhz { get; set; } = 3400.0;
    public double EndFrequencyMhz { get; set; } = 3600.0;
    public double ResolutionBandwidthKhz { get; set; } = 100.0;
    public double InterferencePowerThresholdDbm { get; set; } = -80.0;
    public double NullDepthTargetDb { get; set; } = 25.0;
    public int MaxNullCount { get; set; } = 3;
    public int IntervalMinutes { get; set; } = 15;
    public bool AutoNullSteering { get; set; } = true;
    public double DoaEstimationAccuracy { get; set; } = 0.9;
    public double WidebandThresholdMhz { get; set; } = 5.0;
    public double SubbandWidthMhz { get; set; } = 2.0;
    public int MaxSubbands { get; set; } = 8;
    public double DiagonalLoadingLevel { get; set; } = 0.1;
}
