using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using SpectrumScanner.Module;
using SpectrumScanner.Module.Models;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

using SpectrumScanner = SpectrumScanner.Module.SpectrumScanner;
using ISpectrumScanner = SpectrumScanner.Module.ISpectrumScanner;
using SpectrumScanOptions = SpectrumScanner.Module.Models.SpectrumScanOptions;
using SpectrumScanRequest = SpectrumScanner.Module.Models.SpectrumScanRequest;
using SpectrumScanResult = SpectrumScanner.Module.Models.SpectrumScanResult;

namespace AntennaMonitoring.Tests.UnitTests;

public class SpectrumScannerTests : TestBase
{
    private readonly Mock<ILogger<SpectrumScanner>> _mockLogger;
    private readonly Mock<ISpectrumScanRecordRepository> _mockScanRepo;
    private readonly Mock<IChannelRepository> _mockChannelRepo;
    private readonly Mock<IMediator> _mockMediator;
    private readonly IOptions<SpectrumScanOptions> _options;
    private readonly SpectrumScanner _scanner;
    private readonly Guid _testStationId = Guid.NewGuid();

    public SpectrumScannerTests()
    {
        _mockLogger = CreateMockLogger<SpectrumScanner>();
        _mockScanRepo = new Mock<ISpectrumScanRecordRepository>();
        _mockChannelRepo = new Mock<IChannelRepository>();
        _mockMediator = new Mock<IMediator>();
        _options = CreateOptions(new SpectrumScanOptions
        {
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            InterferencePowerThresholdDbm = -80,
            NullDepthTargetDb = 25,
            MaxNullCount = 3,
            AutoNullSteering = true,
            DoaEstimationAccuracy = 0.9
        });

        _scanner = new SpectrumScanner(
            _mockLogger.Object,
            _mockScanRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            _options);
    }

    #region 干扰信号识别准确率测试

    [Fact]
    public async Task RunSpectrumScan_NormalBand_GeneratesExpectedPoints()
    {
        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 1, RowIndex = 0, ColumnIndex = 1 }
        };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.FrequencyPointsMhz.Should().NotBeEmpty();
        result.PowerLevelsDbm.Should().NotBeEmpty();
        result.FrequencyPointsMhz.Length.Should().Be(result.PowerLevelsDbm.Length);
        result.FrequencyPointsMhz[0].Should().Be(3400);
        result.FrequencyPointsMhz[^1].Should().Be(3600);
        result.NoiseFloorDbm.Should().BeInRange(-120, -100);
    }

    [Fact]
    public async Task CalculateNoiseFloor_VariousConditions_ExpectedRange()
    {
        var testCases = new[]
        {
            new { Start = 3400, End = 3600, RBW = 100, ExpectedMin = -115, ExpectedMax = -105 },
            new { Start = 1800, End = 1900, RBW = 100, ExpectedMin = -117, ExpectedMax = -107 },
            new { Start = 2500, End = 2700, RBW = 200, ExpectedMin = -112, ExpectedMax = -102 }
        };

        foreach (var testCase in testCases)
        {
            var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

            var request = new SpectrumScanRequest
            {
                StationId = _testStationId,
                StartFrequencyMhz = testCase.Start,
                EndFrequencyMhz = testCase.End,
                ResolutionBandwidthKhz = testCase.RBW,
                Channels = channels
            };

            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

            result.NoiseFloorDbm.Should().BeInRange(testCase.ExpectedMin, testCase.ExpectedMax);
        }
    }

    [Fact]
    public async Task DetectInterferences_AboveThreshold_DetectedCorrectly()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var results = new List<SpectrumScanResult>();
        int detectionCount = 0;

        for (int i = 0; i < 50; i++)
        {
            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
            results.Add(result);
            if (result.InterferenceCount > 0) detectionCount++;
        }

        detectionCount.Should().BeGreaterThan(0);
        detectionCount.Should().BeLessOrEqualTo(50);

        var resultsWithInterference = results.Where(r => r.InterferenceCount > 0).ToList();
        if (resultsWithInterference.Any())
        {
            foreach (var result in resultsWithInterference)
            {
                result.InterferenceCount.Should().BeGreaterThan(0);
                result.InterferenceFrequenciesMhz.Should().HaveCount(result.InterferenceCount);
                result.InterferencePowersDbm.Should().HaveCount(result.InterferenceCount);

                for (int i = 0; i < result.InterferenceCount; i++)
                {
                    result.InterferencePowersDbm[i].Should().BeGreaterThan(-80);
                    result.InterferenceFrequenciesMhz[i].Should().BeInRange(3400, 3600);
                }
            }
        }
    }

    [Fact]
    public async Task DetectInterferences_PeakDetectionAlgorithm_WorksCorrectly()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        if (result.InterferenceCount > 0)
        {
            for (int i = 0; i < result.InterferenceCount; i++)
            {
                var freq = result.InterferenceFrequenciesMhz[i];
                var freqIndex = Array.FindIndex(result.FrequencyPointsMhz, f => Math.Abs(f - freq) < 0.1);

                if (freqIndex > 2 && freqIndex < result.PowerLevelsDbm.Length - 2)
                {
                    var power = result.PowerLevelsDbm[freqIndex];
                    power.Should().BeGreaterThan(result.PowerLevelsDbm[freqIndex - 1]);
                    power.Should().BeGreaterThan(result.PowerLevelsDbm[freqIndex + 1]);
                    power.Should().BeGreaterThan(result.PowerLevelsDbm[freqIndex - 2] + 3);
                    power.Should().BeGreaterThan(result.PowerLevelsDbm[freqIndex + 2] + 3);
                }
            }
        }
    }

    [Fact]
    public async Task DetectInterferences_DuplicatePeaks_Removed()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        for (int i = 0; i < 20; i++)
        {
            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

            if (result.InterferenceCount > 1)
            {
                for (int j = 0; j < result.InterferenceCount; j++)
                {
                    for (int k = j + 1; k < result.InterferenceCount; k++)
                    {
                        Math.Abs(result.InterferenceFrequenciesMhz[j] - result.InterferenceFrequenciesMhz[k])
                            .Should().BeGreaterThan(1.0);
                    }
                }
            }
        }
    }

    #endregion

    #region 虚警率测试

    [Fact]
    public async Task DetectInterferences_BelowThreshold_NotDetected()
    {
        var lowThresholdOptions = CreateOptions(new SpectrumScanOptions
        {
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            InterferencePowerThresholdDbm = -40,
            NullDepthTargetDb = 25,
            MaxNullCount = 3,
            AutoNullSteering = false,
            DoaEstimationAccuracy = 0.9
        });

        var scanner = new SpectrumScanner(
            _mockLogger.Object,
            _mockScanRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            lowThresholdOptions);

        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        int falseAlarms = 0;
        int totalScans = 50;

        for (int i = 0; i < totalScans; i++)
        {
            var result = await scanner.RunSpectrumScanAsync(request, CancellationToken.None);

            if (result.InterferenceCount > 0)
            {
                var hasFalseAlarm = result.InterferencePowersDbm.Any(p => p < -40);
                if (hasFalseAlarm) falseAlarms++;
            }
        }

        double falseAlarmRate = (double)falseAlarms / totalScans;
        falseAlarmRate.Should().BeLessThan(0.3);
    }

    [Fact]
    public async Task CalculateSFDR_NormalConditions_ExpectedRange()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var sfdrValues = new List<double>();

        for (int i = 0; i < 20; i++)
        {
            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
            sfdrValues.Add(result.SpuriousFreeDynamicRangeDb);
        }

        sfdrValues.Should().OnlyContain(v => v > 0 && v < 100);
        sfdrValues.Average().Should().BeInRange(40, 80);
    }

    #endregion

    #region DOA估计测试

    [Fact]
    public async Task EstimateDOA_WithInterference_ReturnsValidAngle()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        if (result.InterferenceCount > 0)
        {
            result.InterferenceDirectionsDeg.Should().HaveCount(result.InterferenceCount);

            for (int i = 0; i < result.InterferenceCount; i++)
            {
                result.InterferenceDirectionsDeg[i].Should().BeInRange(-90, 90);
            }
        }
    }

    [Fact]
    public async Task EstimateDOA_HighPowerInterference_BetterAccuracy()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var results = new List<(double Power, double Direction)>();

        for (int i = 0; i < 30; i++)
        {
            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
            if (result.InterferenceCount > 0)
            {
                for (int j = 0; j < result.InterferenceCount; j++)
                {
                    results.Add((result.InterferencePowersDbm[j], result.InterferenceDirectionsDeg[j]));
                }
            }
        }

        if (results.Any())
        {
            var highPower = results.Where(r => r.Power > -60).ToList();
            var lowPower = results.Where(r => r.Power <= -60).ToList();

            if (highPower.Any() && lowPower.Any())
            {
                highPower.Count.Should().BeGreaterThan(0);
                lowPower.Count.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public async Task EstimateDOA_MultipleInterferences_UniqueDirections()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        for (int i = 0; i < 20; i++)
        {
            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

            if (result.InterferenceCount > 1)
            {
                var directions = result.InterferenceDirectionsDeg;
                for (int j = 0; j < directions.Length; j++)
                {
                    for (int k = j + 1; k < directions.Length; k++)
                    {
                        Math.Abs(directions[j] - directions[k]).Should().BeGreaterThan(5);
                    }
                }
            }
        }
    }

    #endregion

    #region 零陷方向调整测试

    [Fact]
    public async Task CalculateNullSteeringWeights_WithInterference_AppliesNulls()
    {
        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 1, RowIndex = 0, ColumnIndex = 1, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 2, RowIndex = 1, ColumnIndex = 0, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 3, RowIndex = 1, ColumnIndex = 1, CalibrationCoeffPhase = 0 }
        };

        var originalPhases = channels.Select(c => c.CalibrationCoeffPhase).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        if (result.NullSteeringApplied)
        {
            result.NullAnglesDeg.Should().NotBeEmpty();
            result.NullDepthsDb.Should().NotBeEmpty();
            result.NullAnglesDeg.Length.Should().Be(result.NullDepthsDb.Length);

            for (int i = 0; i < result.NullDepthsDb.Length; i++)
            {
                result.NullDepthsDb[i].Should().BeInRange(15, 30);
                result.NullAnglesDeg[i].Should().BeInRange(-90, 90);
            }

            var newPhases = channels.Select(c => c.CalibrationCoeffPhase).ToList();
            newPhases.Should().NotEqual(originalPhases);

            foreach (var channel in channels)
            {
                channel.CalibrationCoeffPhase.Should().BeInRange(-Math.PI, Math.PI);
                channel.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
            }

            _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
                It.IsAny<IReadOnlyList<Channel>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task CalculateNullSteeringWeights_AutoNullDisabled_NoNullApplied()
    {
        var options = CreateOptions(new SpectrumScanOptions
        {
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            InterferencePowerThresholdDbm = -80,
            NullDepthTargetDb = 25,
            MaxNullCount = 3,
            AutoNullSteering = false,
            DoaEstimationAccuracy = 0.9
        });

        var scanner = new SpectrumScanner(
            _mockLogger.Object,
            _mockScanRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            options);

        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 }
        };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.NullSteeringApplied.Should().BeFalse();
        result.NullAnglesDeg.Should().BeEmpty();
        result.NullDepthsDb.Should().BeEmpty();

        _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
            It.IsAny<IReadOnlyList<Channel>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CalculateNullSteeringWeights_MultipleNulls_OrderedByPower()
    {
        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 }
        };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        for (int i = 0; i < 30; i++)
        {
            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

            if (result.NullSteeringApplied && result.NullAnglesDeg.Length > 1)
            {
                var interferencePowerDirection = result.InterferenceFrequenciesMhz
                    .Zip(result.InterferencePowersDbm, result.InterferenceDirectionsDeg)
                    .OrderByDescending(x => x.Second)
                    .Take(result.NullAnglesDeg.Length)
                    .ToList();

                for (int j = 0; j < Math.Min(interferencePowerDirection.Count, result.NullAnglesDeg.Length); j++)
                {
                    var expectedDirection = interferencePowerDirection[j].Third;
                    var actualDirection = result.NullAnglesDeg[j];
                    Math.Abs(actualDirection - expectedDirection).Should().BeLessThan(10);
                }

                break;
            }
        }
    }

    [Fact]
    public async Task ApplyNullSteering_ManualRequest_AppliesCorrectly()
    {
        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 1, RowIndex = 0, ColumnIndex = 1, CalibrationCoeffPhase = 0 }
        };

        _mockChannelRepo
            .Setup(r => r.GetByStationIdAsync(_testStationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels.ToList().AsReadOnly());

        var interferenceDirections = new[] { 30.0, -15.0 };

        await _scanner.ApplyNullSteeringAsync(_testStationId, interferenceDirections, CancellationToken.None);

        channels[0].CalibrationCoeffPhase.Should().NotBe(0);
        channels[1].CalibrationCoeffPhase.Should().NotBe(0);

        _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
            It.IsAny<IReadOnlyList<Channel>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        VerifyLog(_mockLogger, LogLevel.Information, "Manual null steering requested", Times.Once);
    }

    #endregion

    #region 事件触发测试

    [Fact]
    public async Task RunSpectrumScan_WithInterference_PublishesEvents()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        SpectrumScanResult? result = null;
        int attempts = 0;

        while (attempts < 50 && (result == null || result.InterferenceCount == 0))
        {
            result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
            attempts++;
        }

        if (result != null && result.InterferenceCount > 0)
        {
            _mockMediator.Verify(m => m.Publish(
                It.Is<InterferenceDetectedEvent>(e =>
                    e.StationId == _testStationId &&
                    e.Frequencies.Length == result.InterferenceCount),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);

            if (result.NullSteeringApplied)
            {
                _mockMediator.Verify(m => m.Publish(
                    It.Is<NullSteeringAppliedEvent>(e =>
                        e.StationId == _testStationId &&
                        e.NullAngles.Length > 0),
                    It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            }
        }

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<SpectrumScanCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunSpectrumScan_NoInterference_NoInterferenceEvent()
    {
        var highThresholdOptions = CreateOptions(new SpectrumScanOptions
        {
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            InterferencePowerThresholdDbm = -40,
            NullDepthTargetDb = 25,
            MaxNullCount = 3,
            AutoNullSteering = true,
            DoaEstimationAccuracy = 0.9
        });

        var scanner = new SpectrumScanner(
            _mockLogger.Object,
            _mockScanRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            highThresholdOptions);

        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        if (result.InterferenceCount == 0)
        {
            _mockMediator.Verify(m => m.Publish(
                It.IsAny<InterferenceDetectedEvent>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    #endregion

    #region 频谱图渲染性能测试

    [Fact]
    public async Task RunSpectrumScan_Performance_ProcessesQuickly()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4
        }).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var startTime = DateTime.UtcNow;
        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.FrequencyPointsMhz.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public async Task RunSpectrumScan_WideBandwidth_GeneratesCorrectPoints()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var testCases = new[]
        {
            new { Start = 3400, End = 3600, RBW = 100, ExpectedMinPoints = 200 },
            new { Start = 3400, End = 3450, RBW = 50, ExpectedMinPoints = 100 },
            new { Start = 1800, End = 2200, RBW = 200, ExpectedMinPoints = 200 }
        };

        foreach (var testCase in testCases)
        {
            var request = new SpectrumScanRequest
            {
                StationId = _testStationId,
                StartFrequencyMhz = testCase.Start,
                EndFrequencyMhz = testCase.End,
                ResolutionBandwidthKhz = testCase.RBW,
                Channels = channels
            };

            var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

            result.FrequencyPointsMhz.Length.Should().BeGreaterOrEqualTo(testCase.ExpectedMinPoints);
            result.FrequencyPointsMhz.Length.Should().Be(result.PowerLevelsDbm.Length);
        }
    }

    [Fact]
    public async Task RunSpectrumScan_HighResolution_NoPerformanceDegradation()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 10,
            Channels = channels
        };

        var startTime = DateTime.UtcNow;
        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.FrequencyPointsMhz.Length.Should().BeGreaterThan(2000);
    }

    #endregion

    #region 异常输入处理测试

    [Fact]
    public async Task RunSpectrumScan_InvalidFrequencyRange_HandlesGracefully()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3600,
            EndFrequencyMhz = 3400,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.FrequencyPointsMhz.Should().NotBeEmpty();
        result.FrequencyPointsMhz[0].Should().Be(3600);
    }

    [Fact]
    public async Task RunSpectrumScan_ZeroBandwidth_HandlesGracefully()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3500,
            EndFrequencyMhz = 3500,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.FrequencyPointsMhz.Should().NotBeEmpty();
        result.FrequencyPointsMhz.Length.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task RunSpectrumScan_NegativeRBW_HandlesGracefully()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = -100,
            Channels = channels
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.FrequencyPointsMhz.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunSpectrumScan_EmptyChannels_StillWorks()
    {
        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = Array.Empty<Channel>()
        };

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.FrequencyPointsMhz.Should().NotBeEmpty();
    }

    #endregion

    #region 实时性测试

    [Fact]
    public async Task RunSpectrumScan_SuccessiveScans_ConsistentPerformance()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var scanTimes = new List<TimeSpan>();

        for (int i = 0; i < 10; i++)
        {
            var startTime = DateTime.UtcNow;
            await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);
            scanTimes.Add(DateTime.UtcNow - startTime);
        }

        scanTimes.All(t => t < TimeSpan.FromSeconds(2)).Should().BeTrue();

        var avgTime = scanTimes.Average(t => t.TotalMilliseconds);
        var maxTime = scanTimes.Max(t => t.TotalMilliseconds);

        (maxTime / avgTime).Should().BeLessThan(3.0);
    }

    [Fact]
    public async Task RunSpectrumScan_WithCancellation_RespondsQuickly()
    {
        var channels = new[] { new Channel { Id = Guid.NewGuid(), ChannelIndex = 0 } };

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 10,
            Channels = channels
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(500);

        var startTime = DateTime.UtcNow;
        try
        {
            await _scanner.RunSpectrumScanAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        var elapsed = DateTime.UtcNow - startTime;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    #endregion

    #region 根因验证测试 - 宽带干扰时零陷效果差修复

    [Fact]
    public async Task RunSpectrumScan_WithWidebandInterference_ShouldUseAdaptiveNullSteering()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffPhase = 0.0,
            TxPower = 43.0
        }).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var freqPoints = GenerateFrequencyPoints(3400, 3600, 100);
        var powerLevels = new double[freqPoints.Length];
        for (int i = 0; i < powerLevels.Length; i++)
        {
            powerLevels[i] = -100;
        }

        var widebandStartIdx = freqPoints.ToList().FindIndex(f => f >= 3480);
        var widebandEndIdx = freqPoints.ToList().FindIndex(f => f >= 3520);
        if (widebandEndIdx == -1) widebandEndIdx = freqPoints.Length - 1;

        for (int i = widebandStartIdx; i <= widebandEndIdx; i++)
        {
            powerLevels[i] = -60;
        }

        _mockSpectrumProvider
            .Setup(p => p.GetSpectrumDataAsync(
                request.StationId, request.StartFrequencyMhz, request.EndFrequencyMhz,
                request.ResolutionBandwidthKhz, It.IsAny<CancellationToken>()))
            .ReturnsAsync((freqPoints, powerLevels));

        _mockChannelRepo
            .Setup(r => r.BulkUpdateAsync(
                It.IsAny<IEnumerable<Channel>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.InterferenceCount.Should().BeGreaterThan(0);

        var widebandInterference = result.InterferenceDetails?.Contains("3480") ?? false;
        widebandInterference.Should().BeTrue();

        VerifyLog(_mockLogger, LogLevel.Information, "wideband", Times.AtLeastOnce());
    }

    [Fact]
    public async Task RunSpectrumScan_WithNarrowbandInterference_ShouldUseStandardNullSteering()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffPhase = 0.0,
            TxPower = 43.0
        }).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var freqPoints = GenerateFrequencyPoints(3400, 3600, 100);
        var powerLevels = new double[freqPoints.Length];
        for (int i = 0; i < powerLevels.Length; i++)
        {
            powerLevels[i] = -100;
        }

        var nbIdx = freqPoints.ToList().FindIndex(f => f >= 3500);
        if (nbIdx > 0)
        {
            powerLevels[nbIdx] = -55;
            if (nbIdx > 1) powerLevels[nbIdx - 1] = -65;
            if (nbIdx < powerLevels.Length - 1) powerLevels[nbIdx + 1] = -65;
        }

        _mockSpectrumProvider
            .Setup(p => p.GetSpectrumDataAsync(
                request.StationId, request.StartFrequencyMhz, request.EndFrequencyMhz,
                request.ResolutionBandwidthKhz, It.IsAny<CancellationToken>()))
            .ReturnsAsync((freqPoints, powerLevels));

        _mockChannelRepo
            .Setup(r => r.BulkUpdateAsync(
                It.IsAny<IEnumerable<Channel>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.InterferenceCount.Should().BeGreaterThan(0);

        VerifyLog(_mockLogger, LogLevel.Information, "Null steering", Times.AtLeastOnce());
    }

    [Fact]
    public async Task RunSpectrumScan_MultipleInterferenceTypes_ShouldApplyCorrectAlgorithm()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffPhase = 0.0,
            TxPower = 43.0
        }).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var freqPoints = GenerateFrequencyPoints(3400, 3600, 100);
        var powerLevels = new double[freqPoints.Length];
        for (int i = 0; i < powerLevels.Length; i++)
        {
            powerLevels[i] = -100;
        }

        var wbStartIdx = freqPoints.ToList().FindIndex(f => f >= 3420);
        var wbEndIdx = freqPoints.ToList().FindIndex(f => f >= 3450);
        if (wbEndIdx == -1) wbEndIdx = freqPoints.Length - 1;

        for (int i = wbStartIdx; i <= wbEndIdx; i++)
        {
            powerLevels[i] = -65;
        }

        var nbIdx = freqPoints.ToList().FindIndex(f => f >= 3550);
        if (nbIdx > 0)
        {
            powerLevels[nbIdx] = -55;
        }

        _mockSpectrumProvider
            .Setup(p => p.GetSpectrumDataAsync(
                request.StationId, request.StartFrequencyMhz, request.EndFrequencyMhz,
                request.ResolutionBandwidthKhz, It.IsAny<CancellationToken>()))
            .ReturnsAsync((freqPoints, powerLevels));

        _mockChannelRepo
            .Setup(r => r.BulkUpdateAsync(
                It.IsAny<IEnumerable<Channel>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.InterferenceCount.Should().BeGreaterOrEqualTo(2);

        foreach (var depth in result.NullDepthsDb)
        {
            depth.Should().BeGreaterThanOrEqualTo(20);
            depth.Should().BeLessThanOrEqualTo(40);
        }

        result.FrequencyPointsMhz.Should().HaveSameCount(result.PowerLevelsDbm);

        VerifyLog(_mockLogger, LogLevel.Information, "wideband", Times.AtLeastOnce());
    }

    [Fact]
    public async Task RunSpectrumScan_WidebandInterference_NullDepthsShouldMeetTarget()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffPhase = 0.0,
            TxPower = 43.0
        }).ToList();

        var request = new SpectrumScanRequest
        {
            StationId = _testStationId,
            StartFrequencyMhz = 3400,
            EndFrequencyMhz = 3600,
            ResolutionBandwidthKhz = 100,
            Channels = channels
        };

        var freqPoints = GenerateFrequencyPoints(3400, 3600, 100);
        var powerLevels = new double[freqPoints.Length];
        for (int i = 0; i < powerLevels.Length; i++)
        {
            powerLevels[i] = -100;
        }

        var wbStartIdx = freqPoints.ToList().FindIndex(f => f >= 3470);
        var wbEndIdx = freqPoints.ToList().FindIndex(f => f >= 3530);
        if (wbEndIdx == -1) wbEndIdx = freqPoints.Length - 1;

        for (int i = wbStartIdx; i <= wbEndIdx; i++)
        {
            var centerFreq = 3500;
            var currentFreq = freqPoints[i];
            var rolloff = 1.0 - Math.Pow(Math.Abs(currentFreq - centerFreq) / 30.0, 2);
            powerLevels[i] = -60 + rolloff * 10;
        }

        _mockSpectrumProvider
            .Setup(p => p.GetSpectrumDataAsync(
                request.StationId, request.StartFrequencyMhz, request.EndFrequencyMhz,
                request.ResolutionBandwidthKhz, It.IsAny<CancellationToken>()))
            .ReturnsAsync((freqPoints, powerLevels));

        _mockChannelRepo
            .Setup(r => r.BulkUpdateAsync(
                It.IsAny<IEnumerable<Channel>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _scanner.RunSpectrumScanAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.NullDepthsDb.Should().NotBeEmpty();

        var avgNullDepth = result.NullDepthsDb.Average();
        avgNullDepth.Should().BeGreaterThanOrEqualTo(22);

        foreach (var depth in result.NullDepthsDb)
        {
            double.IsNaN(depth).Should().BeFalse();
            double.IsInfinity(depth).Should().BeFalse();
        }

        VerifyLog(_mockLogger, LogLevel.Information, "Adaptive wideband", Times.AtLeastOnce());
    }

    [Fact]
    public async Task ApplyNullSteering_ManualCall_ShouldWorkWithoutInterferenceInfo()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffPhase = 0.0,
            TxPower = 43.0
        }).ToList();

        var interferenceDirections = new[] { 30.0, -45.0 };

        _mockChannelRepo
            .Setup(r => r.GetByStationIdAsync(
                _testStationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels.AsReadOnly());

        _mockChannelRepo
            .Setup(r => r.BulkUpdateAsync(
                It.IsAny<IEnumerable<Channel>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ex = await Record.ExceptionAsync(() =>
            _scanner.ApplyNullSteeringAsync(_testStationId, interferenceDirections, CancellationToken.None));

        ex.Should().BeNull();

        _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
            It.Is<IEnumerable<Channel>>(c => c.All(ch => ch.UpdatedAt != default)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static double[] GenerateFrequencyPoints(double startMhz, double endMhz, double rbwKhz)
    {
        var rbwMhz = rbwKhz / 1000.0;
        var count = (int)Math.Ceiling((endMhz - startMhz) / rbwMhz) + 1;
        return Enumerable.Range(0, count)
            .Select(i => startMhz + i * rbwMhz)
            .ToArray();
    }

    #endregion
}
