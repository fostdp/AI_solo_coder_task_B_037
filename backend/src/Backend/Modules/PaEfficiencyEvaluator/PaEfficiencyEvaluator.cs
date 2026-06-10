using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MathNet.Numerics.Statistics;

namespace AntennaMonitoring.Modules.PaEfficiencyEvaluator;

public class PaEfficiencyEvaluator : IPaEfficiencyEvaluator
{
    private readonly ILogger<PaEfficiencyEvaluator> _logger;
    private readonly IPaEfficiencyRecordRepository _efficiencyRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IMediator _mediator;
    private readonly PaEfficiencyOptions _options;

    public PaEfficiencyEvaluator(
        ILogger<PaEfficiencyEvaluator> logger,
        IPaEfficiencyRecordRepository efficiencyRepo,
        IChannelRepository channelRepo,
        IMediator mediator,
        IOptions<PaEfficiencyOptions> options)
    {
        _logger = logger;
        _efficiencyRepo = efficiencyRepo;
        _channelRepo = channelRepo;
        _mediator = mediator;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<PaEfficiencyResult>> RunEfficiencyEvaluationAsync(
        PaEfficiencyRequest request,
        CancellationToken stoppingToken)
    {
        _logger.LogDebug("Running PA efficiency evaluation for station {StationId}", request.StationId);

        var results = new List<PaEfficiencyResult>();
        var historyDict = new Dictionary<Guid, List<PaEfficiencyRecord>>();

        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-24);

        foreach (var channel in request.Channels)
        {
            try
            {
                var history = await _efficiencyRepo.GetByChannelIdAndTimeRangeAsync(
                    channel.Id, startTime, endTime, stoppingToken);
                historyDict[channel.Id] = history.ToList();

                var result = await EvaluateChannelEfficiencyAsync(
                    channel, request.RecentMetrics, historyDict[channel.Id], stoppingToken);

                results.Add(result);

                if (result.NeedsReplacement)
                {
                    await _mediator.Publish(new PaEfficiencyLowEvent(
                        request.StationId,
                        channel.Id,
                        channel.ChannelIndex,
                        result.EfficiencyPercent,
                        _options.ThresholdPercent,
                        result.ReplacementReason,
                        DateTime.UtcNow), stoppingToken);
                }

                await SaveEfficiencyRecordAsync(request.StationId, result, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating PA efficiency for channel {ChannelId}",
                    channel.Id);
            }
        }

        await _mediator.Publish(new PaEfficiencyCompletedEvent(
            request.StationId,
            results.AsReadOnly(),
            DateTime.UtcNow), stoppingToken);

        return results.AsReadOnly();
    }

    private Task<PaEfficiencyResult> EvaluateChannelEfficiencyAsync(
        Channel channel,
        IReadOnlyList<ChannelMetric> recentMetrics,
        List<PaEfficiencyRecord> history,
        CancellationToken stoppingToken)
    {
        var channelMetrics = recentMetrics
            .Where(m => m.ChannelId == channel.Id.ToString() || m.ChannelIndex == channel.ChannelIndex)
            .OrderByDescending(m => m.Timestamp)
            .Take(5)
            .ToList();

        double outputPowerDbm;
        double paTemperature;
        double inputPowerDbm;

        if (channelMetrics.Any())
        {
            outputPowerDbm = channelMetrics.Average(m => m.TxPower);
            paTemperature = channelMetrics.Average(m => m.PaTemperature);
            inputPowerDbm = outputPowerDbm - _options.NominalGainDb;
        }
        else
        {
            outputPowerDbm = channel.TxPower ?? 43.0;
            paTemperature = 45.0;
            inputPowerDbm = outputPowerDbm - _options.NominalGainDb;
        }

        var gainDb = outputPowerDbm - inputPowerDbm;
        var outputPowerW = Math.Pow(10, outputPowerDbm / 10) / 1000;
        var inputPowerW = Math.Pow(10, inputPowerDbm / 10) / 1000;

        var dcCurrentA = EstimateDcCurrent(outputPowerW, paTemperature);
        var dcPowerW = dcCurrentA * _options.NominalDcVoltageV;
        var rfPowerW = outputPowerW;

        var drainEfficiencyPercent = dcPowerW > 0 ? (rfPowerW / dcPowerW) * 100 : 0;
        var powerAddedEfficiencyPercent = dcPowerW > 0
            ? ((rfPowerW - inputPowerW) / dcPowerW) * 100
            : 0;

        var temperatureDerating = CalculateTemperatureDerating(paTemperature);
        var efficiencyPercent = drainEfficiencyPercent * temperatureDerating;

        var (efficiencyHistory, historyTimestamps) = BuildHistoryArray(history, efficiencyPercent);
        var efficiencyDecayRate = CalculateEfficiencyDecayRate(efficiencyHistory);
        var predictedRemainingHours = PredictRemainingLifetime(
            efficiencyPercent, efficiencyDecayRate, paTemperature);

        var needsReplacement = efficiencyPercent < _options.ThresholdPercent ||
                             efficiencyDecayRate > _options.DecayRateAlarmThreshold ||
                             predictedRemainingHours < _options.MinimumRemainingHours;

        var replacementReason = GenerateReplacementReason(
            efficiencyPercent, efficiencyDecayRate, predictedRemainingHours, paTemperature, gainDb);

        return Task.FromResult(new PaEfficiencyResult
        {
            ChannelId = channel.Id,
            ChannelIndex = channel.ChannelIndex,
            PaTemperature = Math.Round(paTemperature, 2),
            OutputPowerDbm = Math.Round(outputPowerDbm, 4),
            InputPowerDbm = Math.Round(inputPowerDbm, 4),
            GainDb = Math.Round(gainDb, 4),
            EfficiencyPercent = Math.Round(efficiencyPercent, 4),
            PowerAddedEfficiencyPercent = Math.Round(powerAddedEfficiencyPercent, 4),
            DcCurrentA = Math.Round(dcCurrentA, 6),
            DcVoltageV = _options.NominalDcVoltageV,
            DcPowerW = Math.Round(dcPowerW, 4),
            RfPowerW = Math.Round(rfPowerW, 4),
            EfficiencyDecayRate = Math.Round(efficiencyDecayRate, 8),
            PredictedRemainingHours = Math.Round(predictedRemainingHours, 2),
            NeedsReplacement = needsReplacement,
            ReplacementReason = replacementReason,
            EfficiencyHistory = efficiencyHistory,
            HistoryTimestamps = historyTimestamps
        });
    }

    private static double EstimateDcCurrent(double outputPowerW, double temperature)
    {
        var nominalCurrent = (outputPowerW / 28.0) * 2.2;
        var temperatureFactor = 1 + (temperature - 25) * 0.005;
        return nominalCurrent * temperatureFactor;
    }

    private static double CalculateTemperatureDerating(double temperature)
    {
        if (temperature <= 50) return 1.0;
        if (temperature <= 70) return 1.0 - (temperature - 50) * 0.005;
        return 0.9 - (temperature - 70) * 0.01;
    }

    private static (double[] history, DateTime[] timestamps) BuildHistoryArray(
        List<PaEfficiencyRecord> history,
        double currentEfficiency)
    {
        var historyList = new List<double>();
        var timestampList = new List<DateTime>();

        var recentHistory = history
            .OrderByDescending(r => r.MeasurementTime)
            .Take(23)
            .Reverse()
            .ToList();

        foreach (var record in recentHistory)
        {
            historyList.Add(record.EfficiencyPercent);
            timestampList.Add(record.MeasurementTime);
        }

        historyList.Add(currentEfficiency);
        timestampList.Add(DateTime.UtcNow);

        while (historyList.Count < 24)
        {
            historyList.Insert(0, currentEfficiency * (0.98 + historyList.Count * 0.001));
            timestampList.Insert(0, DateTime.UtcNow.AddMinutes(-5 * (24 - historyList.Count)));
        }

        return (historyList.Take(24).ToArray(), timestampList.Take(24).ToArray());
    }

    private static double CalculateEfficiencyDecayRate(double[] efficiencyHistory)
    {
        if (efficiencyHistory.Length < 2) return 0;

        var n = efficiencyHistory.Length;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var y = efficiencyHistory;

        var slope = x.Zip(y, (xi, yi) => xi * yi).Sum() * n;
        slope -= x.Sum() * y.Sum();
        slope /= n * x.Sum(xi => xi * xi) - x.Sum() * x.Sum();

        return -slope / y.Average();
    }

    private double PredictRemainingLifetime(
        double currentEfficiency,
        double decayRate,
        double temperature)
    {
        var threshold = _options.ThresholdPercent;
        var currentDecayRate = decayRate;

        if (temperature > 70)
        {
            currentDecayRate *= 1 + (temperature - 70) * 0.02;
        }

        if (currentDecayRate <= 0)
        {
            currentDecayRate = 0.0001;
        }

        var efficiencyToLose = currentEfficiency - threshold;
        if (efficiencyToLose <= 0) return 0;

        var hoursRemaining = efficiencyToLose / (currentEfficiency * currentDecayRate);
        hoursRemaining *= _options.IntervalMinutes / 60.0;

        return Math.Clamp(hoursRemaining, 0, 87600);
    }

    private string GenerateReplacementReason(
        double efficiencyPercent,
        double decayRate,
        double remainingHours,
        double temperature,
        double gainDb)
    {
        var reasons = new List<string>();

        if (efficiencyPercent < _options.ThresholdPercent)
        {
            reasons.Add($"效率过低：当前{efficiencyPercent:F1}% < 阈值{_options.ThresholdPercent:F1}%");
        }

        if (decayRate > _options.DecayRateAlarmThreshold)
        {
            reasons.Add($"效率衰减过快：衰减率{decayRate:F6} > 阈值{_options.DecayRateAlarmThreshold:F6}");
        }

        if (remainingHours < _options.MinimumRemainingHours)
        {
            reasons.Add($"剩余寿命不足：预测{remainingHours:F1}小时 < 阈值{_options.MinimumRemainingHours:F1}小时");
        }

        if (temperature > 75)
        {
            reasons.Add($"温度过高：{temperature:F1}°C，加速老化");
        }

        if (gainDb < _options.NominalGainDb * 0.9)
        {
            reasons.Add($"增益偏低：{gainDb:F1}dB < 标称{_options.NominalGainDb:F1}dB的90%");
        }

        return reasons.Any() ? string.Join("；", reasons) : "工作正常";
    }

    public async Task<PaEfficiencyRecord> SaveEfficiencyRecordAsync(
        Guid stationId,
        PaEfficiencyResult result,
        CancellationToken stoppingToken)
    {
        var record = new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            StationId = stationId,
            ChannelId = result.ChannelId,
            ChannelIndex = result.ChannelIndex,
            PaTemperature = result.PaTemperature,
            OutputPowerDbm = result.OutputPowerDbm,
            InputPowerDbm = result.InputPowerDbm,
            GainDb = result.GainDb,
            EfficiencyPercent = result.EfficiencyPercent,
            PowerAddedEfficiencyPercent = result.PowerAddedEfficiencyPercent,
            DcCurrentA = result.DcCurrentA,
            DcVoltageV = result.DcVoltageV,
            DcPowerW = result.DcPowerW,
            RfPowerW = result.RfPowerW,
            EfficiencyDecayRate = result.EfficiencyDecayRate,
            PredictedRemainingHours = result.PredictedRemainingHours,
            NeedsReplacement = result.NeedsReplacement,
            ReplacementReason = result.ReplacementReason,
            EfficiencyHistory = result.EfficiencyHistory,
            HistoryTimestamps = result.HistoryTimestamps,
            MeasurementTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        return await _efficiencyRepo.AddAsync(record, stoppingToken);
    }

    public async Task<IReadOnlyList<PaEfficiencyRecord>> GetEfficiencyHistoryAsync(
        Guid stationId,
        Guid? channelId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken)
    {
        if (channelId.HasValue)
        {
            return await _efficiencyRepo.GetByChannelIdAndTimeRangeAsync(
                channelId.Value, startTime, endTime, stoppingToken);
        }
        return await _efficiencyRepo.GetByStationIdAndTimeRangeAsync(
            stationId, startTime, endTime, stoppingToken);
    }

    public Task<IReadOnlyList<PaEfficiencyRecord>> GetChannelsNeedingReplacementAsync(
        CancellationToken stoppingToken)
    {
        return _efficiencyRepo.GetNeedingReplacementAsync(stoppingToken);
    }
}
