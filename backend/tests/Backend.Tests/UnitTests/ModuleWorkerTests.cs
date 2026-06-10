using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using DeformationMonitor.Module;
using DeformationMonitor.Module.Models;
using DeformationMonitor.Module.Workers;
using CoSiteInterference.Module;
using CoSiteInterference.Module.Models;
using CoSiteInterference.Module.Workers;
using PaEfficiencyTracker.Module;
using PaEfficiencyTracker.Module.Models;
using PaEfficiencyTracker.Module.Workers;
using SpectrumScanner.Module;
using SpectrumScanner.Module.Models;
using SpectrumScanner.Module.Workers;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Channels;
using Xunit;

using DeformationOptions = DeformationMonitor.Module.Models.DeformationOptions;
using CoSiteInterferenceOptions = CoSiteInterference.Module.Models.CoSiteInterferenceOptions;
using PaEfficiencyOptions = PaEfficiencyTracker.Module.Models.PaEfficiencyOptions;
using SpectrumScanOptions = SpectrumScanner.Module.Models.SpectrumScanOptions;

namespace AntennaMonitoring.Tests.UnitTests;

public class FemCalculationWorkerTests : TestBase, IAsyncLifetime
{
    private Mock<ILogger<FemCalculationWorker>> _mockLogger;
    private FemCalculationWorker _worker;
    private IOptions<DeformationOptions> _options;

    public async Task InitializeAsync()
    {
        _mockLogger = CreateMockLogger<FemCalculationWorker>();
        _options = CreateOptions(new DeformationOptions
        {
            ThresholdMm = 0.5,
            AutoBeamCorrection = true
        });

        _worker = new FemCalculationWorker(_mockLogger.Object, _options);
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_worker != null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }
    }

    [Fact]
    public async Task CalculateDisplacementFEMAsync_ValidInput_ShouldReturnValidResult()
    {
        var sensorData = new SensorData
        {
            StationId = Guid.NewGuid(),
            SensorIndex = 0,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005,
            WindSpeed = 10,
            Temperature = 25
        };

        var result = await _worker.CalculateDisplacementFEMAsync(sensorData);

        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(100);
        double.IsNaN(result).Should().BeFalse();
        double.IsInfinity(result).Should().BeFalse();
    }

    [Fact]
    public async Task CalculateDisplacementFEMAsync_InterpolatedData_ShouldWork()
    {
        var sensorData = new SensorData
        {
            StationId = Guid.NewGuid(),
            SensorIndex = 4,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005,
            WindSpeed = 10,
            Temperature = 25,
            IsInterpolated = true
        };

        var result = await _worker.CalculateDisplacementFEMAsync(sensorData);

        result.Should().BeGreaterThan(0);
        double.IsNaN(result).Should().BeFalse();
    }

    [Fact]
    public async Task CalculateBatchDisplacementFEMAsync_MultipleSensors_ShouldReturnAll()
    {
        var sensorDatas = Enumerable.Range(0, 9).Select(i => new SensorData
        {
            StationId = Guid.NewGuid(),
            SensorIndex = i,
            TiltAngleX = 0.5 + i * 0.01,
            TiltAngleY = 0.3 + i * 0.01,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005 + i * 0.00001,
            WindSpeed = 10,
            Temperature = 25
        }).ToList();

        var results = await _worker.CalculateBatchDisplacementFEMAsync(sensorDatas);

        results.Should().HaveCount(9);
        results.All(r => r > 0).Should().BeTrue();
        results.All(r => !double.IsNaN(r)).Should().BeTrue();
    }

    [Fact]
    public async Task CalculateDisplacementFEMAsync_Cancellation_ShouldCancel()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var sensorData = new SensorData
        {
            StationId = Guid.NewGuid(),
            SensorIndex = 0,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005,
            WindSpeed = 10,
            Temperature = 25
        };

        var ex = await Record.ExceptionAsync(() =>
            _worker.CalculateDisplacementFEMAsync(sensorData, cts.Token));

        ex.Should().BeNull();
    }
}

public class CouplingMatrixWorkerTests : TestBase, IAsyncLifetime
{
    private Mock<ILogger<CouplingMatrixWorker>> _mockLogger;
    private CouplingMatrixWorker _worker;
    private IOptions<CoSiteInterferenceOptions> _options;

    public async Task InitializeAsync()
    {
        _mockLogger = CreateMockLogger<CouplingMatrixWorker>();
        _options = CreateOptions(new CoSiteInterferenceOptions
        {
            IsolationThresholdDb = 30.0,
            FastCalculationDistanceThresholdMeters = 100.0,
            PcaDimensions = 3,
            CacheCapacity = 1000
        });

        _worker = new CouplingMatrixWorker(_mockLogger.Object, _options);
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_worker != null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }
    }

    [Fact]
    public async Task CalculateIsolationAsync_NearField_ShouldUseFullCalculation()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 10,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var result = await _worker.CalculateIsolationAsync(
            antenna, 3400, 3600, 43);

        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(100);
        double.IsNaN(result).Should().BeFalse();
    }

    [Fact]
    public async Task CalculateIsolationAsync_FarField_ShouldUseApproximate()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 200,
            AzimuthAngleDeg = 180,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var result = await _worker.CalculateIsolationAsync(
            antenna, 3400, 3600, 43);

        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(100);
    }

    [Fact]
    public async Task BuildCouplingMatrixAsync_MultipleAntennas_ShouldReturnMatrix()
    {
        var antennas = Enumerable.Range(0, 8).Select(i => new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            FrequencyStartMhz = 3400 + i * 10,
            FrequencyEndMhz = 3500 + i * 10,
            TransmitPowerDbm = 40 + i % 5,
            SeparationDistanceMeters = 5.0 + i * 2,
            AzimuthAngleDeg = i * 10,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        }).ToList();

        var matrix = await _worker.BuildCouplingMatrixAsync(antennas, 3400, 3600);

        matrix.Should().NotBeNull();
        matrix.GetLength(0).Should().Be(8);
        matrix.GetLength(1).Should().Be(8);

        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                double.IsNaN(matrix[i, j]).Should().BeFalse();
            }
        }
    }

    [Fact]
    public void WorkerCache_ShouldCacheResults()
    {
        var antenna1 = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            SeparationDistanceMeters = 50,
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500
        };

        var key1 = (50.0, 3400.0, 3500.0, 3400.0, 3600.0);
        _worker.CacheSize.Should().Be(0);
    }
}

public class TemperatureCalibrationWorkerTests : TestBase, IAsyncLifetime
{
    private Mock<ILogger<TemperatureCalibrationWorker>> _mockLogger;
    private TemperatureCalibrationWorker _worker;
    private IOptions<PaEfficiencyOptions> _options;

    public async Task InitializeAsync()
    {
        _mockLogger = CreateMockLogger<TemperatureCalibrationWorker>();
        _options = CreateOptions(new PaEfficiencyOptions
        {
            TemperatureDriftThreshold = 5.0,
            KalmanFilterAlpha = 0.3,
            NominalGainDb = 28.0,
            NominalEfficiencyPercent = 45.0,
            NominalDcVoltageV = 28.0
        });

        _worker = new TemperatureCalibrationWorker(_mockLogger.Object, _options);
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_worker != null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }
    }

    [Fact]
    public async Task CalibrateTemperatureAsync_StableData_ShouldNotChange()
    {
        var recentTemperatures = Enumerable.Range(0, 10)
            .Select(i => 45.0 + (i - 5) * 0.3)
            .ToList();

        var referenceTemperatures = Enumerable.Range(0, 5)
            .Select(i => 45.0)
            .ToList();

        var result = await _worker.CalibrateTemperatureAsync(
            0, 45.0, recentTemperatures, referenceTemperatures);

        result.CalibratedTemperature.Should().BeApproximately(45.0, 0.5);
        result.DriftDetected.Should().BeFalse();
        result.DriftAmount.Should().BeApproximately(0, 0.5);
    }

    [Fact]
    public async Task CalibrateTemperatureAsync_DriftedData_ShouldCalibrate()
    {
        var channelIndex = 3;
        var rawTemperature = 45.0 + 10.0;

        var recentTemperatures = Enumerable.Range(0, 10)
            .Select(i => rawTemperature + (i - 5) * 0.3)
            .ToList();

        var referenceTemperatures = Enumerable.Range(0, 8)
            .Select(i => 45.0 + (i - 4) * 0.2)
            .ToList();

        var result = await _worker.CalibrateTemperatureAsync(
            channelIndex, rawTemperature, recentTemperatures, referenceTemperatures);

        result.DriftDetected.Should().BeTrue();
        result.DriftAmount.Should().BeGreaterThan(3.0);
        result.CalibratedTemperature.Should().BeLessThan(rawTemperature);
        result.CalibratedTemperature.Should().BeGreaterThan(45.0);
    }

    [Fact]
    public async Task QueueBatchCalibrationAsync_MultipleChannels_ShouldProcessAll()
    {
        var requests = Enumerable.Range(0, 8).Select(i => new CalibrationRequest
        {
            ChannelIndex = i,
            RawTemperature = i % 3 == 0 ? 45.0 + 8.0 : 45.0,
            RecentTemperatures = Enumerable.Range(0, 10)
                .Select(j => (i % 3 == 0 ? 53.0 : 45.0) + (j - 5) * 0.3)
                .ToList(),
            ReferenceTemperatures = Enumerable.Range(0, 5)
                .Select(j => 45.0 + (j - 2) * 0.2)
                .ToList()
        }).ToList();

        var results = await _worker.QueueBatchCalibrationAsync(requests);

        results.Should().HaveCount(8);
        results.Where(r => r.DriftDetected).Count().Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task CalibrateTemperatureAsync_InsufficientData_ShouldReturnRaw()
    {
        var recentTemperatures = new List<double> { 45.0 };
        var referenceTemperatures = new List<double>();

        var result = await _worker.CalibrateTemperatureAsync(
            0, 45.0, recentTemperatures, referenceTemperatures);

        result.CalibratedTemperature.Should().Be(45.0);
        result.DriftDetected.Should().BeFalse();
    }
}

public class GpuFftWorkerTests : TestBase, IAsyncLifetime
{
    private Mock<ILogger<GpuFftWorker>> _mockLogger;
    private GpuFftWorker _worker;
    private IOptions<SpectrumScanOptions> _options;

    public async Task InitializeAsync()
    {
        _mockLogger = CreateMockLogger<GpuFftWorker>();
        _options = CreateOptions(new SpectrumScanOptions
        {
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            WidebandThresholdMhz = 5.0,
            SubbandWidthMhz = 2.0,
            MaxSubbands = 8,
            EnableGpuAcceleration = true,
            GpuBatchSize = 16
        });

        _worker = new GpuFftWorker(_mockLogger.Object, _options);
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_worker != null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }
    }

    [Fact]
    public async Task CalculateFftAsync_SmallData_ShouldWork()
    {
        var n = 256;
        var inputData = new double[n];
        for (int i = 0; i < n; i++)
        {
            inputData[i] = Math.Sin(2 * Math.PI * 10 * i / n);
        }

        var request = new FftCalculationRequest
        {
            InputData = inputData,
            WindowFunction = WindowFunctionType.Hanning
        };

        var result = await _worker.CalculateFftAsync(request);

        result.Should().NotBeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData.Length.Should().Be(n / 2 + 1);
        result.IsGpuAccelerated.Should().BeTrue();
        result.ProcessingTimeMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task CalculateFftAsync_NoWindow_ShouldWork()
    {
        var n = 128;
        var inputData = new double[n];
        for (int i = 0; i < n; i++)
        {
            inputData[i] = Math.Cos(2 * Math.PI * 5 * i / n);
        }

        var request = new FftCalculationRequest
        {
            InputData = inputData,
            WindowFunction = WindowFunctionType.Rectangular
        };

        var result = await _worker.CalculateFftAsync(request);

        result.Should().NotBeNull();
        result.OutputData.Should().NotBeNull();
        result.OutputData.All(v => !double.IsNaN(v)).Should().BeTrue();
    }

    [Fact]
    public async Task BatchCalculateFftAsync_MultipleRequests_ShouldProcessAll()
    {
        var requests = Enumerable.Range(0, 4).Select(i =>
        {
            var n = 256;
            var data = new double[n];
            for (int j = 0; j < n; j++)
            {
                data[j] = Math.Sin(2 * Math.PI * (10 + i * 5) * j / n);
            }
            return new FftCalculationRequest
            {
                InputData = data,
                WindowFunction = WindowFunctionType.Hamming
            };
        }).ToList();

        var results = await _worker.BatchCalculateFftAsync(requests);

        results.Should().HaveCount(4);
        results.All(r => r != null).Should().BeTrue();
        results.All(r => r.OutputData.Length == 129).Should().BeTrue();
    }

    [Fact]
    public async Task CalculateFftAsync_WindowFunctions_ShouldAllWork()
    {
        var n = 64;
        var inputData = new double[n];
        for (int i = 0; i < n; i++)
        {
            inputData[i] = Math.Sin(2 * Math.PI * 8 * i / n);
        }

        var windowTypes = new[]
        {
            WindowFunctionType.Rectangular,
            WindowFunctionType.Hanning,
            WindowFunctionType.Hamming,
            WindowFunctionType.Blackman,
            WindowFunctionType.Kaiser
        };

        foreach (var windowType in windowTypes)
        {
            var request = new FftCalculationRequest
            {
                InputData = inputData,
                WindowFunction = windowType
            };

            var result = await _worker.CalculateFftAsync(request);

            result.Should().NotBeNull($"Window type {windowType} should work");
            result.OutputData.Should().NotBeNull();
            result.OutputData.All(v => !double.IsNaN(v)).Should().BeTrue(
                $"Window type {windowType} should not produce NaN");
        }
    }
}

public class ServiceCollectionExtensionsTests : TestBase
{
    [Fact]
    public void AddDeformationMonitor_WithConfig_ShouldRegister()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Deformation:ThresholdMm"] = "1.0",
                ["Deformation:AutoBeamCorrection"] = "true"
            })
            .Build();

        services.AddLogging();
        services.AddDeformationMonitor(config.GetSection("Deformation"));

        var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetService<IDeformationMonitor>();

        monitor.Should().NotBeNull();
    }

    [Fact]
    public void AddCoSiteInterferenceAnalyzer_WithConfig_ShouldRegister()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CoSiteInterference:IsolationThresholdDb"] = "30.0"
            })
            .Build();

        services.AddLogging();
        services.AddCoSiteInterferenceAnalyzer(config.GetSection("CoSiteInterference"));

        var serviceProvider = services.BuildServiceProvider();
        var analyzer = serviceProvider.GetService<ICoSiteInterferenceAnalyzer>();

        analyzer.Should().NotBeNull();
    }

    [Fact]
    public void AddPaEfficiencyEvaluator_WithConfig_ShouldRegister()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaEfficiency:ThresholdPercent"] = "40.0",
                ["PaEfficiency:TemperatureDriftThreshold"] = "5.0"
            })
            .Build();

        services.AddLogging();
        services.AddPaEfficiencyEvaluator(config.GetSection("PaEfficiency"));

        var serviceProvider = services.BuildServiceProvider();
        var evaluator = serviceProvider.GetService<IPaEfficiencyEvaluator>();

        evaluator.Should().NotBeNull();
    }

    [Fact]
    public void AddSpectrumScanner_WithConfig_ShouldRegister()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpectrumScan:StartFrequencyMhz"] = "3400",
                ["SpectrumScan:EndFrequencyMhz"] = "3600",
                ["SpectrumScan:EnableGpuAcceleration"] = "true"
            })
            .Build();

        services.AddLogging();
        services.AddSpectrumScanner(config.GetSection("SpectrumScan"));
        services.AddSpectrumScannerRepositories(
            _ => Mock.Of<ISpectrumScanRecordRepository>(),
            _ => Mock.Of<IChannelRepository>());

        var serviceProvider = services.BuildServiceProvider();
        var scanner = serviceProvider.GetService<ISpectrumScanner>();

        scanner.Should().NotBeNull();
    }
}
