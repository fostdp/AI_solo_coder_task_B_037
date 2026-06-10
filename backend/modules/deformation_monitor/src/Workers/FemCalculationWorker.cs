using System.Threading.Channels;
using DeformationMonitor.Module.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeformationMonitor.Module.Workers;

public record FemCalculationRequest
{
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public int SensorIndex { get; init; }
    public double TiltMagnitude { get; init; }
    public double StrainValue { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double FlexuralRigidity { get; init; }
    public double PlateThickness { get; init; }
    public double WindSpeed { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public record FemCalculationResponse
{
    public Guid RequestId { get; init; }
    public int SensorIndex { get; init; }
    public double DisplacementMm { get; init; }
    public bool IsSuccess { get; init; }
    public Exception? Error { get; init; }
}

public class FemCalculationWorker : IDisposable
{
    private readonly Channel<FemCalculationRequest> _requestChannel;
    private readonly Channel<FemCalculationResponse> _responseChannel;
    private readonly ILogger<FemCalculationWorker> _logger;
    private readonly DeformationOptions _options;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts;
    private bool _disposed;

    public ChannelReader<FemCalculationRequest> RequestReader => _requestChannel.Reader;
    public ChannelWriter<FemCalculationRequest> RequestWriter => _requestChannel.Writer;
    public ChannelReader<FemCalculationResponse> ResponseReader => _responseChannel.Reader;

    public FemCalculationWorker(
        ILogger<FemCalculationWorker> logger,
        IOptions<DeformationOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _cts = new CancellationTokenSource();

        _requestChannel = Channel.CreateUnbounded<FemCalculationRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _responseChannel = Channel.CreateUnbounded<FemCalculationResponse>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

        _processingTask = Task.Run(() => ProcessRequestsAsync(_cts.Token), _cts.Token);
    }

    public async Task<double> CalculateDisplacementFEMAsync(
        int sensorIndex,
        double tiltMagnitude,
        double strainValue,
        double x,
        double y,
        double flexuralRigidity,
        double plateThickness,
        double windSpeed,
        CancellationToken cancellationToken = default)
    {
        var request = new FemCalculationRequest
        {
            SensorIndex = sensorIndex,
            TiltMagnitude = tiltMagnitude,
            StrainValue = strainValue,
            X = x,
            Y = y,
            FlexuralRigidity = flexuralRigidity,
            PlateThickness = plateThickness,
            WindSpeed = windSpeed,
            CancellationToken = cancellationToken
        };

        await _requestChannel.Writer.WriteAsync(request, cancellationToken);

        await foreach (var response in _responseChannel.Reader.ReadAllAsync(cancellationToken))
        {
            if (response.RequestId == request.RequestId)
            {
                if (!response.IsSuccess && response.Error != null)
                {
                    throw response.Error;
                }
                return response.DisplacementMm;
            }
        }

        throw new OperationCanceledException("FEM calculation was canceled.");
    }

    public async Task<IReadOnlyDictionary<int, double>> CalculateBatchDisplacementFEMAsync(
        IEnumerable<FemCalculationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var requestList = requests.ToList();
        var results = new Dictionary<int, double>();
        var pendingRequests = new HashSet<Guid>(requestList.Select(r => r.RequestId));

        foreach (var request in requestList)
        {
            await _requestChannel.Writer.WriteAsync(request, cancellationToken);
        }

        await foreach (var response in _responseChannel.Reader.ReadAllAsync(cancellationToken))
        {
            if (pendingRequests.Contains(response.RequestId))
            {
                if (response.IsSuccess)
                {
                    results[response.SensorIndex] = response.DisplacementMm;
                }
                pendingRequests.Remove(response.RequestId);

                if (pendingRequests.Count == 0)
                {
                    break;
                }
            }
        }

        return results.AsReadOnly();
    }

    private async Task ProcessRequestsAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FEM Calculation Worker started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var batch = new List<FemCalculationRequest>();

                while (batch.Count < 10 &&
                       _requestChannel.Reader.TryRead(out var request))
                {
                    if (!request.CancellationToken.IsCancellationRequested)
                    {
                        batch.Add(request);
                    }
                }

                if (batch.Count == 0)
                {
                    try
                    {
                        var singleRequest = await _requestChannel.Reader.ReadAsync(stoppingToken);
                        if (!singleRequest.CancellationToken.IsCancellationRequested)
                        {
                            batch.Add(singleRequest);
                        }
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    _logger.LogDebug("Processing {Count} FEM calculation requests.", batch.Count);

                    foreach (var request in batch)
                    {
                        FemCalculationResponse response;
                        try
                        {
                            var displacement = CalculateDisplacementFEM(
                                request.TiltMagnitude,
                                request.StrainValue,
                                request.X,
                                request.Y,
                                request.FlexuralRigidity,
                                request.PlateThickness,
                                request.WindSpeed);

                            response = new FemCalculationResponse
                            {
                                RequestId = request.RequestId,
                                SensorIndex = request.SensorIndex,
                                DisplacementMm = displacement,
                                IsSuccess = true
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing FEM calculation for sensor {SensorIndex}",
                                request.SensorIndex);

                            response = new FemCalculationResponse
                            {
                                RequestId = request.RequestId,
                                SensorIndex = request.SensorIndex,
                                DisplacementMm = 0,
                                IsSuccess = false,
                                Error = ex
                            };
                        }

                        await _responseChannel.Writer.WriteAsync(response, stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("FEM Calculation Worker is stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FEM Calculation Worker encountered an unexpected error.");
        }
        finally
        {
            _responseChannel.Writer.Complete();
            _logger.LogInformation("FEM Calculation Worker stopped.");
        }
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _requestChannel.Writer.Complete();
            _cts.Cancel();

            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            _cts.Dispose();
        }

        _disposed = true;
    }
}
