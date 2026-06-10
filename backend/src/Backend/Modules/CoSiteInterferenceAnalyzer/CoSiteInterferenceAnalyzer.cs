using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.CoSiteInterferenceAnalyzer;

public class CoSiteInterferenceAnalyzer : ICoSiteInterferenceAnalyzer
{
    private readonly ILogger<CoSiteInterferenceAnalyzer> _logger;
    private readonly ICoSiteInterferenceRecordRepository _interferenceRepo;
    private readonly ICoSiteAntennaRepository _cositeAntennaRepo;
    private readonly IMediator _mediator;
    private readonly CoSiteInterferenceOptions _options;

    public CoSiteInterferenceAnalyzer(
        ILogger<CoSiteInterferenceAnalyzer> logger,
        ICoSiteInterferenceRecordRepository interferenceRepo,
        ICoSiteAntennaRepository cositeAntennaRepo,
        IMediator mediator,
        IOptions<CoSiteInterferenceOptions> options)
    {
        _logger = logger;
        _interferenceRepo = interferenceRepo;
        _cositeAntennaRepo = cositeAntennaRepo;
        _mediator = mediator;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<CoSiteInterferenceResult>> RunInterferenceAnalysisAsync(
        CoSiteInterferenceRequest request,
        CancellationToken stoppingToken)
    {
        _logger.LogDebug("Running co-site interference analysis for station {StationId}", request.StationId);

        var results = new List<CoSiteInterferenceResult>();
        var optimizedAntennas = OptimizeAntennaList(request.CoSiteAntennas);

        foreach (var interferingAntenna in optimizedAntennas)
        {
            try
            {
                var result = await AnalyzeSingleInterferenceAsync(request, interferingAntenna, stoppingToken);
                results.Add(result);

                if (!result.IsIsolationSufficient)
                {
                    await _mediator.Publish(new IsolationInsufficientEvent(
                        request.StationId,
                        interferingAntenna.Id,
                        interferingAntenna.OperatorName,
                        result.IsolationDb,
                        _options.IsolationThresholdDb,
                        result.Recommendation,
                        DateTime.UtcNow), stoppingToken);
                }

                await SaveInterferenceRecordAsync(request.StationId, interferingAntenna, result, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing interference from antenna {AntennaId} for station {StationId}",
                    interferingAntenna.Id, request.StationId);
            }
        }

        await _mediator.Publish(new CoSiteInterferenceCompletedEvent(
            request.StationId,
            results.AsReadOnly(),
            DateTime.UtcNow), stoppingToken);

        return results.AsReadOnly();
    }

    private IReadOnlyList<CoSiteAntenna> OptimizeAntennaList(IReadOnlyList<CoSiteAntenna> antennas)
    {
        if (antennas.Count <= 10)
        {
            return antennas;
        }

        var antennaGroups = ClusterAntennasByDistance(antennas);
        var optimizedList = new List<CoSiteAntenna>();
        var processedGroups = new HashSet<int>();

        foreach (var antenna in antennas)
        {
            var groupId = antennaGroups[antenna.Id];

            if (!processedGroups.Contains(groupId))
            {
                var groupAntennas = antennaGroups
                    .Where(kvp => kvp.Value == groupId)
                    .Join(antennas, kvp => kvp.Key, a => a.Id, (kvp, a) => a)
                    .ToList();

                if (groupAntennas.Count == 1)
                {
                    optimizedList.Add(antenna);
                }
                else
                {
                    var representative = CreateRepresentativeAntenna(groupAntennas);
                    optimizedList.Add(representative);
                }

                processedGroups.Add(groupId);
            }
        }

        if (optimizedList.Count > antennas.Count / 2)
        {
            optimizedList = optimizedList
                .OrderBy(a => a.SeparationDistanceMeters)
                .Take(Math.Max(10, antennas.Count / 2))
                .ToList();
        }

        _logger.LogInformation("Optimized antenna list from {OriginalCount} to {OptimizedCount} using clustering",
            antennas.Count, optimizedList.Count);

        return optimizedList.AsReadOnly();
    }

    private Dictionary<Guid, int> ClusterAntennasByDistance(IReadOnlyList<CoSiteAntenna> antennas)
    {
        var groups = new Dictionary<Guid, int>();
        const double clusterThresholdMeters = 20.0;

        for (int i = 0; i < antennas.Count; i++)
        {
            groups[antennas[i].Id] = i;
        }

        for (int i = 0; i < antennas.Count; i++)
        {
            for (int j = i + 1; j < antennas.Count; j++)
            {
                var distance = CalculateAntennaDistance(antennas[i], antennas[j]);

                if (distance < clusterThresholdMeters)
                {
                    var groupI = groups[antennas[i].Id];
                    var groupJ = groups[antennas[j].Id];

                    if (groupI != groupJ)
                    {
                        foreach (var key in groups.Keys.ToList())
                        {
                            if (groups[key] == groupJ)
                            {
                                groups[key] = groupI;
                            }
                        }
                    }
                }
            }
        }

        var groupIds = groups.Values.Distinct().ToList();
        for (int i = 0; i < groupIds.Count; i++)
        {
            foreach (var key in groups.Keys.ToList())
            {
                if (groups[key] == groupIds[i])
                {
                    groups[key] = i;
                }
            }
        }

        return groups;
    }

    private static double CalculateAntennaDistance(CoSiteAntenna a, CoSiteAntenna b)
    {
        var dx = a.SeparationDistanceMeters * Math.Cos(a.AzimuthAngleDeg * Math.PI / 180) -
                 b.SeparationDistanceMeters * Math.Cos(b.AzimuthAngleDeg * Math.PI / 180);
        var dy = a.SeparationDistanceMeters * Math.Sin(a.AzimuthAngleDeg * Math.PI / 180) -
                 b.SeparationDistanceMeters * Math.Sin(b.AzimuthAngleDeg * Math.PI / 180);
        var dz = a.HeightOffsetMeters - b.HeightOffsetMeters;

        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static CoSiteAntenna CreateRepresentativeAntenna(List<CoSiteAntenna> group)
    {
        if (group.Count == 0) throw new ArgumentException("Group cannot be empty");

        var nearest = group.OrderBy(a => a.SeparationDistanceMeters).First();

        var avgDistance = group.Average(a => a.SeparationDistanceMeters);
        var avgAzimuth = group.Average(a => a.AzimuthAngleDeg);
        var avgElevation = group.Average(a => a.ElevationAngleDeg);
        var avgHeight = group.Average(a => a.HeightOffsetMeters);
        var maxPower = group.Max(a => a.TransmitPowerDbm);
        var minFreqStart = group.Min(a => a.FrequencyStartMhz);
        var maxFreqEnd = group.Max(a => a.FrequencyEndMhz);

        return new CoSiteAntenna
        {
            Id = nearest.Id,
            OperatorName = $"{group.Count} antennas",
            AntennaType = "Cluster",
            FrequencyStartMhz = minFreqStart,
            FrequencyEndMhz = maxFreqEnd,
            TransmitPowerDbm = maxPower,
            SeparationDistanceMeters = avgDistance,
            AzimuthAngleDeg = avgAzimuth,
            ElevationAngleDeg = avgElevation,
            HeightOffsetMeters = avgHeight,
            IsApproximated = true
        };
    }

    private double CalculateIsolation(
        double distanceMeters,
        double azimuthDeg,
        double elevationDeg,
        double heightOffsetMeters,
        double interfererStartMhz,
        double interfererEndMhz,
        double selfStartMhz,
        double selfEndMhz,
        double interfererPowerDbm)
    {
        var frequencyMhz = (interfererStartMhz + interfererEndMhz) / 2.0;

        if (distanceMeters > _options.FastCalculationDistanceThresholdMeters)
        {
            return CalculateIsolationApproximate(
                distanceMeters,
                azimuthDeg,
                frequencyMhz,
                interfererStartMhz,
                interfererEndMhz,
                selfStartMhz,
                selfEndMhz,
                interfererPowerDbm);
        }

        var wavelengthMeters = 299.792458 / frequencyMhz;

        var freeSpacePathLoss = 20 * Math.Log10(4 * Math.PI * distanceMeters / wavelengthMeters);

        var mutualCouplingLoss = CalculateMutualCouplingLoss(
            distanceMeters,
            azimuthDeg,
            elevationDeg,
            heightOffsetMeters,
            frequencyMhz);

        var polarizationLoss = CalculatePolarizationLoss(elevationDeg);

        var frequencyOverlap = CalculateFrequencyOverlap(
            interfererStartMhz,
            interfererEndMhz,
            selfStartMhz,
            selfEndMhz);

        var antennaGain = 15.0;
        var offAxisAttenuation = CalculateOffAxisAttenuation(azimuthDeg, elevationDeg);

        var isolationDb = freeSpacePathLoss + mutualCouplingLoss + polarizationLoss +
                          offAxisAttenuation - antennaGain - frequencyOverlap * 10;

        var minIsolation = 0.0;
        var maxIsolation = 100.0;

        return Math.Clamp(isolationDb, minIsolation, maxIsolation);
    }

    private double CalculateIsolationApproximate(
        double distanceMeters,
        double azimuthDeg,
        double frequencyMhz,
        double interfererStartMhz,
        double interfererEndMhz,
        double selfStartMhz,
        double selfEndMhz,
        double interfererPowerDbm)
    {
        var wavelengthMeters = 299.792458 / frequencyMhz;

        var freeSpacePathLoss = 20 * Math.Log10(4 * Math.PI * distanceMeters / wavelengthMeters);

        var frequencyOverlap = CalculateFrequencyOverlap(
            interfererStartMhz,
            interfererEndMhz,
            selfStartMhz,
            selfEndMhz);

        var distanceFactor = 1.5;
        if (distanceMeters > 100) distanceFactor = 2.0;
        if (distanceMeters > 500) distanceFactor = 2.5;

        var approximateCouplingLoss = 20 * distanceFactor * Math.Log10(distanceMeters / wavelengthMeters) * 0.5;

        var offAxisFactor = Math.Abs(azimuthDeg) > 60 ? 15 : 5;

        var isolationDb = freeSpacePathLoss + approximateCouplingLoss +
                          offAxisFactor - 15 - frequencyOverlap * 5;

        _logger.LogDebug("Using approximate isolation calculation for distance {Distance}m: {Isolation:F1}dB",
            distanceMeters, isolationDb);

        return Math.Clamp(isolationDb, 0, 100);
    }

    private static double CalculateMutualCouplingLoss(
        double distanceMeters,
        double azimuthDeg,
        double elevationDeg,
        double heightOffsetMeters,
        double frequencyMhz)
    {
        var wavelengthMeters = 299.792458 / frequencyMhz;
        var normalizedDistance = distanceMeters / wavelengthMeters;

        if (normalizedDistance < 1.0)
        {
            return 20 * Math.Log10(1.0 / normalizedDistance) * 0.5;
        }

        if (normalizedDistance > 10.0)
        {
            return 30 * Math.Log10(normalizedDistance) + 10;
        }

        var heightFactor = Math.Abs(heightOffsetMeters) / (wavelengthMeters * 2);
        var couplingLoss = 30 * Math.Log10(normalizedDistance) + 20 * heightFactor;

        return couplingLoss;
    }

    private Task<CoSiteInterferenceResult> AnalyzeSingleInterferenceAsync(
        CoSiteInterferenceRequest request,
        CoSiteAntenna interferingAntenna,
        CancellationToken stoppingToken)
    {
        var isolationDb = CalculateIsolation(
            interferingAntenna.SeparationDistanceMeters,
            interferingAntenna.AzimuthAngleDeg,
            interferingAntenna.ElevationAngleDeg,
            interferingAntenna.HeightOffsetMeters,
            interferingAntenna.FrequencyStartMhz,
            interferingAntenna.FrequencyEndMhz,
            request.SelfFrequencyStartMhz,
            request.SelfFrequencyEndMhz,
            interferingAntenna.TransmitPowerDbm);

        var couplingCoefficient = CalculateCouplingCoefficient(
            isolationDb,
            interferingAntenna.FrequencyStartMhz,
            interferingAntenna.FrequencyEndMhz,
            request.SelfFrequencyStartMhz,
            request.SelfFrequencyEndMhz);

        var interferenceMarginDb = isolationDb - _options.IsolationThresholdDb;
        var isIsolationSufficient = isolationDb >= _options.IsolationThresholdDb;

        var (vectorX, vectorY, vectorZ) = CalculateInterferenceVector(
            interferingAntenna.SeparationDistanceMeters,
            interferingAntenna.AzimuthAngleDeg,
            interferingAntenna.ElevationAngleDeg);

        var recommendation = GenerateRecommendation(
            isolationDb,
            interferenceMarginDb,
            interferingAntenna,
            request.SelfFrequencyStartMhz,
            request.SelfFrequencyEndMhz);

        var affectedBand = CalculateAffectedBand(
            interferingAntenna.FrequencyStartMhz,
            interferingAntenna.FrequencyEndMhz,
            request.SelfFrequencyStartMhz,
            request.SelfFrequencyEndMhz);

        return Task.FromResult(new CoSiteInterferenceResult
        {
            StationId = request.StationId,
            InterferingAntennaId = interferingAntenna.Id,
            InterferingOperator = interferingAntenna.OperatorName,
            IsolationDb = Math.Round(isolationDb, 4),
            CouplingCoefficient = Math.Round(couplingCoefficient, 9),
            InterferenceMarginDb = Math.Round(interferenceMarginDb, 4),
            IsIsolationSufficient = isIsolationSufficient,
            Recommendation = recommendation,
            VectorX = Math.Round(vectorX, 6),
            VectorY = Math.Round(vectorY, 6),
            VectorZ = Math.Round(vectorZ, 6),
            IsApproximated = interferingAntenna.IsApproximated
        });
    }

    private static double CalculatePolarizationLoss(double elevationDeg)
    {
        var elevationRad = elevationDeg * Math.PI / 180.0;
        var polarizationLoss = 20 * Math.Log10(Math.Cos(elevationRad) + 0.1);
        return Math.Max(polarizationLoss, 0);
    }

    private static double CalculateFrequencyOverlap(
        double start1, double end1,
        double start2, double end2)
    {
        var overlapStart = Math.Max(start1, start2);
        var overlapEnd = Math.Min(end1, end2);

        if (overlapEnd <= overlapStart) return 0.0;

        var overlap = overlapEnd - overlapStart;
        var minBandwidth = Math.Min(end1 - start1, end2 - start2);

        return overlap / minBandwidth;
    }

    private static double CalculateOffAxisAttenuation(double azimuthDeg, double elevationDeg)
    {
        var azimuthRad = azimuthDeg * Math.PI / 180.0;
        var elevationRad = elevationDeg * Math.PI / 180.0;

        var offAxisAngle = Math.Sqrt(
            Math.Pow(azimuthRad, 2) +
            Math.Pow(elevationRad, 2));

        var attenuation = 12 * Math.Pow(offAxisAngle / (Math.PI / 3), 2);
        return attenuation;
    }

    private double CalculateCouplingCoefficient(
        double isolationDb,
        double interfererStartMhz,
        double interfererEndMhz,
        double selfStartMhz,
        double selfEndMhz)
    {
        var couplingLinear = Math.Pow(10, -isolationDb / 20.0);

        var frequencyOverlap = CalculateFrequencyOverlap(
            interfererStartMhz, interfererEndMhz,
            selfStartMhz, selfEndMhz);

        var couplingCoefficient = couplingLinear * (0.8 + 0.2 * frequencyOverlap) *
                                  _options.CouplingModelAccuracy;

        return couplingCoefficient;
    }

    private static (double x, double y, double z) CalculateInterferenceVector(
        double distanceMeters,
        double azimuthDeg,
        double elevationDeg)
    {
        var azimuthRad = azimuthDeg * Math.PI / 180.0;
        var elevationRad = elevationDeg * Math.PI / 180.0;

        var x = distanceMeters * Math.Cos(elevationRad) * Math.Cos(azimuthRad);
        var y = distanceMeters * Math.Cos(elevationRad) * Math.Sin(azimuthRad);
        var z = distanceMeters * Math.Sin(elevationRad);

        var magnitude = Math.Sqrt(x * x + y * y + z * z);
        if (magnitude > 0)
        {
            x /= magnitude;
            y /= magnitude;
            z /= magnitude;
        }

        return (x, y, z);
    }

    private string GenerateRecommendation(
        double isolationDb,
        double marginDb,
        CoSiteAntenna antenna,
        double selfStartMhz,
        double selfEndMhz)
    {
        var recommendations = new List<string>();

        if (marginDb >= 5)
        {
            return "隔离度充足，无需额外措施";
        }

        if (marginDb >= 0)
        {
            recommendations.Add("建议加强监控，定期复测隔离度");
        }
        else
        {
            var deficit = Math.Abs(marginDb);

            if (antenna.SeparationDistanceMeters < 3.0)
            {
                recommendations.Add($"建议增加天线间距，当前{antenna.SeparationDistanceMeters:F1}m，建议≥3.0m（可提升约{Math.Round(deficit * 0.6, 1)}dB）");
            }

            var frequencyOverlap = CalculateFrequencyOverlap(
                antenna.FrequencyStartMhz, antenna.FrequencyEndMhz,
                selfStartMhz, selfEndMhz);

            if (frequencyOverlap > _options.FrequencyOverlapThreshold)
            {
                recommendations.Add($"频率重叠度{frequencyOverlap * 100:F1}%，建议协调频率规划或增加滤波器（可提升约{Math.Round(deficit * 0.4, 1)}dB）");
            }

            if (Math.Abs(antenna.HeightOffsetMeters) < 1.0)
            {
                recommendations.Add($"建议增加垂直隔离，当前高差{antenna.HeightOffsetMeters:F1}m，建议≥1.0m（可提升约{Math.Round(deficit * 0.3, 1)}dB）");
            }

            if (antenna.AzimuthAngleDeg > -30 && antenna.AzimuthAngleDeg < 30)
            {
                recommendations.Add($"建议调整天线方位角，当前{antenna.AzimuthAngleDeg:F1}°，建议背对主瓣方向（可提升约{Math.Round(deficit * 0.5, 1)}dB）");
            }

            recommendations.Add("建议安装高隔离度滤波器或使用杂散抑制装置");

            if (deficit > 10)
            {
                recommendations.Add("⚠️ 严重干扰风险，建议立即采取整改措施");
            }
        }

        return string.Join("；", recommendations);
    }

    private static (double start, double end) CalculateAffectedBand(
        double interfererStart, double interfererEnd,
        double selfStart, double selfEnd)
    {
        var overlapStart = Math.Max(interfererStart, selfStart);
        var overlapEnd = Math.Min(interfererEnd, selfEnd);

        if (overlapEnd <= overlapStart)
        {
            return (selfStart, selfEnd);
        }

        var extension = (interfererEnd - interfererStart) * 0.1;
        return (Math.Max(selfStart, overlapStart - extension),
                Math.Min(selfEnd, overlapEnd + extension));
    }

    public async Task<CoSiteInterferenceRecord> SaveInterferenceRecordAsync(
        Guid stationId,
        CoSiteAntenna interferingAntenna,
        CoSiteInterferenceResult result,
        CancellationToken stoppingToken)
    {
        var affectedBand = CalculateAffectedBand(
            interferingAntenna.FrequencyStartMhz,
            interferingAntenna.FrequencyEndMhz,
            3400, 3600);

        var record = new CoSiteInterferenceRecord
        {
            Id = Guid.NewGuid(),
            StationId = stationId,
            InterferingOperator = interferingAntenna.OperatorName,
            InterferingAntennaType = interferingAntenna.AntennaType,
            InterferingFrequencyMhz = (interferingAntenna.FrequencyStartMhz + interferingAntenna.FrequencyEndMhz) / 2,
            InterferingPowerDbm = interferingAntenna.TransmitPowerDbm,
            SeparationDistanceMeters = interferingAntenna.SeparationDistanceMeters,
            AzimuthAngleDeg = interferingAntenna.AzimuthAngleDeg,
            ElevationAngleDeg = interferingAntenna.ElevationAngleDeg,
            IsolationDb = result.IsolationDb,
            CouplingCoefficient = result.CouplingCoefficient,
            InterferenceMarginDb = result.InterferenceMarginDb,
            IsIsolationSufficient = result.IsIsolationSufficient,
            Recommendation = result.Recommendation,
            InterferenceVectorX = result.VectorX,
            InterferenceVectorY = result.VectorY,
            InterferenceVectorZ = result.VectorZ,
            AffectedBandStartMhz = affectedBand.start,
            AffectedBandEndMhz = affectedBand.end,
            MeasurementTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        return await _interferenceRepo.AddAsync(record, stoppingToken);
    }

    public Task<IReadOnlyList<CoSiteInterferenceRecord>> GetInterferenceHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken)
    {
        return _interferenceRepo.GetByStationIdAndTimeRangeAsync(stationId, startTime, endTime, stoppingToken);
    }

    public async Task<IReadOnlyList<CoSiteAntenna>> GetCoSiteAntennasAsync(
        Guid stationId,
        CancellationToken stoppingToken)
    {
        var antennas = await _cositeAntennaRepo.GetByStationIdAsync(stationId, stoppingToken);
        return antennas.Select(a => new CoSiteAntenna
        {
            Id = a.Id,
            OperatorName = a.OperatorName,
            AntennaType = a.AntennaType,
            FrequencyStartMhz = a.FrequencyBandStartMhz,
            FrequencyEndMhz = a.FrequencyBandEndMhz,
            TransmitPowerDbm = a.TransmitPowerDbm,
            SeparationDistanceMeters = a.SeparationDistanceMeters,
            AzimuthAngleDeg = a.AzimuthAngleDeg,
            ElevationAngleDeg = a.ElevationAngleDeg,
            HeightOffsetMeters = a.HeightOffsetMeters
        }).ToList().AsReadOnly();
    }
}
