using AntennaMonitoring.Models;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Repositories;

public class InfluxDBRepository : IInfluxDBRepository
{
    private readonly IInfluxDBClient _client;
    private readonly InfluxDBOptions _options;

    public InfluxDBRepository(IOptions<InfluxDBOptions> options)
    {
        _options = options.Value;
        _client = InfluxDBClientFactory.Create(_options.Url, _options.Token);
    }

    public async Task WriteChannelMetricsAsync(IEnumerable<ChannelMetrics> metrics,
        CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var points = new List<PointData>();
        foreach (var metric in metrics)
        {
            var point = PointData.Measurement("channel_metrics")
                .Tag("station_id", metric.StationId)
                .Tag("channel_id", metric.ChannelId)
                .Tag("station_code", metric.StationCode)
                .Tag("channel_index", metric.ChannelIndex.ToString())
                .Tag("row_index", metric.RowIndex.ToString())
                .Tag("column_index", metric.ColumnIndex.ToString())
                .Tag("data_source", metric.DataSource)
                .Field("amplitude", metric.Amplitude)
                .Field("phase", metric.Phase)
                .Field("amplitude_deviation", metric.AmplitudeDeviation)
                .Field("phase_deviation", metric.PhaseDeviation)
                .Field("swr", metric.Swr)
                .Field("pa_temperature", metric.PaTemperature)
                .Field("tx_power", metric.TxPower)
                .Field("rx_power", metric.RxPower)
                .Field("ber", metric.Ber)
                .Timestamp(metric.Timestamp, WritePrecision.Ns);

            points.Add(point);
        }

        await writeApi.WritePointsAsync(points, _options.Buckets.MetricsRaw, _options.Org, cancellationToken);
    }

    private string SelectBucketByTimeRange(DateTime startTime, DateTime endTime)
    {
        var timeSpan = endTime - startTime;
        
        if (timeSpan.TotalDays <= 7)
        {
            return _options.Buckets.MetricsRaw;
        }
        else if (timeSpan.TotalDays <= 30)
        {
            return _options.Buckets.Metrics1h;
        }
        else
        {
            return _options.Buckets.Metrics24h;
        }
    }

    private (string Bucket, string FieldSuffix) SelectBucketAndSuffix(DateTime startTime, DateTime endTime, string aggregation)
    {
        var timeSpan = endTime - startTime;
        
        if (aggregation == "raw" && timeSpan.TotalDays <= 7)
        {
            return (_options.Buckets.MetricsRaw, "");
        }
        else if (timeSpan.TotalDays <= 30)
        {
            return (_options.Buckets.Metrics1h, "_mean");
        }
        else
        {
            return (_options.Buckets.Metrics24h, "_mean");
        }
    }

    public async Task<IEnumerable<ChannelMetrics>> GetChannelMetricsAsync(
        string channelId, DateTime startTime, DateTime endTime,
        string aggregation = "raw", CancellationToken cancellationToken = default)
    {
        var (bucket, fieldSuffix) = SelectBucketAndSuffix(startTime, endTime, aggregation);
        var query = BuildMetricsQuery(channelId, "channel_id", startTime, endTime, aggregation, bucket, fieldSuffix);
        return await ExecuteMetricsQuery(query, bucket, fieldSuffix, cancellationToken);
    }

    public async Task<IEnumerable<ChannelMetrics>> GetStationMetricsAsync(
        string stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var (bucket, fieldSuffix) = SelectBucketAndSuffix(startTime, endTime, "raw");
        var query = BuildMetricsQuery(stationId, "station_id", startTime, endTime, "raw", bucket, fieldSuffix);
        return await ExecuteMetricsQuery(query, bucket, fieldSuffix, cancellationToken);
    }

    public async Task<ChannelMetrics?> GetLatestChannelMetricsAsync(
        string channelId, CancellationToken cancellationToken = default)
    {
        var query = $@"
            from(bucket: ""{_options.Buckets.MetricsRaw}"")
                |> range(start: -1h)
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" and r.channel_id == ""{channelId}"")
                |> last()
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                |> limit(n: 1)
        ";

        var metrics = await ExecuteMetricsQuery(query, _options.Buckets.MetricsRaw, "", cancellationToken);
        return metrics.FirstOrDefault();
    }

    public async Task<IEnumerable<ChannelMetrics>> GetLatestStationMetricsAsync(
        string stationId, CancellationToken cancellationToken = default)
    {
        var query = $@"
            from(bucket: ""{_options.Buckets.MetricsRaw}"")
                |> range(start: -10m)
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" and r.station_id == ""{stationId}"")
                |> group(columns: [""channel_id""])
                |> last()
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
        ";

        return await ExecuteMetricsQuery(query, _options.Buckets.MetricsRaw, "", cancellationToken);
    }

    public async Task<double?> GetMetricAverageAsync(
        string channelId, string field,
        DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var bucket = SelectBucketByTimeRange(startTime, endTime);
        var (_, fieldSuffix) = SelectBucketAndSuffix(startTime, endTime, "raw");
        
        var query = $@"
            from(bucket: ""{bucket}"")
                |> range(start: {DateTimeToRFC3339(startTime)}, stop: {DateTimeToRFC3339(endTime)})
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" 
                    and r.channel_id == ""{channelId}"" 
                    and r._field == ""{field}{fieldSuffix}"")
                |> mean()
        ";

        var queryApi = _client.GetQueryApi();
        var tables = await queryApi.QueryAsync(query, _options.Org, cancellationToken);

        return tables.SelectMany(t => t.Records)
            .Select(r => r.GetValue() as double?)
            .FirstOrDefault();
    }

    public async Task WriteBeamformingMetricsAsync(
        Guid stationId, string algorithm,
        double sll, double sllBefore, double sllAfter,
        double beamwidthAz, double beamwidthEl,
        bool converged, CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var point = PointData.Measurement("beamforming_metrics")
            .Tag("station_id", stationId.ToString())
            .Tag("algorithm", algorithm)
            .Field("sll", sll)
            .Field("sll_before", sllBefore)
            .Field("sll_after", sllAfter)
            .Field("beamwidth_azimuth", beamwidthAz)
            .Field("beamwidth_elevation", beamwidthEl)
            .Field("pointing_error_az", 0.0)
            .Field("pointing_error_el", 0.0)
            .Field("calibration_converged", converged)
            .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

        await writeApi.WritePointAsync(point, _options.Buckets.Calibration, _options.Org, cancellationToken);
    }

    public async Task WriteDiagnosisMetricsAsync(
        Guid stationId, Guid channelId, string modelType,
        double failureProbability, double swrPredicted,
        double temperaturePredicted, double anomalyScore,
        int predictedFailureHours, double healthScore,
        CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var point = PointData.Measurement("diagnosis_metrics")
            .Tag("station_id", stationId.ToString())
            .Tag("channel_id", channelId.ToString())
            .Tag("model_type", modelType)
            .Field("failure_probability", failureProbability)
            .Field("swr_predicted", swrPredicted)
            .Field("temperature_predicted", temperaturePredicted)
            .Field("anomaly_score", anomalyScore)
            .Field("predicted_failure_hours", predictedFailureHours)
            .Field("health_score", healthScore)
            .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

        await writeApi.WritePointAsync(point, _options.Buckets.Diagnosis, _options.Org, cancellationToken);
    }

    public async Task<IEnumerable<double[]>> GetBeamPatternHistoryAsync(
        Guid stationId, int limit = 10, CancellationToken cancellationToken = default)
    {
        var query = $@"
            from(bucket: ""{_options.Buckets.Calibration}"")
                |> range(start: -30d)
                |> filter(fn: (r) => r._measurement == ""beamforming_metrics"" 
                    and r.station_id == ""{stationId}""
                    and r._field == ""sll_after"")
                |> sort(columns: [""_time""], desc: true)
                |> limit(n: {limit})
        ";

        var queryApi = _client.GetQueryApi();
        var tables = await queryApi.QueryAsync(query, _options.Org, cancellationToken);

        var results = new List<double[]>();
        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                results.Add(new[]
                {
                    record.GetTime().GetValueOrDefault().ToUnixTimeMilliseconds(),
                    Convert.ToDouble(record.GetValue())
                });
            }
        }

        return results;
    }

    private string BuildMetricsQuery(string id, string tagName, DateTime startTime, DateTime endTime, 
        string aggregation, string bucket, string fieldSuffix)
    {
        string aggregateWindow = aggregation switch
        {
            "1h" => "1h",
            "24h" => "24h",
            _ => ""
        };

        string aggregationPart = string.IsNullOrEmpty(aggregateWindow)
            ? ""
            : $"|> aggregateWindow(every: {aggregateWindow}, fn: mean, createEmpty: false)";

        var fields = new[] { "amplitude", "phase", "amplitude_deviation", "phase_deviation", "swr", "pa_temperature", "tx_power", "rx_power", "ber" };
        var fieldFilters = string.Join(" or ", fields.Select(f => $"r._field == \"{f}{fieldSuffix}\""));

        return $@"
            from(bucket: ""{bucket}"")
                |> range(start: {DateTimeToRFC3339(startTime)}, stop: {DateTimeToRFC3339(endTime)})
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" and r.{tagName} == ""{id}"")
                |> filter(fn: (r) => {fieldFilters})
                {aggregationPart}
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                |> sort(columns: [""_time""])
        ";
    }

    private async Task<IEnumerable<ChannelMetrics>> ExecuteMetricsQuery(string fluxQuery, string bucket, string fieldSuffix,
        CancellationToken cancellationToken = default)
    {
        var queryApi = _client.GetQueryApi();
        var tables = await queryApi.QueryAsync(fluxQuery, _options.Org, cancellationToken);

        var metrics = new List<ChannelMetrics>();
        var fields = new[] { "amplitude", "phase", "amplitude_deviation", "phase_deviation", "swr", "pa_temperature", "tx_power", "rx_power", "ber" };

        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                var metric = new ChannelMetrics
                {
                    StationId = record.GetValueByKey("station_id")?.ToString() ?? "",
                    ChannelId = record.GetValueByKey("channel_id")?.ToString() ?? "",
                    StationCode = record.GetValueByKey("station_code")?.ToString() ?? "",
                    ChannelIndex = int.Parse(record.GetValueByKey("channel_index")?.ToString() ?? "0"),
                    RowIndex = int.Parse(record.GetValueByKey("row_index")?.ToString() ?? "0"),
                    ColumnIndex = int.Parse(record.GetValueByKey("column_index")?.ToString() ?? "0"),
                    DataSource = record.GetValueByKey("data_source")?.ToString() ?? "ecpri",
                    Timestamp = record.GetTime().GetValueOrDefault().ToDateTimeUtc()
                };

                foreach (var field in fields)
                {
                    var fieldName = field + fieldSuffix;
                    if (double.TryParse(record.GetValueByKey(fieldName)?.ToString(), out var value))
                    {
                        switch (field)
                        {
                            case "amplitude": metric.Amplitude = value; break;
                            case "phase": metric.Phase = value; break;
                            case "amplitude_deviation": metric.AmplitudeDeviation = value; break;
                            case "phase_deviation": metric.PhaseDeviation = value; break;
                            case "swr": metric.Swr = value; break;
                            case "pa_temperature": metric.PaTemperature = value; break;
                            case "tx_power": metric.TxPower = value; break;
                            case "rx_power": metric.RxPower = value; break;
                            case "ber": metric.Ber = value; break;
                        }
                    }
                }

                metrics.Add(metric);
            }
        }

        return metrics;
    }

    private static string DateTimeToRFC3339(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ping = await _client.PingAsync(cancellationToken);
            return ping;
        }
        catch
        {
            return false;
        }
    }

    public async Task WriteSensorMetricAsync(string stationId, SensorData sensorData,
        CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var point = PointData.Measurement("sensor_metrics")
            .Tag("station_id", stationId)
            .Tag("sensor_type", sensorData.SensorType)
            .Tag("sensor_index", sensorData.SensorIndex.ToString())
            .Field("tilt_x", sensorData.TiltAngleX)
            .Field("tilt_y", sensorData.TiltAngleY)
            .Field("tilt_z", sensorData.TiltAngleZ)
            .Field("tilt_magnitude", sensorData.TiltMagnitude)
            .Field("strain", sensorData.StrainValue)
            .Field("temperature", sensorData.Temperature)
            .Field("wind_speed", sensorData.WindSpeed)
            .Timestamp(sensorData.Timestamp, WritePrecision.Ns);

        await writeApi.WritePointAsync(point, _options.Buckets.MetricsRaw, _options.Org, cancellationToken);
    }

    public async Task<IEnumerable<SensorMetric>> GetSensorMetricsAsync(
        string stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var bucket = SelectBucketByTimeRange(startTime, endTime);
        var query = $@"
            from(bucket: ""{bucket}"")
                |> range(start: {DateTimeToRFC3339(startTime)}, stop: {DateTimeToRFC3339(endTime)})
                |> filter(fn: (r) => r._measurement == ""sensor_metrics"" and r.station_id == ""{stationId}"")
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                |> sort(columns: [""_time""])
        ";

        var queryApi = _client.GetQueryApi();
        var tables = await queryApi.QueryAsync(query, _options.Org, cancellationToken);

        var metrics = new List<SensorMetric>();
        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                var metric = new SensorMetric
                {
                    StationId = record.GetValueByKey("station_id")?.ToString() ?? "",
                    SensorType = record.GetValueByKey("sensor_type")?.ToString() ?? "",
                    SensorIndex = int.Parse(record.GetValueByKey("sensor_index")?.ToString() ?? "0"),
                    Timestamp = record.GetTime().GetValueOrDefault().ToDateTimeUtc()
                };

                double.TryParse(record.GetValueByKey("tilt_x")?.ToString(), out var tiltX);
                double.TryParse(record.GetValueByKey("tilt_y")?.ToString(), out var tiltY);
                double.TryParse(record.GetValueByKey("tilt_z")?.ToString(), out var tiltZ);
                double.TryParse(record.GetValueByKey("strain")?.ToString(), out var strain);
                double.TryParse(record.GetValueByKey("temperature")?.ToString(), out var temp);
                double.TryParse(record.GetValueByKey("wind_speed")?.ToString(), out var wind);

                metric.TiltAngleX = tiltX;
                metric.TiltAngleY = tiltY;
                metric.TiltAngleZ = tiltZ;
                metric.StrainValue = strain;
                metric.Temperature = temp;
                metric.WindSpeed = wind;

                metrics.Add(metric);
            }
        }

        return metrics;
    }

    public async Task WriteInterferenceMetricAsync(string stationId, CoSiteInterferenceResult result,
        CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var point = PointData.Measurement("interference_metrics")
            .Tag("station_id", stationId)
            .Tag("interfering_operator", result.InterferingOperator ?? "unknown")
            .Tag("isolation_sufficient", result.IsIsolationSufficient.ToString())
            .Field("isolation_db", result.IsolationDb)
            .Field("coupling_coefficient", result.CouplingCoefficient)
            .Field("interference_vector_x", result.InterferenceVectorX)
            .Field("interference_vector_y", result.InterferenceVectorY)
            .Field("interference_vector_z", result.InterferenceVectorZ)
            .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

        await writeApi.WritePointAsync(point, _options.Buckets.MetricsRaw, _options.Org, cancellationToken);
    }

    public async Task WriteEfficiencyMetricAsync(string stationId, PaEfficiencyResult result,
        CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var point = PointData.Measurement("pa_efficiency_metrics")
            .Tag("station_id", stationId)
            .Tag("channel_id", result.ChannelId.ToString())
            .Tag("needs_replacement", result.NeedsReplacement.ToString())
            .Field("efficiency_percent", result.EfficiencyPercent)
            .Field("decay_rate", result.EfficiencyDecayRate)
            .Field("predicted_remaining_hours", result.PredictedRemainingHours)
            .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

        await writeApi.WritePointAsync(point, _options.Buckets.MetricsRaw, _options.Org, cancellationToken);
    }

    public async Task WriteSpectrumMetricAsync(string stationId, SpectrumScanResult result,
        CancellationToken cancellationToken = default)
    {
        using var writeApi = _client.GetWriteApiAsync();

        var points = new List<PointData>();
        for (int i = 0; i < result.FrequencyPointsMhz.Length; i++)
        {
            var point = PointData.Measurement("spectrum_metrics")
                .Tag("station_id", stationId)
                .Tag("scan_index", i.ToString())
                .Field("frequency_mhz", result.FrequencyPointsMhz[i])
                .Field("power_dbm", result.PowerLevelsDbm[i])
                .Timestamp(DateTime.UtcNow, WritePrecision.Ns);
            points.Add(point);
        }

        if (result.InterferenceFrequenciesMhz != null && result.InterferenceFrequenciesMhz.Length > 0)
        {
            for (int i = 0; i < result.InterferenceFrequenciesMhz.Length; i++)
            {
                var direction = result.InterferenceDirectionsDeg != null && i < result.InterferenceDirectionsDeg.Length
                    ? result.InterferenceDirectionsDeg[i] : 0;
                var nullAngle = result.NullAnglesDeg != null && i < result.NullAnglesDeg.Length
                    ? result.NullAnglesDeg[i] : 0;
                var nullDepth = result.NullDepthsDb != null && i < result.NullDepthsDb.Length
                    ? result.NullDepthsDb[i] : 0;

                var point = PointData.Measurement("interference_detection")
                    .Tag("station_id", stationId)
                    .Tag("interference_index", i.ToString())
                    .Field("frequency_mhz", result.InterferenceFrequenciesMhz[i])
                    .Field("direction_deg", direction)
                    .Field("null_angle_deg", nullAngle)
                    .Field("null_depth_db", nullDepth)
                    .Timestamp(DateTime.UtcNow, WritePrecision.Ns);
                points.Add(point);
            }
        }

        await writeApi.WritePointsAsync(points, _options.Buckets.MetricsRaw, _options.Org, cancellationToken);
    }
}
