using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MathNet.Numerics.LinearAlgebra;

namespace AntennaMonitoring.Modules.DeformationMonitor;

public class DeformationMonitor : IDeformationMonitor
{
    private readonly ILogger<DeformationMonitor> _logger;
    private readonly IDeformationRecordRepository _deformationRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IMediator _mediator;
    private readonly DeformationOptions _options;

    public DeformationMonitor(
        ILogger<DeformationMonitor> logger,
        IDeformationRecordRepository deformationRepo,
        IChannelRepository channelRepo,
        IMediator mediator,
        IOptions<DeformationOptions> options)
    {
        _logger = logger;
        _deformationRepo = deformationRepo;
        _channelRepo = channelRepo;
        _mediator = mediator;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<DeformationResult>> RunDeformationAnalysisAsync(
        DeformationRequest request,
        CancellationToken stoppingToken)
    {
        _logger.LogDebug("Running deformation analysis for station {StationId}", request.StationId);

        var results = new List<DeformationResult>();
        var processedSensorData = PreprocessSensorData(request.SensorData);

        foreach (var sensorData in processedSensorData)
        {
            try
            {
                var result = await AnalyzeSensorDataAsync(request.StationId, sensorData, request.Channels, stoppingToken);
                results.Add(result);

                if (result.ExceedsThreshold && _options.AutoBeamCorrection)
                {
                    await ApplyBeamCorrectionAsync(
                        request.StationId,
                        result.CorrectionAngleAzimuth,
                        result.CorrectionAngleElevation,
                        stoppingToken);

                    result.CorrectionApplied = true;

                    await _mediator.Publish(new DeformationThresholdExceededEvent(
                        request.StationId,
                        sensorData.SensorIndex,
                        result.DeformationZone,
                        result.CalculatedDisplacementMm,
                        result.CorrectionAngleAzimuth,
                        result.CorrectionAngleElevation,
                        DateTime.UtcNow), stoppingToken);
                }

                await SaveDeformationRecordAsync(request.StationId, sensorData, result, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing sensor {SensorIndex} for station {StationId}",
                    sensorData.SensorIndex, request.StationId);
            }
        }

        await _mediator.Publish(new DeformationCompletedEvent(
            request.StationId,
            results.AsReadOnly(),
            DateTime.UtcNow), stoppingToken);

        return results.AsReadOnly();
    }

    private IReadOnlyList<SensorData> PreprocessSensorData(IReadOnlyList<SensorData> sensorDataList)
    {
        var processedData = new List<SensorData>(sensorDataList);

        DetectAndFlagAnomalies(processedData);
        InterpolateMissingData(processedData);

        var anomalyCount = processedData.Count(d => d.IsAnomaly);
        var interpolatedCount = processedData.Count(d => d.IsInterpolated);

        if (anomalyCount > 0)
        {
            _logger.LogWarning("Detected {AnomalyCount} anomalous sensor readings out of {TotalCount}",
                anomalyCount, sensorDataList.Count);
        }

        if (interpolatedCount > 0)
        {
            _logger.LogInformation("Interpolated {InterpolatedCount} missing sensor readings",
                interpolatedCount);
        }

        return processedData.AsReadOnly();
    }

    private void DetectAndFlagAnomalies(List<SensorData> sensorDataList)
    {
        if (sensorDataList.Count < 3) return;

        var validTiltX = sensorDataList.Where(d => !double.IsNaN(d.TiltAngleX)).Select(d => d.TiltAngleX).ToList();
        var validTiltY = sensorDataList.Where(d => !double.IsNaN(d.TiltAngleY)).Select(d => d.TiltAngleY).ToList();
        var validStrain = sensorDataList.Where(d => !double.IsNaN(d.StrainValue)).Select(d => d.StrainValue).ToList();

        if (validTiltX.Count < 3 || validStrain.Count < 3) return;

        var tiltXStats = (Mean: validTiltX.Average(), Std: CalculateStdDev(validTiltX));
        var tiltYStats = (Mean: validTiltY.Average(), Std: CalculateStdDev(validTiltY));
        var strainStats = (Mean: validStrain.Average(), Std: CalculateStdDev(validStrain));

        const double zScoreThreshold = 3.0;
        const double maxPhysicalTilt = 15.0;
        const double maxPhysicalStrain = 5000e-6;
        const double maxPhysicalWindSpeed = 50.0;

        for (int i = 0; i < sensorDataList.Count; i++)
        {
            var data = sensorDataList[i];
            var isAnomaly = false;

            if (double.IsNaN(data.TiltAngleX) || double.IsInfinity(data.TiltAngleX) ||
                double.IsNaN(data.TiltAngleY) || double.IsInfinity(data.TiltAngleY) ||
                double.IsNaN(data.TiltAngleZ) || double.IsInfinity(data.TiltAngleZ) ||
                double.IsNaN(data.StrainValue) || double.IsInfinity(data.StrainValue) ||
                double.IsNaN(data.WindSpeed) || double.IsInfinity(data.WindSpeed))
            {
                isAnomaly = true;
            }

            if (Math.Abs(data.TiltAngleX - tiltXStats.Mean) > zScoreThreshold * tiltXStats.Std ||
                Math.Abs(data.TiltAngleX) > maxPhysicalTilt)
            {
                isAnomaly = true;
            }

            if (Math.Abs(data.TiltAngleY - tiltYStats.Mean) > zScoreThreshold * tiltYStats.Std ||
                Math.Abs(data.TiltAngleY) > maxPhysicalTilt)
            {
                isAnomaly = true;
            }

            if (Math.Abs(data.StrainValue - strainStats.Mean) > zScoreThreshold * strainStats.Std ||
                Math.Abs(data.StrainValue) > maxPhysicalStrain)
            {
                isAnomaly = true;
            }

            if (data.WindSpeed < 0 || data.WindSpeed > maxPhysicalWindSpeed)
            {
                isAnomaly = true;
            }

            if (data.Temperature < -40 || data.Temperature > 125)
            {
                isAnomaly = true;
            }

            if (isAnomaly)
            {
                sensorDataList[i] = data with { IsAnomaly = true };
            }
        }
    }

    private void InterpolateMissingData(List<SensorData> sensorDataList)
    {
        const int gridSize = 3;
        const int totalSensors = gridSize * gridSize;

        for (int i = 0; i < sensorDataList.Count; i++)
        {
            if (!sensorDataList[i].IsAnomaly) continue;

            var sensorIndex = sensorDataList[i].SensorIndex;
            var row = sensorIndex / gridSize;
            var col = sensorIndex % gridSize;

            var neighbors = new List<(int Row, int Col, double Weight)>();

            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;

                    var neighborRow = row + dr;
                    var neighborCol = col + dc;

                    if (neighborRow >= 0 && neighborRow < gridSize &&
                        neighborCol >= 0 && neighborCol < gridSize)
                    {
                        var neighborIndex = neighborRow * gridSize + neighborCol;
                        var neighbor = sensorDataList.FirstOrDefault(s => s.SensorIndex == neighborIndex);

                        if (neighbor != null && !neighbor.IsAnomaly && !neighbor.IsInterpolated)
                        {
                            var distance = Math.Sqrt(dr * dr + dc * dc);
                            neighbors.Add((neighborRow, neighborCol, 1.0 / distance));
                        }
                    }
                }
            }

            if (neighbors.Count >= 2)
            {
                var totalWeight = neighbors.Sum(n => n.Weight);

                var interpolatedTiltX = neighbors.Sum(n =>
                {
                    var neighbor = sensorDataList.First(s =>
                        s.SensorIndex == n.Row * gridSize + n.Col);
                    return n.Weight * neighbor.TiltAngleX;
                }) / totalWeight;

                var interpolatedTiltY = neighbors.Sum(n =>
                {
                    var neighbor = sensorDataList.First(s =>
                        s.SensorIndex == n.Row * gridSize + n.Col);
                    return n.Weight * neighbor.TiltAngleY;
                }) / totalWeight;

                var interpolatedTiltZ = neighbors.Sum(n =>
                {
                    var neighbor = sensorDataList.First(s =>
                        s.SensorIndex == n.Row * gridSize + n.Col);
                    return n.Weight * neighbor.TiltAngleZ;
                }) / totalWeight;

                var interpolatedStrain = neighbors.Sum(n =>
                {
                    var neighbor = sensorDataList.First(s =>
                        s.SensorIndex == n.Row * gridSize + n.Col);
                    return n.Weight * neighbor.StrainValue;
                }) / totalWeight;

                var interpolatedTemp = neighbors.Sum(n =>
                {
                    var neighbor = sensorDataList.First(s =>
                        s.SensorIndex == n.Row * gridSize + n.Col);
                    return n.Weight * neighbor.Temperature;
                }) / totalWeight;

                var interpolatedWind = neighbors.Sum(n =>
                {
                    var neighbor = sensorDataList.First(s =>
                        s.SensorIndex == n.Row * gridSize + n.Col);
                    return n.Weight * neighbor.WindSpeed;
                }) / totalWeight;

                sensorDataList[i] = sensorDataList[i] with
                {
                    TiltAngleX = interpolatedTiltX,
                    TiltAngleY = interpolatedTiltY,
                    TiltAngleZ = interpolatedTiltZ,
                    StrainValue = interpolatedStrain,
                    Temperature = interpolatedTemp,
                    WindSpeed = interpolatedWind,
                    IsAnomaly = false,
                    IsInterpolated = true
                };
            }
            else if (neighbors.Count == 1)
            {
                var neighborIndex = neighbors[0].Row * gridSize + neighbors[0].Col;
                var neighbor = sensorDataList.First(s => s.SensorIndex == neighborIndex);

                sensorDataList[i] = sensorDataList[i] with
                {
                    TiltAngleX = neighbor.TiltAngleX,
                    TiltAngleY = neighbor.TiltAngleY,
                    TiltAngleZ = neighbor.TiltAngleZ,
                    StrainValue = neighbor.StrainValue,
                    Temperature = neighbor.Temperature,
                    WindSpeed = neighbor.WindSpeed,
                    IsAnomaly = false,
                    IsInterpolated = true
                };
            }
            else
            {
                var validData = sensorDataList.Where(d => !d.IsAnomaly).ToList();
                if (validData.Any())
                {
                    sensorDataList[i] = sensorDataList[i] with
                    {
                        TiltAngleX = validData.Average(d => d.TiltAngleX),
                        TiltAngleY = validData.Average(d => d.TiltAngleY),
                        TiltAngleZ = validData.Average(d => d.TiltAngleZ),
                        StrainValue = validData.Average(d => d.StrainValue),
                        Temperature = validData.Average(d => d.Temperature),
                        WindSpeed = validData.Average(d => d.WindSpeed),
                        IsAnomaly = false,
                        IsInterpolated = true
                    };
                }
            }
        }
    }

    private static double CalculateStdDev(IEnumerable<double> values)
    {
        if (values.Count() < 2) return 0;

        var mean = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count() - 1));
    }

    private Task<DeformationResult> AnalyzeSensorDataAsync(
        Guid stationId,
        SensorData sensorData,
        IReadOnlyList<Channel> channels,
        CancellationToken stoppingToken)
    {
        var youngModulusPa = _options.YoungModulusGpa * 1e9;
        var poissonRatio = _options.PoissonRatio;
        var plateThicknessM = _options.PlateThicknessMm / 1000.0;

        var flexuralRigidity = (youngModulusPa * Math.Pow(plateThicknessM, 3)) /
                               (12 * (1 - Math.Pow(poissonRatio, 2)));

        var tiltMagnitude = Math.Sqrt(
            Math.Pow(sensorData.TiltAngleX, 2) +
            Math.Pow(sensorData.TiltAngleY, 2) +
            Math.Pow(sensorData.TiltAngleZ, 2));

        var sensorPosition = GetSensorPosition(sensorData.SensorIndex);
        var displacementMm = CalculateDisplacementFEM(
            tiltMagnitude,
            sensorData.StrainValue,
            sensorPosition.x,
            sensorPosition.y,
            flexuralRigidity,
            plateThicknessM,
            sensorData.WindSpeed);

        var stressMpa = CalculateStress(
            sensorData.StrainValue,
            youngModulusPa,
            plateThicknessM,
            sensorPosition.x,
            sensorPosition.y);

        var deformationZone = GetDeformationZone(displacementMm, sensorPosition);

        var exceedsThreshold = displacementMm > _options.ThresholdMm;

        double correctionAzimuth = 0;
        double correctionElevation = 0;

        if (exceedsThreshold)
        {
            correctionAzimuth = CalculateBeamCorrectionAzimuth(
                sensorData.TiltAngleX,
                sensorData.TiltAngleY,
                displacementMm);

            correctionElevation = CalculateBeamCorrectionElevation(
                sensorData.TiltAngleZ,
                displacementMm);
        }

        return Task.FromResult(new DeformationResult
        {
            StationId = stationId,
            SensorIndex = sensorData.SensorIndex,
            CalculatedDisplacementMm = Math.Round(displacementMm, 6),
            StressMpa = Math.Round(stressMpa, 4),
            DeformationZone = deformationZone,
            ExceedsThreshold = exceedsThreshold,
            CorrectionAngleAzimuth = Math.Round(correctionAzimuth, 6),
            CorrectionAngleElevation = Math.Round(correctionElevation, 6),
            CorrectionApplied = false,
            TiltAngleX = sensorData.TiltAngleX,
            TiltAngleY = sensorData.TiltAngleY,
            TiltAngleZ = sensorData.TiltAngleZ,
            StrainValue = sensorData.StrainValue,
            IsInterpolated = sensorData.IsInterpolated,
            IsAnomaly = sensorData.IsAnomaly
        });
    }

    private static (double x, double y) GetSensorPosition(int sensorIndex)
    {
        var gridSize = 3;
        var row = sensorIndex / gridSize;
        var col = sensorIndex % gridSize;

        var x = (col - 1) * 0.5;
        var y = (row - 1) * 0.5;

        return (x, y);
    }

    private double CalculateDisplacementFEM(
        double tiltMagnitude,
        double strainValue,
        double x,
        double y,
        double flexuralRigidity,
        double plateThickness,
        double windSpeed)
    {
        var tiltRadians = tiltMagnitude * Math.PI / 180.0;

        var loadFromTilt = Math.Abs(tiltRadians) * flexuralRigidity / 100.0;
        var windPressure = 0.613 * windSpeed * windSpeed;
        var windLoad = windPressure * 0.8;
        var totalLoad = loadFromTilt + windLoad;

        var a = 1.0;
        var b = 1.0;
        var r = Math.Sqrt(x * x + y * y);
        var maxR = Math.Sqrt(a * a + b * b);
        var normalizedR = r / maxR;

        var shapeFunction = (1 - normalizedR) * (1 - normalizedR);

        var strainDisplacement = strainValue * plateThickness * 1000.0;
        var loadDisplacement = (totalLoad / flexuralRigidity) * shapeFunction * 1000.0;
        var displacementMm = Math.Abs(strainDisplacement) + Math.Abs(loadDisplacement);

        var maxDisplacement = 10.0;
        return Math.Min(displacementMm, maxDisplacement);
    }

    private static double CalculateStress(
        double strainValue,
        double youngModulusPa,
        double plateThickness,
        double x,
        double y)
    {
        var stressFromStrain = Math.Abs(strainValue) * youngModulusPa;

        var r = Math.Sqrt(x * x + y * y);
        var stressConcentration = 1.0 + 0.5 * r;

        var stressPa = stressFromStrain * stressConcentration;
        var stressMpa = stressPa / 1e6;

        var yieldStrengthMpa = 200.0;
        return Math.Min(stressMpa, yieldStrengthMpa * 0.9);
    }

    private static string GetDeformationZone(double displacementMm, (double x, double y) position)
    {
        if (displacementMm < 0.1) return "none";

        var zone = string.Empty;

        if (position.y > 0.2) zone += "top-";
        else if (position.y < -0.2) zone += "bottom-";

        if (position.x > 0.2) zone += "right";
        else if (position.x < -0.2) zone += "left";
        else zone += "center";

        if (zone == "center") zone = "center";
        else zone = zone.Trim('-');

        if (displacementMm > 2.0) zone += "-critical";
        else if (displacementMm > 1.0) zone += "-major";
        else zone += "-minor";

        return zone;
    }

    private static double CalculateBeamCorrectionAzimuth(double tiltX, double tiltY, double displacementMm)
    {
        var tiltHorizontal = Math.Sqrt(tiltX * tiltX + tiltY * tiltY);
        var tiltDirection = Math.Atan2(tiltY, tiltX);

        var correctionAngle = tiltHorizontal * 0.8;

        var maxCorrection = 2.0;
        return Math.Sign(tiltDirection) * Math.Min(correctionAngle, maxCorrection);
    }

    private static double CalculateBeamCorrectionElevation(double tiltZ, double displacementMm)
    {
        var correctionAngle = -tiltZ * 0.8;
        var maxCorrection = 2.0;
        return Math.Clamp(correctionAngle, -maxCorrection, maxCorrection);
    }

    public async Task<DeformationRecord> SaveDeformationRecordAsync(
        Guid stationId,
        SensorData sensorData,
        DeformationResult result,
        CancellationToken stoppingToken)
    {
        var record = new DeformationRecord
        {
            Id = Guid.NewGuid(),
            StationId = stationId,
            SensorIndex = sensorData.SensorIndex,
            TiltAngleX = sensorData.TiltAngleX,
            TiltAngleY = sensorData.TiltAngleY,
            TiltAngleZ = sensorData.TiltAngleZ,
            StrainValue = sensorData.StrainValue,
            Temperature = sensorData.Temperature,
            CalculatedDisplacementMm = result.CalculatedDisplacementMm,
            StressMpa = result.StressMpa,
            DeformationZone = result.DeformationZone,
            BeamCorrectionApplied = result.CorrectionApplied,
            CorrectionAngleAzimuth = result.CorrectionAngleAzimuth,
            CorrectionAngleElevation = result.CorrectionAngleElevation,
            WindSpeed = sensorData.WindSpeed,
            MeasurementTime = sensorData.Timestamp,
            CreatedAt = DateTime.UtcNow
        };

        return await _deformationRepo.AddAsync(record, stoppingToken);
    }

    public async Task ApplyBeamCorrectionAsync(
        Guid stationId,
        double correctionAzimuth,
        double correctionElevation,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Applying beam correction for station {StationId}: Azimuth={Azimuth:F4}°, Elevation={Elevation:F4}°",
            stationId, correctionAzimuth, correctionElevation);

        var channels = await _channelRepo.GetByStationIdAsync(stationId, stoppingToken);

        foreach (var channel in channels)
        {
            var currentPhase = channel.CalibrationCoeffPhase ?? 0.0;
            var phaseCorrection = CalculatePhaseCorrection(
                channel, correctionAzimuth, correctionElevation);
            channel.CalibrationCoeffPhase = currentPhase + phaseCorrection;
            channel.UpdatedAt = DateTime.UtcNow;
        }

        await _channelRepo.BulkUpdateAsync(channels, stoppingToken);

        _logger.LogInformation("Beam correction applied successfully for {ChannelCount} channels",
            channels.Count);
    }

    private static double CalculatePhaseCorrection(
        Channel channel,
        double correctionAzimuthDeg,
        double correctionElevationDeg)
    {
        const double wavelength = 0.0857;
        const double elementSpacing = wavelength / 2.0;

        var azimuthRad = correctionAzimuthDeg * Math.PI / 180.0;
        var elevationRad = correctionElevationDeg * Math.PI / 180.0;

        var dx = channel.ColumnIndex * elementSpacing;
        var dy = channel.RowIndex * elementSpacing;

        var pathDifference =
            dx * Math.Sin(elevationRad) * Math.Cos(azimuthRad) +
            dy * Math.Sin(elevationRad) * Math.Sin(azimuthRad);

        var phaseCorrection = -2 * Math.PI * pathDifference / wavelength;

        return phaseCorrection;
    }

    public Task<IReadOnlyList<DeformationRecord>> GetDeformationHistoryAsync(
        Guid stationId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken stoppingToken)
    {
        return _deformationRepo.GetByStationIdAndTimeRangeAsync(stationId, startTime, endTime, stoppingToken);
    }
}
