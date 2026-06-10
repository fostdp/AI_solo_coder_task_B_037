using CoSiteInterference.Module.Models;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CoSiteInterference.Module.Workers;

public record CouplingCalculationRequest
{
    public required CoSiteAntenna AntennaA { get; init; }
    public required CoSiteAntenna AntennaB { get; init; }
    public double SelfStartMhz { get; init; }
    public double SelfEndMhz { get; init; }
    public bool UsePcaReduction { get; init; } = true;
    public bool UseCache { get; init; } = true;
}

public record CouplingCalculationResult
{
    public double IsolationDb { get; init; }
    public double CouplingCoefficient { get; init; }
    public bool IsApproximated { get; init; }
    public bool IsFromCache { get; init; }
    public TimeSpan CalculationTime { get; init; }
}

public class CouplingMatrixWorker : BackgroundService
{
    private readonly ILogger<CouplingMatrixWorker> _logger;
    private readonly CoSiteInterferenceOptions _options;
    private readonly Channel<CouplingCalculationRequest> _channel;
    private readonly ConcurrentDictionary<string, CouplingCalculationResult> _cache;
    private readonly ConcurrentQueue<string> _cacheKeys;
    private readonly object _cacheLock = new();

    public CouplingMatrixWorker(
        ILogger<CouplingMatrixWorker> logger,
        IOptions<CoSiteInterferenceOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _channel = Channel.CreateUnbounded<CouplingCalculationRequest>();
        _cache = new ConcurrentDictionary<string, CouplingCalculationResult>();
        _cacheKeys = new ConcurrentQueue<string>();
    }

    public ChannelWriter<CouplingCalculationRequest> Writer => _channel.Writer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Coupling Matrix Worker started. PCA Dimensions={PcaDims}, Cache Capacity={CacheCapacity}",
            _options.PcaDimensions, _options.CacheCapacity);

        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessCalculationRequestAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing coupling calculation request");
            }
        }
    }

    public async Task<double> CalculateIsolationAsync(CouplingCalculationRequest request, CancellationToken stoppingToken)
    {
        var result = await CalculateAsync(request, stoppingToken);
        return result.IsolationDb;
    }

    public async Task<CouplingCalculationResult> CalculateAsync(CouplingCalculationRequest request, CancellationToken stoppingToken)
    {
        var startTime = DateTime.UtcNow;
        var cacheKey = GenerateCacheKey(request);

        if (request.UseCache && _cache.TryGetValue(cacheKey, out var cachedResult))
        {
            _logger.LogDebug("Cache hit for coupling calculation: {Key}", cacheKey);
            return cachedResult with { IsFromCache = true, CalculationTime = DateTime.UtcNow - startTime };
        }

        CouplingCalculationResult result;

        if (request.AntennaA.SeparationDistanceMeters > _options.FastCalculationDistanceThresholdMeters)
        {
            result = CalculateFastPath(request);
        }
        else if (request.UsePcaReduction)
        {
            result = CalculateWithPca(request);
        }
        else
        {
            result = CalculateFull(request);
        }

        result = result with { CalculationTime = DateTime.UtcNow - startTime };

        if (request.UseCache)
        {
            AddToCache(cacheKey, result);
        }

        return result;
    }

    private Task ProcessCalculationRequestAsync(CouplingCalculationRequest request, CancellationToken stoppingToken)
    {
        return CalculateAsync(request, stoppingToken);
    }

    private CouplingCalculationResult CalculateFastPath(CouplingCalculationRequest request)
    {
        var frequencyMhz = (request.AntennaA.FrequencyStartMhz + request.AntennaA.FrequencyEndMhz) / 2.0;
        var distance = CoSiteInterferenceAnalyzer.CalculateAntennaDistance(request.AntennaA, request.AntennaB);
        var azimuthDiff = request.AntennaA.AzimuthAngleDeg - request.AntennaB.AzimuthAngleDeg;

        var wavelengthMeters = 299.792458 / frequencyMhz;
        var freeSpacePathLoss = 20 * Math.Log10(4 * Math.PI * distance / wavelengthMeters);

        var frequencyOverlap = CoSiteInterferenceAnalyzer.CalculateFrequencyOverlap(
            request.AntennaA.FrequencyStartMhz, request.AntennaA.FrequencyEndMhz,
            request.SelfStartMhz, request.SelfEndMhz);

        var distanceFactor = 1.5;
        if (distance > 100) distanceFactor = 2.0;
        if (distance > 500) distanceFactor = 2.5;

        var approximateCouplingLoss = 20 * distanceFactor * Math.Log10(distance / wavelengthMeters) * 0.5;
        var offAxisFactor = Math.Abs(azimuthDiff) > 60 ? 15 : 5;

        var isolationDb = freeSpacePathLoss + approximateCouplingLoss + offAxisFactor - 15 - frequencyOverlap * 5;
        isolationDb = Math.Clamp(isolationDb, 0, 100);

        var couplingCoefficient = Math.Pow(10, -isolationDb / 20.0) * (0.8 + 0.2 * frequencyOverlap) * _options.CouplingModelAccuracy;

        _logger.LogDebug("Fast path coupling calculation: distance={Distance:F1}m, isolation={Isolation:F1}dB",
            distance, isolationDb);

        return new CouplingCalculationResult
        {
            IsolationDb = isolationDb,
            CouplingCoefficient = couplingCoefficient,
            IsApproximated = true,
            IsFromCache = false
        };
    }

    private CouplingCalculationResult CalculateWithPca(CouplingCalculationRequest request)
    {
        var antennas = new[] { request.AntennaA, request.AntennaB };
        var fullMatrix = BuildCouplingMatrix(antennas);

        if (fullMatrix.RowCount <= _options.PcaDimensions)
        {
            return CalculateFull(request);
        }

        var reducedMatrix = ApplyPca(fullMatrix, _options.PcaDimensions);
        var isolationDb = ExtractIsolationFromReducedMatrix(reducedMatrix, 0, 1);

        var frequencyOverlap = CoSiteInterferenceAnalyzer.CalculateFrequencyOverlap(
            request.AntennaA.FrequencyStartMhz, request.AntennaA.FrequencyEndMhz,
            request.SelfStartMhz, request.SelfEndMhz);

        var couplingCoefficient = Math.Pow(10, -isolationDb / 20.0) * (0.8 + 0.2 * frequencyOverlap) * _options.CouplingModelAccuracy;

        _logger.LogDebug("PCA reduced coupling calculation: original dims={Original}, reduced dims={Reduced}, isolation={Isolation:F1}dB",
            fullMatrix.RowCount, reducedMatrix.RowCount, isolationDb);

        return new CouplingCalculationResult
        {
            IsolationDb = Math.Clamp(isolationDb, 0, 100),
            CouplingCoefficient = couplingCoefficient,
            IsApproximated = true,
            IsFromCache = false
        };
    }

    private CouplingCalculationResult CalculateFull(CouplingCalculationRequest request)
    {
        var distance = CoSiteInterferenceAnalyzer.CalculateAntennaDistance(request.AntennaA, request.AntennaB);
        var frequencyMhz = (request.AntennaA.FrequencyStartMhz + request.AntennaA.FrequencyEndMhz) / 2.0;

        var mutualCouplingLoss = CoSiteInterferenceAnalyzer.CalculateMutualCouplingLoss(
            distance,
            request.AntennaA.AzimuthAngleDeg - request.AntennaB.AzimuthAngleDeg,
            request.AntennaA.ElevationAngleDeg - request.AntennaB.ElevationAngleDeg,
            request.AntennaA.HeightOffsetMeters - request.AntennaB.HeightOffsetMeters,
            frequencyMhz);

        var wavelengthMeters = 299.792458 / frequencyMhz;
        var freeSpacePathLoss = 20 * Math.Log10(4 * Math.PI * distance / wavelengthMeters);

        var polarizationLoss = CoSiteInterferenceAnalyzer.CalculatePolarizationLoss(
            request.AntennaA.ElevationAngleDeg - request.AntennaB.ElevationAngleDeg);

        var frequencyOverlap = CoSiteInterferenceAnalyzer.CalculateFrequencyOverlap(
            request.AntennaA.FrequencyStartMhz, request.AntennaA.FrequencyEndMhz,
            request.SelfStartMhz, request.SelfEndMhz);

        var offAxisAttenuation = CoSiteInterferenceAnalyzer.CalculateOffAxisAttenuation(
            request.AntennaA.AzimuthAngleDeg - request.AntennaB.AzimuthAngleDeg,
            request.AntennaA.ElevationAngleDeg - request.AntennaB.ElevationAngleDeg);

        var antennaGain = 15.0;
        var isolationDb = freeSpacePathLoss + mutualCouplingLoss + polarizationLoss +
                          offAxisAttenuation - antennaGain - frequencyOverlap * 10;

        isolationDb = Math.Clamp(isolationDb, 0, 100);

        var couplingCoefficient = Math.Pow(10, -isolationDb / 20.0) * (0.8 + 0.2 * frequencyOverlap) * _options.CouplingModelAccuracy;

        _logger.LogDebug("Full coupling calculation: distance={Distance:F1}m, isolation={Isolation:F1}dB",
            distance, isolationDb);

        return new CouplingCalculationResult
        {
            IsolationDb = isolationDb,
            CouplingCoefficient = couplingCoefficient,
            IsApproximated = false,
            IsFromCache = false
        };
    }

    public Matrix<double> BuildCouplingMatrix(IReadOnlyList<CoSiteAntenna> antennas)
    {
        var n = antennas.Count;
        var matrix = Matrix<double>.Build.Dense(n, n);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    matrix[i, j] = 0;
                }
                else
                {
                    var distance = CoSiteInterferenceAnalyzer.CalculateAntennaDistance(antennas[i], antennas[j]);
                    var frequencyMhz = (antennas[i].FrequencyStartMhz + antennas[i].FrequencyEndMhz +
                                        antennas[j].FrequencyStartMhz + antennas[j].FrequencyEndMhz) / 4.0;

                    matrix[i, j] = CoSiteInterferenceAnalyzer.CalculateMutualCouplingLoss(
                        distance,
                        antennas[i].AzimuthAngleDeg - antennas[j].AzimuthAngleDeg,
                        antennas[i].ElevationAngleDeg - antennas[j].ElevationAngleDeg,
                        antennas[i].HeightOffsetMeters - antennas[j].HeightOffsetMeters,
                        frequencyMhz);
                }
            }
        }

        return matrix;
    }

    public Matrix<double> ApplyPca(Matrix<double> matrix, int targetDimensions)
    {
        if (matrix.RowCount <= targetDimensions)
        {
            return matrix;
        }

        var mean = matrix.RowSums() / matrix.RowCount;
        var centered = matrix.SubtractRowVector(mean.ToRowMatrix());

        var covariance = centered.Transpose() * centered / (matrix.RowCount - 1);

        var evd = covariance.Evd();
        var eigenvalues = evd.EigenValues.Real().ToArray();
        var eigenvectors = evd.EigenVectors;

        var sortedIndices = eigenvalues
            .Select((value, index) => new { Value = value, Index = index })
            .OrderByDescending(x => x.Value)
            .Take(targetDimensions)
            .Select(x => x.Index)
            .ToArray();

        var projectionMatrix = Matrix<double>.Build.Dense(eigenvectors.RowCount, targetDimensions);
        for (int i = 0; i < targetDimensions; i++)
        {
            projectionMatrix.SetColumn(i, eigenvectors.Column(sortedIndices[i]));
        }

        var reduced = centered * projectionMatrix;

        _logger.LogDebug("PCA reduction: {Original}x{Original} -> {Reduced}x{Reduced}, explained variance: {Variance:F2}%",
            matrix.RowCount, reduced.RowCount,
            eigenvalues.Take(targetDimensions).Sum() / eigenvalues.Sum() * 100);

        return reduced;
    }

    public double[,] ClusterAntennasByCoupling(IReadOnlyList<CoSiteAntenna> antennas, double thresholdDb = 20.0)
    {
        var couplingMatrix = BuildCouplingMatrix(antennas);
        var n = antennas.Count;
        var clusters = new int[n];
        var clusterId = 0;

        for (int i = 0; i < n; i++)
        {
            if (clusters[i] == 0)
            {
                clusterId++;
                clusters[i] = clusterId;

                for (int j = i + 1; j < n; j++)
                {
                    if (clusters[j] == 0 && couplingMatrix[i, j] < thresholdDb)
                    {
                        clusters[j] = clusterId;
                    }
                }
            }
        }

        var result = new double[n, 2];
        for (int i = 0; i < n; i++)
        {
            result[i, 0] = clusters[i];
            result[i, 1] = couplingMatrix[i, Array.IndexOf(clusters, clusters[i])];
        }

        _logger.LogDebug("Coupling-based clustering: {Count} antennas -> {Clusters} clusters",
            n, clusterId);

        return result;
    }

    private static double ExtractIsolationFromReducedMatrix(Matrix<double> reducedMatrix, int row, int col)
    {
        if (row < reducedMatrix.RowCount && col < reducedMatrix.ColumnCount)
        {
            return Math.Abs(reducedMatrix[row, col]);
        }
        return 50.0;
    }

    private static string GenerateCacheKey(CouplingCalculationRequest request)
    {
        return $"{request.AntennaA.Id:N}_{request.AntennaB.Id:N}_{request.AntennaA.SeparationDistanceMeters:F2}_{request.AntennaB.SeparationDistanceMeters:F2}_{request.SelfStartMhz}_{request.SelfEndMhz}";
    }

    private void AddToCache(string key, CouplingCalculationResult result)
    {
        lock (_cacheLock)
        {
            if (_cache.Count >= _options.CacheCapacity)
            {
                if (_cacheKeys.TryDequeue(out var oldestKey))
                {
                    _cache.TryRemove(oldestKey, out _);
                    _logger.LogDebug("Evicted oldest cache entry: {Key}", oldestKey);
                }
            }

            _cache.AddOrUpdate(key, result, (k, v) => result);
            _cacheKeys.Enqueue(key);
        }

        _logger.LogDebug("Added to cache: {Key}, cache size: {Count}", key, _cache.Count);
    }

    public bool TryGetFromCache(string key, out CouplingCalculationResult result)
    {
        return _cache.TryGetValue(key, out result!);
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
            while (_cacheKeys.TryDequeue(out _)) { }
        }

        _logger.LogInformation("Cache cleared");
    }

    public int CacheSize => _cache.Count;
}
