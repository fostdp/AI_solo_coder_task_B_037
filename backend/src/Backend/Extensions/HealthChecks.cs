using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using AntennaMonitoring.Repositories;

namespace AntennaMonitoring.Extensions;

public class InfluxDBHealthCheck : IHealthCheck
{
    private readonly IInfluxDBRepository _influxDBRepository;
    private readonly ILogger<InfluxDBHealthCheck> _logger;

    public InfluxDBHealthCheck(
        IInfluxDBRepository influxDBRepository,
        ILogger<InfluxDBHealthCheck> logger)
    {
        _influxDBRepository = influxDBRepository;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _influxDBRepository.CheckHealthAsync(cancellationToken);

            if (isHealthy)
            {
                return HealthCheckResult.Healthy("InfluxDB connection is healthy");
            }

            return HealthCheckResult.Unhealthy("InfluxDB connection failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InfluxDB health check failed");
            return HealthCheckResult.Unhealthy("InfluxDB health check exception", ex);
        }
    }
}

public static class AppMetrics
{
    public static readonly Counter EcpriPacketsReceived = Metrics.CreateCounter(
        "ecpri_packets_received_total",
        "Total number of eCPRI packets received",
        new CounterConfiguration
        {
            LabelNames = new[] { "protocol", "station_code" }
        });

    public static readonly Counter EcpriPacketsFailed = Metrics.CreateCounter(
        "ecpri_packets_failed_total",
        "Total number of failed eCPRI packets",
        new CounterConfiguration
        {
            LabelNames = new[] { "protocol", "error_type" }
        });

    public static readonly Histogram CalibrationDuration = Metrics.CreateHistogram(
        "calibration_duration_seconds",
        "Calibration execution duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "algorithm", "station_id" },
            Buckets = Histogram.LinearBuckets(0.1, 0.5, 10)
        });

    public static readonly Histogram DiagnosisDuration = Metrics.CreateHistogram(
        "diagnosis_duration_seconds",
        "Diagnosis execution duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "model", "station_id" },
            Buckets = Histogram.LinearBuckets(0.1, 0.5, 10)
        });

    public static readonly Gauge ActiveAlarms = Metrics.CreateGauge(
        "alarms_active_total",
        "Current number of active alarms",
        new GaugeConfiguration
        {
            LabelNames = new[] { "severity", "level" }
        });

    public static readonly Gauge ChannelsHealth = Metrics.CreateGauge(
        "channels_health_ratio",
        "Ratio of healthy channels per station",
        new GaugeConfiguration
        {
            LabelNames = new[] { "station_id", "status" }
        });

    public static readonly Counter AlarmsTriggered = Metrics.CreateCounter(
        "alarms_triggered_total",
        "Total number of alarms triggered",
        new CounterConfiguration
        {
            LabelNames = new[] { "alarm_code", "severity" }
        });

    public static readonly Histogram EcpriProcessingLatency = Metrics.CreateHistogram(
        "ecpri_processing_latency_ms",
        "eCPRI packet processing latency in milliseconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.LinearBuckets(1, 5, 10)
        });

    public static readonly Gauge SllBefore = Metrics.CreateGauge(
        "calibration_sll_before_db",
        "SLL before calibration in dB",
        new GaugeConfiguration
        {
            LabelNames = new[] { "station_id" }
        });

    public static readonly Gauge SllAfter = Metrics.CreateGauge(
        "calibration_sll_after_db",
        "SLL after calibration in dB",
        new GaugeConfiguration
        {
            LabelNames = new[] { "station_id" }
        });

    public static readonly Gauge AverageFailureProbability = Metrics.CreateGauge(
        "diagnosis_avg_failure_probability",
        "Average channel failure probability per station",
        new GaugeConfiguration
        {
            LabelNames = new[] { "station_id" }
        });
}
