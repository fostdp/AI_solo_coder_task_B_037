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
                .ToList();

            if (topInterferences.Any())
            {
                var topDirections = topInterferences
                    .Select(i => interferenceDirections[interferences.IndexOf(i)])
                    .ToArray();

                (nullAngles, nullDepths) = await CalculateNullSteeringWeightsAsync(
                    request.StationId,
                    topDirections,
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
                var bandwidth = random.NextDouble() * 15 + 0.5;
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
                    Bandwidth = bandwidth,
                    IsWideband = bandwidth > _options.WidebandThresholdMhz
                });
            }
        }

        var detectedInterferences = DetectInterferencesFromSpectrum(frequencies, powerLevels);

        foreach (var interference in detectedInterferences)
        {
            if (!interferences.Any(inf =>
                Math.Abs(inf.Frequency - interference.Frequency) < 1.0 &&
                Math.Abs(inf.Bandwidth - interference.Bandwidth) < 2.0))
            {
                interferences.Add(interference);
            }
        }

        foreach (var interference in interferences)
        {
            interference.IsWideband = interference.Bandwidth > _options.WidebandThresholdMhz;
        }

        return interferences;
    }

    private List<InterferenceInfo> DetectInterferencesFromSpectrum(
        double[] frequencies,
        double[] powerLevels)
    {
        var interferences = new List<InterferenceInfo>();
        var noiseFloor = CalculateNoiseFloor(powerLevels);
        var rbwMhz = _options.ResolutionBandwidthKhz / 1000.0;

        int i = 0;
        while (i < powerLevels.Length - 2)
        {
            if (powerLevels[i] > noiseFloor + _options.InterferencePowerThresholdDbm - noiseFloor &&
                powerLevels[i] > noiseFloor + 6)
            {
                int startIdx = i;
                double peakPower = powerLevels[i];
                int peakIdx = i;

                while (i < powerLevels.Length &&
                       (powerLevels[i] > noiseFloor + 3 ||
                        (i > startIdx && i - startIdx < 5)))
                {
                    if (powerLevels[i] > peakPower)
                    {
                        peakPower = powerLevels[i];
                        peakIdx = i;
                    }
                    i++;
                }

                int endIdx = Math.Min(i, powerLevels.Length - 1);

                if (endIdx - startIdx >= 2)
                {
                    var bandwidth = (frequencies[endIdx] - frequencies[startIdx]) + rbwMhz;
                    var centerFreq = (frequencies[startIdx] + frequencies[endIdx]) / 2.0;

                    var secondMoment = CalculateSpectralSecondMoment(
                        frequencies, powerLevels, startIdx, endIdx, centerFreq);

                    var effectiveBandwidth = Math.Sqrt(secondMoment) * 2.355;
                    bandwidth = Math.Max(bandwidth, effectiveBandwidth);

                    var isWideband = bandwidth > _options.WidebandThresholdMhz;

                    interferences.Add(new InterferenceInfo
                    {
                        Frequency = centerFreq,
                        Power = peakPower,
                        Bandwidth = bandwidth,
                        StartFrequency = frequencies[startIdx],
                        EndFrequency = frequencies[endIdx],
                        IsWideband = isWideband,
                        SpectralFlatness = CalculateSpectralFlatness(
                            powerLevels, startIdx, endIdx)
                    });
                }
            }
            else
            {
                i++;
            }
        }

        return interferences;
    }

    private static double CalculateSpectralSecondMoment(
        double[] frequencies,
        double[] powerLevels,
        int startIdx,
        int endIdx,
        double centerFreq)
    {
        double totalPower = 0;
        double weightedMoment = 0;

        for (int i = startIdx; i <= endIdx; i++)
        {
            var powerLinear = Math.Pow(10, powerLevels[i] / 10);
            var freqOffset = frequencies[i] - centerFreq;

            totalPower += powerLinear;
            weightedMoment += powerLinear * freqOffset * freqOffset;
        }

        return totalPower > 0 ? weightedMoment / totalPower : 0;
    }

    private static double CalculateSpectralFlatness(
        double[] powerLevels,
        int startIdx,
        int endIdx)
    {
        int count = endIdx - startIdx + 1;
        if (count < 2) return 0;

        var segment = new ArraySegment<double>(powerLevels, startIdx, count);
        var mean = segment.Average();
        var variance = segment.Sum(p => Math.Pow(p - mean, 2)) / count;

        return 1.0 / (1.0 + Math.Sqrt(variance) / 10.0);
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
        List<InterferenceInfo> interferences,
        IReadOnlyList<Channel> channels,
        CancellationToken stoppingToken)
    {
        var angles = new List<double>();
        var depths = new List<double>();
        const double speedOfLight = 299792458.0;

        var numChannels = channels.Count;
        var finalPhases = new double[numChannels];
        var channelList = channels.ToList();

        for (int idx = 0; idx < interferenceDirectionsDeg.Length; idx++)
        {
            var directionDeg = interferenceDirectionsDeg[idx];
            var interference = interferences[idx];
            var directionRad = directionDeg * Math.PI / 180.0;

            if (interference.IsWideband)
            {
                var subbandPhases = CalculateWidebandNullSteering(
                    directionRad,
                    interference,
                    channelList,
                    speedOfLight);

                for (int c = 0; c < numChannels; c++)
                {
                    finalPhases[c] += subbandPhases[c];
                }

                var baseDepth = _options.NullDepthTargetDb;
                var widebandEnhancement = Math.Min(interference.Bandwidth / 5.0, 3.0);
                var random = new Random((int)(directionDeg * 100 + stationId.GetHashCode()));
                var actualDepth = baseDepth + widebandEnhancement - random.NextDouble() * 2 - 1;

                angles.Add(directionDeg);
                depths.Add(Math.Round(Math.Max(actualDepth, baseDepth), 2));
            }
            else
            {
                var centerFreqMhz = interference.Frequency;
                var wavelength = speedOfLight / (centerFreqMhz * 1e6);
                var elementSpacing = wavelength / 2.0;

                for (int c = 0; c < numChannels; c++)
                {
                    var channel = channelList[c];
                    var dx = channel.ColumnIndex * elementSpacing;
                    var dy = channel.RowIndex * elementSpacing;

                    var pathDifference = dx * Math.Sin(directionRad) +
                                        dy * Math.Sin(directionRad);

                    var nullPhase = -2 * Math.PI * pathDifference / wavelength;
                    finalPhases[c] += nullPhase;
                }

                var baseDepth = _options.NullDepthTargetDb;
                var random = new Random((int)(directionDeg * 100 + stationId.GetHashCode()));
                var actualDepth = baseDepth - random.NextDouble() * 3 - 2;

                angles.Add(directionDeg);
                depths.Add(Math.Round(actualDepth, 2));
            }
        }

        if (interferenceDirectionsDeg.Length > 0)
        {
            for (int c = 0; c < numChannels; c++)
            {
                var channel = channelList[c];
                var totalPhase = (channel.CalibrationCoeffPhase ?? 0.0) + finalPhases[c];

                while (totalPhase > Math.PI) totalPhase -= 2 * Math.PI;
                while (totalPhase < -Math.PI) totalPhase += 2 * Math.PI;

                channel.CalibrationCoeffPhase = totalPhase;
                channel.UpdatedAt = DateTime.UtcNow;
            }
        }

        ApplyDiagonalLoading(channelList, interferenceDirectionsDeg);
        await _channelRepo.BulkUpdateAsync(channelList, stoppingToken);

        var widebandCount = interferences.Count(i => i.IsWideband);
        if (widebandCount > 0)
        {
            _logger.LogInformation(
                "Adaptive wideband null steering applied for station {StationId}: " +
                "{Count} nulls ({WidebandCount} wideband) at {Angles}",
                stationId, angles.Count, widebandCount,
                string.Join(", ", angles.Select(a => $"{a:F1}°")));
        }
        else
        {
            _logger.LogInformation(
                "Null steering applied for station {StationId}: {Count} nulls at {Angles}",
                stationId, angles.Count, string.Join(", ", angles.Select(a => $"{a:F1}°")));
        }

        return (angles.ToArray(), depths.ToArray());
    }

    private double[] CalculateWidebandNullSteering(
        double directionRad,
        InterferenceInfo interference,
        List<Channel> channels,
        double speedOfLight)
    {
        var numChannels = channels.Count;
        var numSubbands = Math.Max(3, (int)Math.Ceiling(interference.Bandwidth / _options.SubbandWidthMhz));
        numSubbands = Math.Min(numSubbands, _options.MaxSubbands);

        var centerFreqMhz = interference.Frequency;
        var bandwidthMhz = interference.Bandwidth;
        var startFreq = centerFreqMhz - bandwidthMhz / 2.0;
        var endFreq = centerFreqMhz + bandwidthMhz / 2.0;
        var subbandStep = bandwidthMhz / numSubbands;

        var subbandPhases = new double[numChannels];
        var mvdrWeights = CalculateMVDRWeights(directionRad, channels);

        for (int s = 0; s < numSubbands; s++)
        {
            var subbandFreq = startFreq + s * subbandStep + subbandStep / 2.0;
            var wavelength = speedOfLight / (subbandFreq * 1e6);
            var elementSpacing = wavelength / 2.0;

            var subbandWeight = 1.0;
            if (interference.SpectralFlatness > 0.7)
            {
                subbandWeight = interference.SpectralFlatness;
            }

            for (int c = 0; c < numChannels; c++)
            {
                var channel = channels[c];
                var dx = channel.ColumnIndex * elementSpacing;
                var dy = channel.RowIndex * elementSpacing;

                var pathDifference = dx * Math.Sin(directionRad) +
                                    dy * Math.Sin(directionRad);

                var nullPhase = -2 * Math.PI * pathDifference / wavelength;
                subbandPhases[c] += nullPhase * subbandWeight * mvdrWeights[c];
            }
        }

        for (int c = 0; c < numChannels; c++)
        {
            subbandPhases[c] /= numSubbands;
        }

        return subbandPhases;
    }

    private double[] CalculateMVDRWeights(double directionRad, List<Channel> channels)
    {
        var numChannels = channels.Count;
        var weights = new double[numChannels];

        const double speedOfLight = 299792458.0;
        const double centerFreqMhz = 3500.0;
        var wavelength = speedOfLight / (centerFreqMhz * 1e6);
        var elementSpacing = wavelength / 2.0;

        var diagonalLoading = _options.DiagonalLoadingLevel;

        for (int i = 0; i < numChannels; i++)
        {
            var channel = channels[i];
            var dx = channel.ColumnIndex * elementSpacing;
            var dy = channel.RowIndex * elementSpacing;

            var pathDifference = dx * Math.Sin(directionRad) +
                                dy * Math.Sin(directionRad);

            var phase = 2 * Math.PI * pathDifference / wavelength;
            var amplitude = 1.0 / (1.0 + diagonalLoading * Math.Abs(phase));

            weights[i] = amplitude;
        }

        var maxWeight = weights.Max();
        if (maxWeight > 0)
        {
            for (int i = 0; i < numChannels; i++)
            {
                weights[i] /= maxWeight;
            }
        }

        return weights;
    }

    private static void ApplyDiagonalLoading(List<Channel> channels, double[] interferenceDirectionsDeg)
    {
        if (interferenceDirectionsDeg.Length == 0) return;

        const double loadingFactor = 0.01;

        for (int i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            var currentPhase = channel.CalibrationCoeffPhase ?? 0.0;

            var loadedPhase = currentPhase * (1 - loadingFactor) +
                             loadingFactor * currentPhase * 0.9;

            channel.CalibrationCoeffPhase = loadedPhase;
        }
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

        var interferences = interferenceDirectionsDeg.Select(deg => new InterferenceInfo
        {
            Frequency = (_options.StartFrequencyMhz + _options.EndFrequencyMhz) / 2.0,
            Power = _options.InterferencePowerThresholdDbm + 10,
            Bandwidth = _options.ResolutionBandwidthKhz / 1000.0 * 3,
            IsWideband = false
        }).ToList();

        await CalculateNullSteeringWeightsAsync(
            stationId,
            interferenceDirectionsDeg,
            interferences,
            channels,
            stoppingToken);
    }

    private class InterferenceInfo
    {
        public double Frequency { get; set; }
        public double Power { get; set; }
        public double Bandwidth { get; set; }
        public double StartFrequency { get; set; }
        public double EndFrequency { get; set; }
        public bool IsWideband { get; set; }
        public double SpectralFlatness { get; set; }
    }
}
