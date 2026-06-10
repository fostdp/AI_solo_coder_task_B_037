using System.Numerics;
using System.Threading.Channels;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Algorithms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MathNet.Numerics.IntegralTransforms;
using SpectrumScanner.Module.Models;

namespace SpectrumScanner.Module.Workers;

public class GpuFftWorker : IDisposable
{
    private readonly ILogger<GpuFftWorker> _logger;
    private readonly SpectrumScanOptions _options;
    private readonly Channel<FftCalculationRequest> _requestChannel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource;

    private Context _gpuContext;
    private Accelerator _accelerator;
    private bool _gpuAvailable;
    private bool _disposed;

    private Action<Index2D, ArrayView2D<Complex, Stride2D.DenseX>, int, int> _fftKernel;
    private Action<Index1D, ArrayView<double>, ArrayView<double>, int> _windowKernel;

    public bool IsGpuAvailable => _gpuAvailable;

    public GpuFftWorker(
        ILogger<GpuFftWorker> logger,
        IOptions<SpectrumScanOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _cancellationTokenSource = new CancellationTokenSource();
        _requestChannel = Channel.CreateUnbounded<FftCalculationRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        InitializeGpu();

        _processingTask = ProcessRequestsAsync(_cancellationTokenSource.Token);
    }

    private void InitializeGpu()
    {
        try
        {
            if (!_options.EnableGpuAcceleration)
            {
                _logger.LogInformation("GPU acceleration is disabled in configuration");
                _gpuAvailable = false;
                return;
            }

            _gpuContext = Context.Create(builder =>
            {
                builder.Default().EnableAlgorithms();
            });

            if (_gpuContext.Devices.Length == 0)
            {
                _logger.LogWarning("No GPU devices found, falling back to CPU");
                _gpuAvailable = false;
                return;
            }

            var device = _gpuContext.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda)
                         ?? _gpuContext.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.OpenCL)
                         ?? _gpuContext.Devices.First();

            _accelerator = device.CreateAccelerator(_gpuContext);

            _logger.LogInformation(
                "GPU accelerator initialized: {Name}, Type: {Type}, Memory: {Memory}MB",
                _accelerator.Name,
                _accelerator.AcceleratorType,
                _accelerator.MemorySize / (1024 * 1024));

            CompileKernels();
            _gpuAvailable = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize GPU accelerator, falling back to CPU");
            _gpuAvailable = false;
        }
    }

    private void CompileKernels()
    {
        _fftKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D,
            ArrayView2D<Complex, Stride2D.DenseX>,
            int,
            int>(FftCooleyTukeyKernel);

        _windowKernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView<double>,
            ArrayView<double>,
            int>(ApplyWindowKernel);
    }

    private static void FftCooleyTukeyKernel(
        Index2D index,
        ArrayView2D<Complex, Stride2D.DenseX> data,
        int fftSize,
        int stages)
    {
        int batchIndex = index.Y;
        int elementIndex = index.X;

        if (elementIndex >= fftSize) return;

        int j = BitReverse(elementIndex, stages);
        if (elementIndex < j)
        {
            (data[batchIndex, elementIndex], data[batchIndex, j]) =
                (data[batchIndex, j], data[batchIndex, elementIndex]);
        }

        for (int stage = 1; stage <= stages; stage++)
        {
            int m = 1 << stage;
            int m2 = m >> 1;

            double angle = -2.0 * double.Pi / m;
            Complex w = new Complex(Math.Cos(angle), Math.Sin(angle));
            Complex wm = Complex.One;

            for (int k = 0; k < m2; k++)
            {
                for (int i = k; i < fftSize; i += m)
                {
                    int t = i + m2;
                    Complex tComplex = wm * data[batchIndex, t];
                    Complex u = data[batchIndex, i];
                    data[batchIndex, i] = u + tComplex;
                    data[batchIndex, t] = u - tComplex;
                }
                wm *= w;
            }
        }
    }

    private static int BitReverse(int n, int bits)
    {
        int reversed = 0;
        for (int i = 0; i < bits; i++)
        {
            reversed <<= 1;
            reversed |= n & 1;
            n >>= 1;
        }
        return reversed;
    }

    private static void ApplyWindowKernel(
        Index1D index,
        ArrayView<double> input,
        ArrayView<double> output,
        int windowType)
    {
        int n = input.Length;
        if (index >= n) return;

        double windowValue;
        double x = (double)index / (n - 1);

        switch (windowType)
        {
            case 0:
                windowValue = 1.0;
                break;
            case 1:
                windowValue = 0.5 * (1.0 - Math.Cos(2.0 * double.Pi * x));
                break;
            case 2:
                windowValue = 0.54 - 0.46 * Math.Cos(2.0 * double.Pi * x);
                break;
            case 3:
                windowValue = 0.42 - 0.5 * Math.Cos(2.0 * double.Pi * x) + 0.08 * Math.Cos(4.0 * double.Pi * x);
                break;
            case 4:
                double alpha = 3.0;
                windowValue = XMath.BesselI0(alpha * Math.Sqrt(1 - Math.Pow(2 * x - 1, 2))) / XMath.BesselI0(alpha);
                break;
            default:
                windowValue = 1.0;
                break;
        }

        output[index] = input[index] * windowValue;
    }

    public async Task<FftCalculationResult> CalculateFftAsync(FftCalculationRequest request)
    {
        var startTime = DateTime.UtcNow;

        if (_gpuAvailable && request.FftSize >= 64)
        {
            try
            {
                return await CalculateGpuFftAsync(request, startTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GPU FFT calculation failed, falling back to CPU");
            }
        }

        return await CalculateCpuFftAsync(request, startTime);
    }

    public async Task<List<FftCalculationResult>> BatchCalculateFftAsync(List<FftCalculationRequest> requests)
    {
        var results = new List<FftCalculationResult>();

        if (_gpuAvailable && requests.Count >= 2 && requests.All(r => r.FftSize == requests[0].FftSize))
        {
            try
            {
                var batchResults = await CalculateBatchGpuFftAsync(requests);
                results.AddRange(batchResults);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch GPU FFT calculation failed, falling back to CPU");
            }
        }

        foreach (var request in requests)
        {
            var result = await CalculateCpuFftAsync(request, DateTime.UtcNow);
            results.Add(result);
        }

        return results;
    }

    public async Task QueueFftCalculationAsync(FftCalculationRequest request)
    {
        await _requestChannel.Writer.WriteAsync(request, _cancellationTokenSource.Token);
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = new List<FftCalculationRequest>();

            while (batch.Count < _options.GpuBatchSize &&
                   await _requestChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (batch.Count < _options.GpuBatchSize &&
                       _requestChannel.Reader.TryRead(out var request))
                {
                    batch.Add(request);
                }
            }

            if (batch.Count == 0) continue;

            try
            {
                var results = await BatchCalculateFftAsync(batch);

                for (int i = 0; i < batch.Count; i++)
                {
                    batch[i].CompletionSource.TrySetResult(results[i]);
                }
            }
            catch (Exception ex)
            {
                foreach (var request in batch)
                {
                    request.CompletionSource.TrySetException(ex);
                }
            }
        }
    }

    private async Task<FftCalculationResult> CalculateGpuFftAsync(
        FftCalculationRequest request,
        DateTime startTime)
    {
        await Task.Run(() =>
        {
            int fftSize = request.FftSize;
            int stages = (int)Math.Log2(fftSize);

            var complexData = new Complex[fftSize];
            var windowedData = request.ApplyWindow
                ? ApplyWindowCpu(request.TimeDomainData, request.WindowType)
                : request.TimeDomainData;

            for (int i = 0; i < fftSize; i++)
            {
                complexData[i] = i < windowedData.Length
                    ? new Complex(windowedData[i], 0.0)
                    : Complex.Zero;
            }

            var buffer2D = new Complex[1, fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                buffer2D[0, i] = complexData[i];
            }

            using var gpuBuffer = _accelerator.Allocate2DDenseX(buffer2D);
            _fftKernel(new Index2D(1, fftSize), gpuBuffer.View, fftSize, stages);
            _accelerator.Synchronize();

            gpuBuffer.CopyToCPU(buffer2D);

            for (int i = 0; i < fftSize; i++)
            {
                complexData[i] = buffer2D[0, i];
            }

            request.CompletionSource.TrySetResult(new FftCalculationResult
            {
                RequestId = request.RequestId,
                ComplexSpectrum = complexData,
                FrequencySpectrum = ConvertToPowerSpectrum(complexData),
                GpuAccelerated = true,
                CalculationTime = DateTime.UtcNow - startTime
            });
        }, _cancellationTokenSource.Token);

        return await request.CompletionSource.Task;
    }

    private async Task<List<FftCalculationResult>> CalculateBatchGpuFftAsync(
        List<FftCalculationRequest> requests)
    {
        var startTime = DateTime.UtcNow;
        var results = new List<FftCalculationResult>();

        await Task.Run(() =>
        {
            int batchSize = requests.Count;
            int fftSize = requests[0].FftSize;
            int stages = (int)Math.Log2(fftSize);

            var batchBuffer = new Complex[batchSize, fftSize];

            for (int b = 0; b < batchSize; b++)
            {
                var request = requests[b];
                var windowedData = request.ApplyWindow
                    ? ApplyWindowCpu(request.TimeDomainData, request.WindowType)
                    : request.TimeDomainData;

                for (int i = 0; i < fftSize; i++)
                {
                    batchBuffer[b, i] = i < windowedData.Length
                        ? new Complex(windowedData[i], 0.0)
                        : Complex.Zero;
                }
            }

            using var gpuBuffer = _accelerator.Allocate2DDenseX(batchBuffer);
            _fftKernel(new Index2D(fftSize, batchSize), gpuBuffer.View, fftSize, stages);
            _accelerator.Synchronize();

            gpuBuffer.CopyToCPU(batchBuffer);

            for (int b = 0; b < batchSize; b++)
            {
                var complexData = new Complex[fftSize];
                for (int i = 0; i < fftSize; i++)
                {
                    complexData[i] = batchBuffer[b, i];
                }

                results.Add(new FftCalculationResult
                {
                    RequestId = requests[b].RequestId,
                    ComplexSpectrum = complexData,
                    FrequencySpectrum = ConvertToPowerSpectrum(complexData),
                    GpuAccelerated = true,
                    CalculationTime = DateTime.UtcNow - startTime
                });
            }
        }, _cancellationTokenSource.Token);

        return results;
    }

    private async Task<FftCalculationResult> CalculateCpuFftAsync(
        FftCalculationRequest request,
        DateTime startTime)
    {
        return await Task.Run(() =>
        {
            int fftSize = request.FftSize;
            var complexData = new Complex[fftSize];
            var windowedData = request.ApplyWindow
                ? ApplyWindowCpu(request.TimeDomainData, request.WindowType)
                : request.TimeDomainData;

            for (int i = 0; i < fftSize; i++)
            {
                complexData[i] = i < windowedData.Length
                    ? new Complex(windowedData[i], 0.0)
                    : Complex.Zero;
            }

            Fourier.Forward(complexData, FourierOptions.Default);

            return new FftCalculationResult
            {
                RequestId = request.RequestId,
                ComplexSpectrum = complexData,
                FrequencySpectrum = ConvertToPowerSpectrum(complexData),
                GpuAccelerated = false,
                CalculationTime = DateTime.UtcNow - startTime
            };
        }, _cancellationTokenSource.Token);
    }

    private static double[] ApplyWindowCpu(double[] data, WindowType windowType)
    {
        int n = data.Length;
        var result = new double[n];

        for (int i = 0; i < n; i++)
        {
            double x = (double)i / (n - 1);
            double windowValue = windowType switch
            {
                WindowType.Rectangular => 1.0,
                WindowType.Hanning => 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * x)),
                WindowType.Hamming => 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * x),
                WindowType.Blackman => 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * x) + 0.08 * Math.Cos(4.0 * Math.PI * x),
                WindowType.Kaiser => CalculateKaiserWindow(x, 3.0),
                _ => 1.0
            };

            result[i] = data[i] * windowValue;
        }

        return result;
    }

    private static double CalculateKaiserWindow(double x, double alpha)
    {
        double arg = alpha * Math.Sqrt(1 - Math.Pow(2 * x - 1, 2));
        return BesselI0(arg) / BesselI0(alpha);
    }

    private static double BesselI0(double x)
    {
        double result = 1.0;
        double term = 1.0;
        double xHalf = x / 2.0;

        for (int i = 1; i <= 20; i++)
        {
            term *= xHalf / i;
            result += term * term;
        }

        return result;
    }

    private static double[] ConvertToPowerSpectrum(Complex[] complexSpectrum)
    {
        int n = complexSpectrum.Length;
        var result = new double[n];

        for (int i = 0; i < n; i++)
        {
            result[i] = 20.0 * Math.Log10(complexSpectrum[i].Magnitude + 1e-10);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cancellationTokenSource.Cancel();
        _processingTask.Wait(TimeSpan.FromSeconds(5));
        _cancellationTokenSource.Dispose();

        _accelerator?.Dispose();
        _gpuContext?.Dispose();

        _disposed = true;
    }
}
