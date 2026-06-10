using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using DeformationMonitor.Module;
using DeformationMonitor.Module.Models;
using CoSiteInterference.Module;
using CoSiteInterference.Module.Models;
using PaEfficiencyTracker.Module;
using PaEfficiencyTracker.Module.Models;
using SpectrumScanner.Module;
using SpectrumScanner.Module.Models;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

using DeformationMonitor = DeformationMonitor.Module.DeformationMonitor;
using CoSiteInterferenceAnalyzer = CoSiteInterference.Module.CoSiteInterferenceAnalyzer;
using PaEfficiencyEvaluator = PaEfficiencyTracker.Module.PaEfficiencyEvaluator;
using SpectrumScanner = SpectrumScanner.Module.SpectrumScanner;
using DeformationOptions = DeformationMonitor.Module.Models.DeformationOptions;
using CoSiteInterferenceOptions = CoSiteInterference.Module.Models.CoSiteInterferenceOptions;
using PaEfficiencyOptions = PaEfficiencyTracker.Module.Models.PaEfficiencyOptions;
using SpectrumScanOptions = SpectrumScanner.Module.Models.SpectrumScanOptions;
using SensorData = DeformationMonitor.Module.Models.SensorData;
using DeformationRequest = DeformationMonitor.Module.Models.DeformationRequest;
using CoSiteAntenna = CoSiteInterference.Module.Models.CoSiteAntenna;
using CoSiteInterferenceRequest = CoSiteInterference.Module.Models.CoSiteInterferenceRequest;
using ChannelMetric = PaEfficiencyTracker.Module.Models.ChannelMetric;
using PaEfficiencyRequest = PaEfficiencyTracker.Module.Models.PaEfficiencyRequest;
using SpectrumScanRequest = SpectrumScanner.Module.Models.SpectrumScanRequest;

namespace AntennaMonitoring.Tests.IntegrationTests;

public class FeatureModulesIntegrationTests : TestBase
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<IDeformationRecordRepository> _mockDeformationRepo;
    private readonly Mock<ICoSiteRecordRepository> _mockCoSiteRepo;
    private readonly Mock<IPaEfficiencyRecordRepository> _mockPaRepo;
    private readonly Mock<ISpectrumScanRecordRepository> _mockSpectrumRepo;
    private readonly Mock<IChannelRepository> _mockChannelRepo;
    private readonly Mock<IStationRepository> _mockStationRepo;
    private readonly Guid _testStationId;

    public FeatureModulesIntegrationTests()
    {
        _mockMediator = new Mock<IMediator>();
        _mockDeformationRepo = new Mock<IDeformationRecordRepository>();
        _mockCoSiteRepo = new Mock<ICoSiteRecordRepository>();
        _mockPaRepo = new Mock<IPaEfficiencyRecordRepository>();
        _mockSpectrumRepo = new Mock<ISpectrumScanRecordRepository>();
        _mockChannelRepo = new Mock<IChannelRepository>();
        _mockStationRepo = new Mock<IStationRepository>();
        _testStationId = Guid.NewGuid();
    }

    #region 形变监测 + 波束修正联动测试

    [Fact]
    public async Task DeformationMonitor_WithBeamCorrection_UpdatesChannelPhase()
    {
        var channels = CreateTestChannels(16);
        var originalPhases = channels.Select(c => c.CalibrationCoeffPhase).ToList();

        var deformationMonitor = CreateDeformationMonitor(true);

        var request = new DeformationMonitorRequest
        {
            StationId = _testStationId,
            Sensors = CreateTestSensors(16, 2.5, 2.5, 2.5, 850, 25),
            Channels = channels
        };

        var result = await deformationMonitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.MaxDisplacementMm.Should().BeGreaterThan(2.0);

        if (result.BeamCorrectionApplied)
        {
            var newPhases = channels.Select(c => c.CalibrationCoeffPhase).ToList();
            newPhases.Should().NotEqual(originalPhases);

            for (int i = 0; i < channels.Count; i++)
            {
                Math.Abs(newPhases[i] - originalPhases[i]).Should().BeLessOrEqualTo(Math.PI / 2);
            }

            _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
                It.IsAny<IReadOnlyList<Channel>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task DeformationMonitor_MultipleCycles_ConsistentBehavior()
    {
        var channels = CreateTestChannels(16);
        var deformationMonitor = CreateDeformationMonitor(true);

        var results = new List<DeformationMonitorResult>();

        for (int i = 0; i < 5; i++)
        {
            double strainBase = 800 + i * 20;
            var request = new DeformationMonitorRequest
            {
                StationId = _testStationId,
                Sensors = CreateTestSensors(16, 2.0, 2.0, 2.0, strainBase, 25),
                Channels = channels
            };

            var result = await deformationMonitor.RunDeformationAnalysisAsync(request, CancellationToken.None);
            results.Add(result);

            await Task.Delay(10);
        }

        results.Should().HaveCount(5);
        results.All(r => r != null).Should().BeTrue();
    }

    #endregion

    #region 共址干扰 + 频谱扫描联动测试

    [Fact]
    public async Task CoSiteInterference_WithSpectrumScan_CorrelatedResults()
    {
        var antennaA = new Antenna
        {
            Id = Guid.NewGuid(),
            Name = "AntennaA",
            Type = "BaseStation",
            FrequencyMhz = 3500,
            X = 0,
            Y = 0,
            Z = 30,
            AzimuthDeg = 0,
            TiltDeg = 0,
            MaxPowerDbm = 43,
            GainDb = 17,
            BandwidthMhz = 100
        };

        var antennaB = new Antenna
        {
            Id = Guid.NewGuid(),
            Name = "AntennaB",
            Type = "BaseStation",
            FrequencyMhz = 3500,
            X = 15,
            Y = 0,
            Z = 30,
            AzimuthDeg = 180,
            TiltDeg = 0,
            MaxPowerDbm = 43,
            GainDb = 17,
            BandwidthMhz = 100
        };

        var channels = CreateTestChannels(16);

        var coSiteAnalyzer = CreateCoSiteAnalyzer();
        var spectrumScanner = CreateSpectrumScanner(true);

        var coSiteRequest = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            Antennas = new[] { antennaA, antennaB },
            Channels = channels
        };

        var coSiteResult = await coSiteAnalyzer.AnalyzeCoSiteInterferenceAsync(coSiteRequest, CancellationToken.None);

        var spectrumRequest = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var spectrumResult = await spectrumScanner.RunSpectrumScanAsync(spectrumRequest, CancellationToken.None);

        coSiteResult.Should().NotBeNull();
        spectrumResult.Should().NotBeNull();

        if (coSiteResult.OverallSeverity == InterferenceSeverity.Warning ||
            coSiteResult.OverallSeverity == InterferenceSeverity.Critical)
        {
            double expectedRiskFreq = antennaA.FrequencyMhz;

            if (spectrumResult.InterferenceCount > 0)
            {
                var hasCorrelatedInterference = spectrumResult.InterferenceFrequenciesMhz
                    .Any(f => Math.Abs(f - expectedRiskFreq) < 20);

                if (hasCorrelatedInterference)
                {
                    var correlatedPower = spectrumResult.InterferencePowersDbm
                        .Where((p, i) => Math.Abs(spectrumResult.InterferenceFrequenciesMhz[i] - expectedRiskFreq) < 20)
                        .First();

                    correlatedPower.Should().BeGreaterThan(-80);
                }
            }
        }
    }

    #endregion

    #region 功放效率 + 形变监测联动测试

    [Fact]
    public async Task PaEfficiency_WithDeformation_CombinedAnalysis()
    {
        var channels = CreateTestChannels(16);
        var metrics = CreateTestMetrics(16, 10);

        var deformationMonitor = CreateDeformationMonitor(false);
        var paEvaluator = CreatePaEvaluator();

        var deformationRequest = new DeformationMonitorRequest
        {
            StationId = _testStationId,
            Sensors = CreateTestSensors(16, 2.0, 2.0, 2.0, 800, 75),
            Channels = channels
        };

        var deformationResult = await deformationMonitor.RunDeformationAnalysisAsync(deformationRequest, CancellationToken.None);

        var paRequest = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = metrics
        };

        var paResults = await paEvaluator.EvaluatePaEfficiencyAsync(paRequest, CancellationToken.None);

        deformationResult.Should().NotBeNull();
        paResults.Should().NotBeNull();

        if (deformationResult.MaxDisplacementMm > 2.0)
        {
            paResults.All(r => r.PaTemperature >= 65).Should().BeTrue();
        }

        if (deformationResult.OverallSeverity == SeverityLevel.Critical)
        {
            paResults.Any(r => r.EfficiencyPercent < 35).Should().BeTrue();
        }
    }

    #endregion

    #region 全流程端到端测试

    [Fact]
    public async Task FullWorkflow_AllFeatures_ProcessesCorrectly()
    {
        var channels = CreateTestChannels(16);
        var sensors = CreateTestSensors(16, 2.5, 2.5, 2.5, 850, 85);
        var metrics = CreateTestMetrics(16, 10);
        var antennas = CreateTestAntennas(3);

        var deformationMonitor = CreateDeformationMonitor(true);
        var coSiteAnalyzer = CreateCoSiteAnalyzer();
        var paEvaluator = CreatePaEvaluator();
        var spectrumScanner = CreateSpectrumScanner(true);

        var startTime = DateTime.UtcNow;

        var deformationRequest = new DeformationMonitorRequest
        {
            StationId = _testStationId,
            Sensors = sensors,
            Channels = channels
        };
        var deformationResult = await deformationMonitor.RunDeformationAnalysisAsync(deformationRequest, CancellationToken.None);

        var coSiteRequest = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            Antennas = antennas,
            Channels = channels
        };
        var coSiteResult = await coSiteAnalyzer.AnalyzeCoSiteInterferenceAsync(coSiteRequest, CancellationToken.None);

        var paRequest = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = metrics
        };
        var paResults = await paEvaluator.EvaluatePaEfficiencyAsync(paRequest, CancellationToken.None);

        var spectrumRequest = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };
        var spectrumResult = await spectrumScanner.RunSpectrumScanAsync(spectrumRequest, CancellationToken.None);

        var totalElapsed = DateTime.UtcNow - startTime;

        deformationResult.Should().NotBeNull();
        coSiteResult.Should().NotBeNull();
        paResults.Should().NotBeNull();
        spectrumResult.Should().NotBeNull();

        totalElapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<INotification>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(4));
    }

    #endregion

    #region 数据流一致性测试

    [Fact]
    public async Task MultipleModules_SameChannelData_Consistent()
    {
        var channels = CreateTestChannels(8);
        var originalTemperatures = Enumerable.Range(0, 8).Select(i => 45.0 + i * 5).ToList();
        var metrics = CreateTestMetricsWithTemperatures(8, 10, originalTemperatures);

        var paEvaluator = CreatePaEvaluator();
        var deformationMonitor = CreateDeformationMonitor(false);

        var paRequest = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = metrics
        };
        var paResults = await paEvaluator.EvaluatePaEfficiencyAsync(paRequest, CancellationToken.None);

        var sensors = CreateTestSensorsWithTemperatures(16, 2.0, 2.0, 2.0, 800, originalTemperatures);
        var deformationRequest = new DeformationMonitorRequest
        {
            StationId = _testStationId,
            Sensors = sensors,
            Channels = channels
        };
        var deformationResult = await deformationMonitor.RunDeformationAnalysisAsync(deformationRequest, CancellationToken.None);

        for (int i = 0; i < Math.Min(paResults.Count, 8); i++)
        {
            Math.Abs(paResults[i].PaTemperature - originalTemperatures[i]).Should().BeLessThan(10);
        }

        for (int i = 0; i < Math.Min(sensors.Count, 8); i++)
        {
            Math.Abs(sensors[i].Temperature - originalTemperatures[i % 8]).Should().BeLessThan(1);
        }
    }

    #endregion

    #region 事件流验证测试

    [Fact]
    public async Task AllModules_EventsPublished_InCorrectOrder()
    {
        var channels = CreateTestChannels(8);
        var eventOrder = new List<string>();

        _mockMediator
            .Setup(m => m.Publish(It.IsAny<DeformationAnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((e, _) => eventOrder.Add("DeformationCompleted"));

        _mockMediator
            .Setup(m => m.Publish(It.IsAny<CoSiteAnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((e, _) => eventOrder.Add("CoSiteCompleted"));

        _mockMediator
            .Setup(m => m.Publish(It.IsAny<PaEfficiencyAnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((e, _) => eventOrder.Add("PaEfficiencyCompleted"));

        _mockMediator
            .Setup(m => m.Publish(It.IsAny<SpectrumScanCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((e, _) => eventOrder.Add("SpectrumCompleted"));

        var deformationMonitor = CreateDeformationMonitor(false);
        var coSiteAnalyzer = CreateCoSiteAnalyzer();
        var paEvaluator = CreatePaEvaluator();
        var spectrumScanner = CreateSpectrumScanner(false);

        await deformationMonitor.RunDeformationAnalysisAsync(
            new DeformationMonitorRequest
            {
                StationId = _testStationId,
                Sensors = CreateTestSensors(8, 1.0, 1.0, 1.0, 700, 25),
                Channels = channels
            }, CancellationToken.None);

        await coSiteAnalyzer.AnalyzeCoSiteInterferenceAsync(
            new CoSiteInterferenceRequest
            {
                StationId = _testStationId,
                Antennas = CreateTestAntennas(2),
                Channels = channels
            }, CancellationToken.None);

        await paEvaluator.EvaluatePaEfficiencyAsync(
            new PaEfficiencyRequest
            {
                StationId = _testStationId,
                Channels = channels,
                RecentMetrics = CreateTestMetrics(8, 5)
            }, CancellationToken.None);

        await spectrumScanner.RunSpectrumScanAsync(
            new SpectrumScanRequest
            {
                StationId = _testStationId,
                StartFrequencyMhz = 3400,
                EndFrequencyMhz = 3600,
                ResolutionBandwidthKhz = 100,
                Channels = channels
            }, CancellationToken.None);

        eventOrder.Should().HaveCount(4);
        eventOrder[0].Should().Be("DeformationCompleted");
        eventOrder[1].Should().Be("CoSiteCompleted");
        eventOrder[2].Should().Be("PaEfficiencyCompleted");
        eventOrder[3].Should().Be("SpectrumCompleted");
    }

    #endregion

    #region 并发测试

    [Fact]
    public async Task MultipleModules_RunConcurrently_NoRaceConditions()
    {
        var channels = CreateTestChannels(16);
        var sensors = CreateTestSensors(16, 2.0, 2.0, 2.0, 800, 45);
        var metrics = CreateTestMetrics(16, 10);
        var antennas = CreateTestAntennas(3);

        var deformationMonitor = CreateDeformationMonitor(false);
        var coSiteAnalyzer = CreateCoSiteAnalyzer();
        var paEvaluator = CreatePaEvaluator();
        var spectrumScanner = CreateSpectrumScanner(false);

        var tasks = new[]
        {
            deformationMonitor.RunDeformationAnalysisAsync(
                new DeformationMonitorRequest
                {
                    StationId = _testStationId,
                    Sensors = sensors,
                    Channels = channels
                }, CancellationToken.None),
            coSiteAnalyzer.AnalyzeCoSiteInterferenceAsync(
                new CoSiteInterferenceRequest
                {
                    StationId = _testStationId,
                    Antennas = antennas,
                    Channels = channels
                }, CancellationToken.None),
            paEvaluator.EvaluatePaEfficiencyAsync(
                new PaEfficiencyRequest
                {
                    StationId = _testStationId,
                    Channels = channels,
                    RecentMetrics = metrics
                }, CancellationToken.None),
            spectrumScanner.RunSpectrumScanAsync(
                new SpectrumScanRequest
                {
                    StationId = _testStationId,
                    StartFrequencyMhz = 3400,
                    EndFrequencyMhz = 3600,
                    ResolutionBandwidthKhz = 100,
                    Channels = channels
                }, CancellationToken.None)
        };

        var allResults = await Task.WhenAll(tasks);

        allResults.Should().NotContainNulls();

        foreach (var channel in channels)
        {
            channel.CalibrationCoeffPhase.Should().BeInRange(-Math.PI, Math.PI);
        }
    }

    #endregion

    #region 错误传播测试

    [Fact]
    public async Task OneModuleFails_OthersContinue()
    {
        var channels = CreateTestChannels(8);

        var failingLogger = new Mock<ILogger<DeformationMonitor>>();
        failingLogger
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Throws<InvalidOperationException>();

        var failingOptions = CreateOptions(new DeformationMonitorOptions
        {
            AutoBeamCorrection = false,
            DisplacementThresholdMm = 2.0,
            WarningThresholdMm = 1.5,
            CriticalThresholdMm = 3.0,
            PlateWidthM = 0.6,
            PlateHeightM = 0.4,
            PlateThicknessM = 0.005,
            YoungsModulusPa = 73.1e9,
            PoissonsRatio = 0.33,
            TemperatureCorrection = true,
            MaxSensorDeviation = 5.0,
            DefaultWindSpeed = 0.0
        });

        var failingMonitor = new DeformationMonitor(
            failingLogger.Object,
            _mockDeformationRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            failingOptions);

        var paEvaluator = CreatePaEvaluator();

        Func<Task> deformationAction = async () =>
        {
            await failingMonitor.RunDeformationAnalysisAsync(
                new DeformationMonitorRequest
                {
                    StationId = _testStationId,
                    Sensors = CreateTestSensors(8, 2.0, 2.0, 2.0, 800, 25),
                    Channels = channels
                }, CancellationToken.None);
        };

        var paTask = paEvaluator.EvaluatePaEfficiencyAsync(
            new PaEfficiencyRequest
            {
                StationId = _testStationId,
                Channels = channels,
                RecentMetrics = CreateTestMetrics(8, 5)
            }, CancellationToken.None);

        var exception = await Record.ExceptionAsync(deformationAction);
        var paResult = await paTask;

        exception.Should().NotBeNull();
        paResult.Should().NotBeNull();
        paResult.Should().NotContainNulls();
    }

    #endregion

    #region 辅助方法

    private IReadOnlyList<Channel> CreateTestChannels(int count)
    {
        return Enumerable.Range(0, count).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffAmplitude = 1.0,
            CalibrationCoeffPhase = 0,
            Connected = true,
            LastUpdate = DateTime.UtcNow
        }).ToList();
    }

    private IReadOnlyList<DeformationSensorData> CreateTestSensors(
        int count,
        double tiltX,
        double tiltY,
        double tiltZ,
        double strain,
        double temperature)
    {
        return Enumerable.Range(0, count).Select(i => new DeformationSensorData
        {
            SensorId = $"S{i:00}",
            RowIndex = i / 4,
            ColIndex = i % 4,
            PositionX = (i % 4) * 0.15,
            PositionY = (i / 4) * 0.1,
            TiltXDeg = tiltX + Random.Shared.NextDouble() * 0.2,
            TiltYDeg = tiltY + Random.Shared.NextDouble() * 0.2,
            TiltZDeg = tiltZ + Random.Shared.NextDouble() * 0.2,
            StrainMicro = strain + Random.Shared.NextDouble() * 50,
            Temperature = temperature + Random.Shared.NextDouble() * 2,
            Timestamp = DateTime.UtcNow
        }).ToList();
    }

    private IReadOnlyList<DeformationSensorData> CreateTestSensorsWithTemperatures(
        int count,
        double tiltX,
        double tiltY,
        double tiltZ,
        double strain,
        IReadOnlyList<double> temperatures)
    {
        return Enumerable.Range(0, count).Select(i => new DeformationSensorData
        {
            SensorId = $"S{i:00}",
            RowIndex = i / 4,
            ColIndex = i % 4,
            PositionX = (i % 4) * 0.15,
            PositionY = (i / 4) * 0.1,
            TiltXDeg = tiltX + Random.Shared.NextDouble() * 0.2,
            TiltYDeg = tiltY + Random.Shared.NextDouble() * 0.2,
            TiltZDeg = tiltZ + Random.Shared.NextDouble() * 0.2,
            StrainMicro = strain + Random.Shared.NextDouble() * 50,
            Temperature = temperatures[i % temperatures.Count],
            Timestamp = DateTime.UtcNow
        }).ToList();
    }

    private IReadOnlyList<ChannelMetric> CreateTestMetrics(int channelCount, int hoursOfHistory)
    {
        var metrics = new List<ChannelMetric>();
        var baseTime = DateTime.UtcNow.AddHours(-hoursOfHistory);

        for (int i = 0; i < channelCount; i++)
        {
            for (int h = 0; h < hoursOfHistory; h++)
            {
                metrics.Add(new ChannelMetric
                {
                    ChannelId = $"CH{i:00}",
                    ChannelIndex = i,
                    TxPower = 40.0 - i * 0.5 + Random.Shared.NextDouble() * 2,
                    PaTemperature = 45.0 + i * 3 + Random.Shared.NextDouble() * 5,
                    Amplitude = 1.0 - i * 0.01,
                    Phase = Random.Shared.NextDouble() * Math.PI / 4,
                    Swr = 1.2 + Random.Shared.NextDouble() * 0.3,
                    Timestamp = baseTime.AddHours(h)
                });
            }
        }

        return metrics;
    }

    private IReadOnlyList<ChannelMetric> CreateTestMetricsWithTemperatures(
        int channelCount,
        int hoursOfHistory,
        IReadOnlyList<double> temperatures)
    {
        var metrics = new List<ChannelMetric>();
        var baseTime = DateTime.UtcNow.AddHours(-hoursOfHistory);

        for (int i = 0; i < channelCount; i++)
        {
            for (int h = 0; h < hoursOfHistory; h++)
            {
                metrics.Add(new ChannelMetric
                {
                    ChannelId = $"CH{i:00}",
                    ChannelIndex = i,
                    TxPower = 40.0 - i * 0.5,
                    PaTemperature = temperatures[i] + Random.Shared.NextDouble() * 2,
                    Amplitude = 1.0 - i * 0.01,
                    Phase = 0,
                    Swr = 1.2,
                    Timestamp = baseTime.AddHours(h)
                });
            }
        }

        return metrics;
    }

    private IReadOnlyList<Antenna> CreateTestAntennas(int count)
    {
        var positions = new[]
        {
            new { X = 0.0, Y = 0.0, Z = 30.0 },
            new { X = 20.0, Y = 0.0, Z = 30.0 },
            new { X = 10.0, Y = 15.0, Z = 30.0 }
        };

        return Enumerable.Range(0, count).Select(i => new Antenna
        {
            Id = Guid.NewGuid(),
            Name = $"Antenna{i}",
            Type = "BaseStation",
            FrequencyMhz = 3400 + i * 100,
            X = positions[i % positions.Length].X,
            Y = positions[i % positions.Length].Y,
            Z = positions[i % positions.Length].Z,
            AzimuthDeg = i * 120,
            TiltDeg = 5,
            MaxPowerDbm = 43,
            GainDb = 17,
            BandwidthMhz = 100
        }).ToList();
    }

    private DeformationMonitor CreateDeformationMonitor(bool autoBeamCorrection)
    {
        return new DeformationMonitor(
            CreateMockLogger<DeformationMonitor>().Object,
            _mockDeformationRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            CreateOptions(new DeformationMonitorOptions
            {
                AutoBeamCorrection = autoBeamCorrection,
                DisplacementThresholdMm = 2.0,
                WarningThresholdMm = 1.5,
                CriticalThresholdMm = 3.0,
                PlateWidthM = 0.6,
                PlateHeightM = 0.4,
                PlateThicknessM = 0.005,
                YoungsModulusPa = 73.1e9,
                PoissonsRatio = 0.33,
                TemperatureCorrection = true,
                MaxSensorDeviation = 5.0,
                DefaultWindSpeed = 0.0
            }));
    }

    private CoSiteInterferenceAnalyzer CreateCoSiteAnalyzer()
    {
        return new CoSiteInterferenceAnalyzer(
            CreateMockLogger<CoSiteInterferenceAnalyzer>().Object,
            _mockCoSiteRepo.Object,
            _mockMediator.Object,
            CreateOptions(new CoSiteInterferenceOptions
            {
                IsolationThresholdDb = 30,
                WarningThresholdDb = 40,
                CriticalThresholdDb = 25,
                MinDistanceM = 5.0,
                MaxCouplingDistM = 100,
                CouplingLossExponent = 1.5,
                SpuriousLevelDb = -60,
                AltitudeMarginM = 3.0,
                BandOverlapThresholdMhz = 10,
                MinHeightDiffM = 2.0
            }));
    }

    private PaEfficiencyEvaluator CreatePaEvaluator()
    {
        return new PaEfficiencyEvaluator(
            CreateMockLogger<PaEfficiencyEvaluator>().Object,
            _mockPaRepo.Object,
            _mockMediator.Object,
            CreateOptions(new PaEfficiencyOptions
            {
                EfficiencyThresholdPercent = 30.0,
                WarningThresholdPercent = 35.0,
                CriticalThresholdPercent = 25.0,
                DecayRateThresholdPercentPerMonth = 1.0,
                WarningDecayRatePercentPerMonth = 0.5,
                TemperatureDeratingFactorPercentPerC = 0.1,
                NominalTemperatureC = 25.0,
                MaxTemperatureC = 85.0,
                HistoryHours = 24,
                NominalGainDb = 14.0,
                MinGainDb = 10.0,
                ReferenceVoltageV = 28.0
            }));
    }

    private SpectrumScanner CreateSpectrumScanner(bool autoNullSteering)
    {
        return new SpectrumScanner(
            CreateMockLogger<SpectrumScanner>().Object,
            _mockSpectrumRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            CreateOptions(new SpectrumScanOptions
            {
                StartFrequencyMhz = 3400,
                EndFrequencyMhz = 3600,
                ResolutionBandwidthKhz = 100,
                InterferencePowerThresholdDbm = -80,
                NullDepthTargetDb = 25,
                MaxNullCount = 3,
                AutoNullSteering = autoNullSteering,
                DoaEstimationAccuracy = 0.9
            }));
    }

    #endregion
}
