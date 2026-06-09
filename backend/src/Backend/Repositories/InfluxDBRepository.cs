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

        await writeApi.WritePointsAsync(points, _options.Buckets.Metrics, _options.Org, cancellationToken);
    }

    public async Task<IEnumerable<ChannelMetrics>> GetChannelMetricsAsync(
        string channelId, DateTime startTime, DateTime endTime,
        string aggregation = "raw", CancellationToken cancellationToken = default)
    {
        var query = BuildMetricsQuery(channelId, "channel_id", startTime, endTime, aggregation);
        return await ExecuteMetricsQuery(query, cancellationToken);
    }

    public async Task<IEnumerable<ChannelMetrics>> GetStationMetricsAsync(
        string stationId, DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var query = BuildMetricsQuery(stationId, "station_id", startTime, endTime, "raw");
        return await ExecuteMetricsQuery(query, cancellationToken);
    }

    public async Task<ChannelMetrics?> GetLatestChannelMetricsAsync(
        string channelId, CancellationToken cancellationToken = default)
    {
        var query = $@"
            from(bucket: ""{_options.Buckets.Metrics}"")
                |> range(start: -1h)
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" and r.channel_id == ""{channelId}"")
                |> last()
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                |> limit(n: 1)
        ";

        var metrics = await ExecuteMetricsQuery(query, cancellationToken);
        return metrics.FirstOrDefault();
    }

    public async Task<IEnumerable<ChannelMetrics>> GetLatestStationMetricsAsync(
        string stationId, CancellationToken cancellationToken = default)
    {
        var query = $@"
            from(bucket: ""{_options.Buckets.Metrics}"")
                |> range(start: -10m)
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" and r.station_id == ""{stationId}"")
                |> group(columns: [""channel_id""])
                |> last()
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
        ";

        return await ExecuteMetricsQuery(query, cancellationToken);
    }

    public async Task<double?> GetMetricAverageAsync(
        string channelId, string field,
        DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var query = $@"
            from(bucket: ""{_options.Buckets.Metrics}"")
                |> range(start: {DateTimeToRFC3339(startTime)}, stop: {DateTimeToRFC3339(endTime)})
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" 
                    and r.channel_id == ""{channelId}"" 
                    and r._field == ""{field}"")
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

    private string BuildMetricsQuery(string id, string tagName, DateTime startTime, DateTime endTime, string aggregation)
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

        return $@"
            from(bucket: ""{_options.Buckets.Metrics}"")
                |> range(start: {DateTimeToRFC3339(startTime)}, stop: {DateTimeToRFC3339(endTime)})
                |> filter(fn: (r) => r._measurement == ""channel_metrics"" and r.{tagName} == ""{id}"")
                {aggregationPart}
                |> pivot(rowKey:[""_time""], columnKey: [""_field""], valueColumn: ""_value"")
                |> sort(columns: [""_time""])
        ";
    }

    private async Task<IEnumerable<ChannelMetrics>> ExecuteMetricsQuery(string fluxQuery,
        CancellationToken cancellationToken = default)
    {
        var queryApi = _client.GetQueryApi();
        var tables = await queryApi.QueryAsync(fluxQuery, _options.Org, cancellationToken);

        var metrics = new List<ChannelMetrics>();

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

                if (double.TryParse(record.GetValueByKey("amplitude")?.ToString(), out var amp))
                    metric.Amplitude = amp;
                if (double.TryParse(record.GetValueByKey("phase")?.ToString(), out var phase))
                    metric.Phase = phase;
                if (double.TryParse(record.GetValueByKey("amplitude_deviation")?.ToString(), out var ampDev))
                    metric.AmplitudeDeviation = ampDev;
                if (double.TryParse(record.GetValueByKey("phase_deviation")?.ToString(), out var phaseDev))
                    metric.PhaseDeviation = phaseDev;
                if (double.TryParse(record.GetValueByKey("swr")?.ToString(), out var swr))
                    metric.Swr = swr;
                if (double.TryParse(record.GetValueByKey("pa_temperature")?.ToString(), out var temp))
                    metric.PaTemperature = temp;
                if (double.TryParse(record.GetValueByKey("tx_power")?.ToString(), out var tx))
                    metric.TxPower = tx;
                if (double.TryParse(record.GetValueByKey("rx_power")?.ToString(), out var rx))
                    metric.RxPower = rx;
                if (double.TryParse(record.GetValueByKey("ber")?.ToString(), out var ber))
                    metric.Ber = ber;

                metrics.Add(metric);
            }
        }

        return metrics;
    }

    private static string DateTimeToRFC3339(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }
}
