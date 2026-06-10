using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MathNet.Numerics.Statistics;

namespace AntennaMonitoring.Modules.SpectrumScanner;

public class SpectrumScanner : ISpectrumScanner
{
    private readonly ILogger<SpectrumScanner> _logger;
    private readonly ISpectrumScanRecordRepository _scanRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IMediator _mediator;
    private readonly SpectrumScanOptions _options;

    public SpectrumScanner(
        ILogger<SpectrumScanner> logger,
        ISpectrumScanRecordRepository scanRepo,
        IChannelRepository channelRepo,
        IMediator mediator,
        IOptions<SpectrumScanOptions> options)
    {
        _logger = logger;
        _scanRepo = scanRepo;
        _channelRepo = channelRepo;
        _mediator = mediator;
        _options = options.Value;
    }

    public async Task<SpectrumScanResult> RunSpectrumScanAsync(
        SpectrumScanRequest request,
        CancellationToken stoppingToken)
    {
        _logger.LogDebug("Running spectrum scan for station {StationId}: {Start}-{End}MHz",
            request.StationId, request.StartFrequencyMhz, request.EndFrequencyMhz);

        var frequencyStep = request.ResolutionBandwidthKhz / 1000.0;
        var numPoints = (int)Math.Ceiling(
            (request.EndFrequencyMhz - request.StartFrequencyMhz) / frequencyStep) + 1;

        var frequencyPoints = new double[numPoints];
        var powerLevels = new double[numPoints];

        for (int i = 0; i < numPoints; i++)
        {
            frequencyPoints[i] = request.StartFrequencyMhz + i * frequencyStep;
            powerLevels[i] = GenerateNoiseFloor(frequencyPoints[i]);
        }

        AddThermalNoise(powerLevels);
        AddSignalComponents(frequencyPoints, powerLevels, request.StartFrequencyMhz, request.EndFrequencyMhz);
        var interferences = DetectAndAddInterferences(frequencyPoints, powerLevels);

        var noiseFloor = CalculateNoiseFloor(powerLevels);
        var spuriousFreeDynamicRange = CalculateSFDR(powerLevels, noiseFloor);

        var interferenceFrequencies = interferences.Select(i => i.Frequency).ToArray();
        var interferencePowers = interferences.Select(i => i.Power).ToArray();
        var interferenceDirections = new double[interferences.Count];

        for (int i = 0; i < interferences.Count; i++)
        {
            interferenceDirections[i] = EstimateDOA(
                interferences[i].Frequency,
                interferences[i].Power,
                request.Channels);
        }

        var nullAngles = Array.Empty<double>();
        var nullDepths = Array.Empty<double>();
        var nullSteeringApplied = false;

        if (interferences.Any() && _options.AutoNullSteering)
        {
            var topInterferences = interferences
                .OrderByDescending(i => i.Power)
                .Take(_options.MaxNullCount)
                .Select(i => interferenceDirections[interferences.IndexOf(i)])
                .ToArray();

            if (topInterferences.Any())
            {
                (nullAngles, nullDepths) = await CalculateNullSteeringWeightsAsync(
                    request.StationId,
                    topInterferences,
                    request.Channels,
                    stoppingToken);

                nullSteeringApplied = true;

                await _mediator.Publish(new NullSteeringAppliedEvent(
                    request.StationId,
                    nullAngles,
                    nullDepths,
                    DateTime.UtcNow), stoppingToken);
            }
        }

        if (interferences.Any())
        {
            await _mediator.Publish(new InterferenceDetectedEvent(
                request.StationId,
                interferenceFrequencies,
                interferencePowers,
                interferenceDirections,
                DateTime.UtcNow), stoppingToken);
        }

        var result = new SpectrumScanResult
        {
            StationId = request.StationId,
            FrequencyPointsMhz = frequencyPoints,
            PowerLevelsDbm = powerLevels,
            InterferenceCount = interferences.Count,
            InterferenceDetails = GenerateInterferenceDetails(interferences),
            InterferenceFrequenciesMhz = interferenceFrequencies,
            InterferencePowersDbm = interferencePowers,
            InterferenceDirectionsDeg = interferenceDirections,
            NullSteeringApplied = nullSteeringApplied,
            NullAnglesDeg = nullAngles,
            NullDepthsDb = nullDepths,
            NoiseFloorDbm = Math.Round(noiseFloor, 4),
            SpuriousFreeDynamicRangeDb = Math.Round(spuriousFreeDynamicRange, 4)
        };

        await _mediator.Publish(new SpectrumScanCompletedEvent(
            request.StationId,
            result,
            DateTime.UtcNow), stoppingToken);

        await SaveScanRecordAsync(request.StationId, result, stoppingToken);

        return result;
    }

    private static double GenerateNoiseFloor(double frequencyMhz)
    {
        var baseNoise = -110.0;
        var frequencySlope = (frequencyMhz - 3500) * 0.01;
        return baseNoise + frequencySlope;
    }

    private static void AddThermalNoise(double[] powerLevels)
    {
        var random = new Random();
        for (int i = 0; i < powerLevels.Length; i++)
        {
            var noise = (random.NextDouble() - 0.5) * 3.0;
            powerLevels[i] += noise;
        }
    }

    private static void AddSignalComponents(
        double[] frequencies,
        double[] powerLevels,
        double startMhz,
        double endMhz)
    {
        var centerFreq = (startMhz + endMhz) / 2;
        var bandwidth = endMhz - startMhz;
        var signalPower = -60.0;

        for (int i = 0; i < frequencies.Length; i++)
        {
            var freqOffset = Math.Abs(frequencies[i] - centerFreq);
            if (freqOffset < bandwidth / 2)
            {
                var rolloff = 1.0 - Math.Pow(freqOffset / (bandwidth / 2), 2);
                powerLevels[i] = Math.Max(powerLevels[i], signalPower + rolloff * 10);
            }
        }
    }

    private List<InterferenceInfo> DetectAndAddInterferences(
        double[] frequencies,
        double[] powerLevels)
    {
        var interferences = new List<InterferenceInfo>();
        var random = new Random();

        if (random.NextDouble() < 0.7)
        {
            var numInterferences = random.Next(1, 4);
            for (int i = 0; i < numInterferences; i++)
            {
                var centerIdx = random.Next(10, frequencies.Length - 10);
                var centerFreq = frequencies[centerIdx];
                var bandwidth = random.NextDouble() * 5 + 0.5;
                var power = _options.InterferencePowerThresholdDbm +
                           random.NextDouble() * 25;

                for (int j = 0; j < frequencies.Length; j++)
                {
                    var freqOffset = Math.Abs(frequencies[j] - centerFreq);
                    if (freqOffset < bandwidth / 2)
                    {
                        var rolloff = 1.0 - Math.Pow(freqOffset / (bandwidth / 2), 2);
                        powerLevels[j] = Math.Max(powerLevels[j], power + rolloff * 5);
                    }
                }

                interferences.Add(new InterferenceInfo
                {
                    Frequency = centerFreq,
                    Power = power,
                    Bandwidth = bandwidth
                });
            }
        }

        for (int i = 2; i < powerLevels.Length - 2; i++)
        {
            if (powerLevels[i] > _options.InterferencePowerThresholdDbm &&
                powerLevels[i] > powerLevels[i - 1] &&
                powerLevels[i] > powerLevels[i + 1] &&
                powerLevels[i] > powerLevels[i - 2] + 3 &&
                powerLevels[i] > powerLevels[i + 2] + 3)
            {
                if (!interferences.Any(inf => Math.Abs(inf.Frequency - frequencies[i]) < 1.0))
                {
                    interferences.Add(new InterferenceInfo
                    {
                        Frequency = frequencies[i],
                        Power = powerLevels[i],
                        Bandwidth = _options.ResolutionBandwidthKhz / 1000.0 * 3
                    });
                }
            }
        }

        return interferences;
    }

    private static double CalculateNoiseFloor(double[] powerLevels)
    {
        var sortedPowers = powerLevels.OrderBy(p => p).Take(powerLevels.Length / 4).ToList();
        return sortedPowers.Any() ? sortedPowers.Average() : -110;
    }

    private static double CalculateSFDR(double[] powerLevels, double noiseFloor)
    {
        var maxSpurious = powerLevels
            .Where(p => p < -10)
            .OrderByDescending(p => p)
            .FirstOrDefault();

        return maxSpurious != 0 ? maxSpurious - noiseFloor : 60;
    }

    private double EstimateDOA(
        double interferenceFreqMhz,
        double interferencePowerDbm,
        IReadOnlyList<Channel> channels)
    {
        var random = new Random((int)(interferenceFreqMhz * 1000));
        var baseAngle = random.NextDouble() * 180 - 90;

        var powerWeight = Math.Min(
            (interferencePowerDbm - _options.InterferencePowerThresholdDbm) / 20.0,
            1.0);

        var accuracyFactor = _options.DoaEstimationAccuracy * (0.8 + 0.2 * powerWeight);
        var error = (random.NextDouble() - 0.5) * (1 - accuracyFactor) * 20;

        var doa = baseAngle + error;

        return Math.Clamp(doa, -90, 90);
    }

    private async Task<(double[] angles, double[] depths)> CalculateNullSteeringWeightsAsync(
        Guid stationId,
        double[] interferenceDirectionsDeg,
        IReadOnlyList<Channel> channels,
        CancellationToken stoppingToken)
    {
        var angles = new List<double>();
        var depths = new List<double>();
        const double wavelength = 0.0857;
        const double elementSpacing = wavelength / 2.0;

        foreach (var directionDeg in interferenceDirectionsDeg)
        {
            var directionRad = directionDeg * Math.PI / 180.0;

            foreach (var channel in channels)
            {
                var dx = channel.ColumnIndex * elementSpacing;
                var dy = channel.RowIndex * elementSpacing;

                var pathDifference = dx * Math.Sin(directionRad) +
                                    dy * Math.Sin(directionRad);

                var nullPhase = -2 * Math.PI * pathDifference / wavelength;

                var currentPhase = channel.CalibrationCoeffPhase ?? 0.0;
                var totalPhase = currentPhase + nullPhase;

                while (totalPhase > Math.PI) totalPhase -= 2 * Math.PI;
                while (totalPhase < -Math.PI) totalPhase += 2 * Math.PI;

                channel.CalibrationCoeffPhase = totalPhase;
                channel.UpdatedAt = DateTime.UtcNow;
            }

            angles.Add(directionDeg);

            var baseDepth = _options.NullDepthTargetDb;
            var random = new Random((int)(directionDeg * 100 + stationId.GetHashCode()));
            var actualDepth = baseDepth - random.NextDouble() * 3 - 2;
            depths.Add(Math.Round(actualDepth, 2));
        }

        await _channelRepo.BulkUpdateAsync(channels, stoppingToken);

        _logger.LogInformation(
            "Null steering applied for station {StationId}: {Count} nulls at {Angles}",
            stationId, angles.Count, string.Join(", ", angles.Select(a => $"{a:F1}°")));

        return (angles.ToArray(), depths.ToArray());
    }

    private static string GenerateInterferenceDetails(List<InterferenceInfo> interferences)
    {
        if (!interferences.Any()) return "无外部干扰";

        var details = interferences.Select((inf, idx) =>
            $"干扰{idx + 1}: {inf.Frequency:F2}MHz, {inf.Power:F1}dBm, 带宽{inf.Bandwidth:F1}MHz");

        return string.Join("；", details);
    }

    public async Task<SpectrumScanRecord> SaveScanRecordAsync(
        Guid stationId,
        SpectrumScanResult result,
        CancellationToken stoppingToken)
    {
        var record = new SpectrumScanRecord
        {
            Id = Guid.NewGuid(),
            StationId = stationId,
            StartFrequencyMhz = result.FrequencyPointsMhz.First(),
            EndFrequencyMhz = result.FrequencyPointsMhz.Last(),
            ResolutionBandwidthKhz = _options.ResolutionBandwidthKhz,
            FrequencyPointsMhz = result.FrequencyPointsMhz,
            PowerLevelsDbm = result.PowerLevelsDbm,
            InterferenceCount = result.InterferenceCount,
            InterferenceDetails = result.InterferenceDetails,
            InterferenceFrequenciesMhz = result.InterferenceFrequenciesMhz,
            InterferencePowersDbm = result.InterferencePowersDbm,
            InterferenceDirectionsDeg = result.InterferenceDirectionsDeg,
            NullSteeringApplied = result.NullSteeringApplied,
            NullAnglesDeg = result.NullAnglesDeg,
            NullDepthsDb = result.NullDepthsDb,
            NoiseFloorDbm = result.NoiseFloorDbm,
            SpuriousFreeDynamicRangeDb = result.SpuriousFreeDynamicRangeDb,
            ScanTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        return await _scanRepo.AddAsync(record, stoppingToken);
    }

    public Task<IReadOnlyList<SpectrumScanRecord>> GetScanHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken)
    {
        return _scanRepo.GetByStationIdAndTimeRangeAsync(stationId, startTime, endTime, stoppingToken);
    }

    public async Task ApplyNullSteeringAsync(
        Guid stationId,
        double[] interferenceDirectionsDeg,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Manual null steering requested for station {StationId}", stationId);

        var channels = (await _channelRepo.GetByStationIdAsync(stationId, stoppingToken)).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = stationId,
            StartFrequencyMhz = _options.StartFrequencyMhz,
            EndFrequencyMhz = _options.EndFrequencyMhz,
            ResolutionBandwidthKhz = _options.ResolutionBandwidthKhz,
            Channels = channels.AsReadOnly()
        };

        await CalculateNullSteeringWeightsAsync(
            stationId,
            interferenceDirectionsDeg,
            channels,
            stoppingToken);
    }

    private class InterferenceInfo
    {
        public double Frequency { get; set; }
        public double Power { get; set; }
        public double Bandwidth { get; set; }
    }
}
