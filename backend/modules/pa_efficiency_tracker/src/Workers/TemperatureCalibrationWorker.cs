using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module.Workers;

public class TemperatureCalibrationWorker : BackgroundService
{
    private readonly ILogger<TemperatureCalibrationWorker> _logger;
    private readonly PaEfficiencyOptions _options;
    private readonly Channel<CalibrationRequest> _requestChannel;
    private readonly ConcurrentDictionary<Guid, List<CalibrationResult>> _calibrationHistory;
    private readonly ConcurrentDictionary<Guid, (double DriftTrend, int SampleCount)> _driftTrends;
    private readonly ConcurrentDictionary<int, Task> _channelWorkers;
    private readonly int _maxDegreeOfParallelism;

    public ChannelReader<CalibrationRequest> RequestReader => _requestChannel.Reader;
    public ChannelWriter<CalibrationRequest> RequestWriter => _requestChannel.Writer;

    public TemperatureCalibrationWorker(
        ILogger<TemperatureCalibrationWorker> logger,
        IOptions<PaEfficiencyOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _requestChannel = Channel.CreateUnbounded<CalibrationRequest>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = false
        });
        _calibrationHistory = new ConcurrentDictionary<Guid, List<CalibrationResult>>();
        _driftTrends = new ConcurrentDictionary<Guid, (double, int)>();
        _channelWorkers = new ConcurrentDictionary<int, Task>();
        _maxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Temperature Calibration Worker started. Max parallelism: {MaxParallelism}, " +
            "Drift threshold: {DriftThreshold}°C, Kalman alpha: {KalmanAlpha}",
            _maxDegreeOfParallelism,
            _options.TemperatureDriftThreshold,
            _options.KalmanFilterAlpha);

        var processingTasks = new List<Task>();
        for (int i = 0; i < _maxDegreeOfParallelism; i++)
        {
            var workerId = i;
            var workerTask = ProcessCalibrationQueueAsync(workerId, stoppingToken);
            _channelWorkers.TryAdd(workerId, workerTask);
            processingTasks.Add(workerTask);
        }

        var maintenanceTask = RunMaintenanceTaskAsync(stoppingToken);

        await Task.WhenAll(processingTasks.Concat(new[] { maintenanceTask }));
    }

    private async Task ProcessCalibrationQueueAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Calibration worker {WorkerId} started", workerId);

        await foreach (var request in _requestChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = await ProcessCalibrationRequestAsync(request, stoppingToken);
                UpdateCalibrationHistory(result);
                UpdateDriftTrend(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing calibration request for channel {ChannelId}",
                    request.ChannelId);
            }
        }

        _logger.LogDebug("Calibration worker {WorkerId} stopped", workerId);
    }

    private async Task<CalibrationResult> ProcessCalibrationRequestAsync(
        CalibrationRequest request,
        CancellationToken stoppingToken)
    {
        return await Task.Run(() =>
        {
            var channelMetrics = request.ChannelMetrics.ToList();
            var allMetrics = request.AllMetrics.ToList();
            var history = request.History.ToList();

            var calibratedTemperature = PerformKalmanCalibration(
                request.RawTemperature,
                channelMetrics,
                allMetrics,
                history,
                out var driftDetected,
                out var driftAmount);

            return new CalibrationResult
            {
                ChannelId = request.ChannelId,
                ChannelIndex = request.ChannelIndex,
                RawTemperature = request.RawTemperature,
                CalibratedTemperature = Math.Round(calibratedTemperature, 4),
                DriftDetected = driftDetected,
                DriftAmount = Math.Round(driftAmount, 4),
                KalmanAlpha = _options.KalmanFilterAlpha,
                KalmanBeta = 1.0 - _options.KalmanFilterAlpha
            };
        }, stoppingToken);
    }

    private double PerformKalmanCalibration(
        double rawTemperature,
        List<ChannelMetric> channelMetrics,
        List<ChannelMetric> allMetrics,
        List<PaEfficiencyRecord> history,
        out bool driftDetected,
        out double driftAmount)
    {
        driftDetected = false;
        driftAmount = 0;

        if (channelMetrics.Count < 3)
        {
            return rawTemperature;
        }

        double referenceTemperature = 0;
        int referenceCount = 0;

        if (history.Count >= 5)
        {
            var recentHistory = history
                .OrderByDescending(h => h.Timestamp)
                .Take(10)
                .ToList();

            referenceTemperature += recentHistory.Average(h => h.PaTemperature);
            referenceCount++;
        }

        var otherChannelTemperatures = allMetrics
            .Where(m => m.ChannelIndex != channelMetrics.First().ChannelIndex)
            .Select(m => m.PaTemperature)
            .Where(t => t > -40 && t < 125)
            .ToList();

        if (otherChannelTemperatures.Count >= 3)
        {
            var tempStd = PaEfficiencyEvaluator.CalculateStdDev(otherChannelTemperatures);
            var tempMean = otherChannelTemperatures.Average();

            var validTemps = otherChannelTemperatures
                .Where(t => Math.Abs(t - tempMean) < 3 * tempStd)
                .ToList();

            if (validTemps.Count >= 3)
            {
                referenceTemperature += validTemps.Average();
                referenceCount++;
            }
        }

        if (referenceCount == 0)
        {
            return rawTemperature;
        }

        referenceTemperature /= referenceCount;

        var currentStd = PaEfficiencyEvaluator.CalculateStdDev(channelMetrics.Select(m => m.PaTemperature));
        var currentMean = channelMetrics.Average(m => m.PaTemperature);

        var stableMetrics = channelMetrics
            .Where(m => Math.Abs(m.PaTemperature - currentMean) < 2 * currentStd)
            .ToList();

        var stableMean = stableMetrics.Any()
            ? stableMetrics.Average(m => m.PaTemperature)
            : currentMean;

        driftAmount = stableMean - referenceTemperature;
        var driftThreshold = _options.TemperatureDriftThreshold;

        if (Math.Abs(driftAmount) > driftThreshold)
        {
            driftDetected = true;

            var alpha = _options.KalmanFilterAlpha;
            var beta = 1.0 - alpha;

            var calibratedTemperature = alpha * stableMean + beta * referenceTemperature;

            var minTemperature = -30.0;
            var maxTemperature = 100.0;

            _logger.LogDebug(
                "Kalman calibration applied for channel {ChannelIndex}: " +
                "raw={RawTemp:F2}°C, ref={RefTemp:F2}°C, stable={StableMean:F2}°C, " +
                "calibrated={Calibrated:F2}°C, alpha={Alpha:F2}, beta={Beta:F2}",
                channelMetrics.First().ChannelIndex,
                rawTemperature, referenceTemperature, stableMean,
                calibratedTemperature, alpha, beta);

            return Math.Clamp(calibratedTemperature, minTemperature, maxTemperature);
        }

        return rawTemperature;
    }

    private void UpdateCalibrationHistory(CalibrationResult result)
    {
        var history = _calibrationHistory.GetOrAdd(result.ChannelId, _ => new List<CalibrationResult>());

        lock (history)
        {
            history.Add(result);

            while (history.Count > 100)
            {
                history.RemoveAt(0);
            }
        }
    }

    private void UpdateDriftTrend(CalibrationResult result)
    {
        if (!result.DriftDetected) return;

        _driftTrends.AddOrUpdate(
            result.ChannelId,
            _ => (result.DriftAmount, 1),
            (_, existing) =>
            {
                var (oldTrend, oldCount) = existing;
                var newCount = oldCount + 1;
                var newTrend = (oldTrend * oldCount + result.DriftAmount) / newCount;
                return (newTrend, newCount);
            });
    }

    private async Task RunMaintenanceTaskAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                LogCalibrationSummary();
                CleanupOldHistory();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in calibration maintenance task");
            }
        }
    }

    private void LogCalibrationSummary()
    {
        var totalRequests = _calibrationHistory.Sum(kv => kv.Value.Count);
        var driftCount = _calibrationHistory
            .SelectMany(kv => kv.Value)
            .Count(r => r.DriftDetected);

        if (totalRequests > 0)
        {
            _logger.LogInformation(
                "Calibration summary: total={Total}, drift={DriftCount}, " +
                "drift rate={DriftRate:P2}, channels with drift={DriftChannels}",
                totalRequests,
                driftCount,
                (double)driftCount / totalRequests,
                _driftTrends.Count);

            foreach (var (channelId, (trend, count)) in _driftTrends.Take(5))
            {
                _logger.LogDebug(
                    "Channel {ChannelId} drift trend: {Trend:F3}°C over {Count} samples",
                    channelId, trend, count);
            }
        }
    }

    private void CleanupOldHistory()
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-24);

        foreach (var (channelId, history) in _calibrationHistory)
        {
            lock (history)
            {
                var removed = history.RemoveAll(r => r.CalibrationTime < cutoffTime);
                if (removed > 0)
                {
                    _logger.LogTrace(
                        "Removed {Count} old calibration records for channel {ChannelId}",
                        removed, channelId);
                }
            }
        }
    }

    public IReadOnlyList<CalibrationResult> GetChannelCalibrationHistory(Guid channelId)
    {
        if (_calibrationHistory.TryGetValue(channelId, out var history))
        {
            lock (history)
            {
                return history.ToList().AsReadOnly();
            }
        }
        return Array.Empty<CalibrationResult>();
    }

    public (double DriftTrend, int SampleCount) GetChannelDriftTrend(Guid channelId)
    {
        if (_driftTrends.TryGetValue(channelId, out var trend))
        {
            return trend;
        }
        return (0, 0);
    }

    public async Task<CalibrationResult> QueueCalibrationRequestAsync(
        CalibrationRequest request,
        CancellationToken stoppingToken)
    {
        var tcs = new TaskCompletionSource<CalibrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try
            {
                await _requestChannel.Writer.WriteAsync(request, stoppingToken);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cts.Token, stoppingToken);

                while (!linkedCts.Token.IsCancellationRequested)
                {
                    var history = GetChannelCalibrationHistory(request.ChannelId);
                    var latest = history.LastOrDefault();

                    if (latest != null && latest.CalibrationTime >= request.RequestTime)
                    {
                        tcs.SetResult(latest);
                        return;
                    }

                    await Task.Delay(100, linkedCts.Token);
                }

                tcs.SetException(new TimeoutException(
                    $"Calibration request timed out for channel {request.ChannelId}"));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, stoppingToken);

        return await tcs.Task;
    }

    public void QueueBatchCalibrationRequests(
        IEnumerable<CalibrationRequest> requests,
        CancellationToken stoppingToken)
    {
        foreach (var request in requests)
        {
            _ = _requestChannel.Writer.WriteAsync(request, stoppingToken).AsTask();
        }

        _logger.LogDebug("Queued {Count} calibration requests for batch processing",
            requests.Count());
    }

    public override void Dispose()
    {
        _requestChannel.Writer.TryComplete();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
