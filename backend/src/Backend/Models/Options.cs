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
