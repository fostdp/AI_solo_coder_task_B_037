using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Modules.PaEfficiencyEvaluator;
using AntennaMonitoring.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AntennaMonitoring.Tests.UnitTests;

public class PaEfficiencyEvaluatorTests : TestBase
{
    private readonly Mock<ILogger<PaEfficiencyEvaluator>> _mockLogger;
    private readonly Mock<IPaEfficiencyRecordRepository> _mockEfficiencyRepo;
    private readonly Mock<IChannelRepository> _mockChannelRepo;
    private readonly Mock<IMediator> _mockMediator;
    private readonly IOptions<PaEfficiencyOptions> _options;
    private readonly PaEfficiencyEvaluator _evaluator;
    private readonly Guid _testStationId = Guid.NewGuid();

    public PaEfficiencyEvaluatorTests()
    {
        _mockLogger = CreateMockLogger<PaEfficiencyEvaluator>();
        _mockEfficiencyRepo = new Mock<IPaEfficiencyRecordRepository>();
        _mockChannelRepo = new Mock<IChannelRepository>();
        _mockMediator = new Mock<IMediator>();
        _options = CreateOptions(new PaEfficiencyOptions
        {
            ThresholdPercent = 40.0,
            NominalGainDb = 28.0,
            NominalEfficiencyPercent = 45.0,
            NominalDcVoltageV = 28.0,
            HistoryPoints = 24,
            IntervalMinutes = 5,
            DecayRateAlarmThreshold = 0.001,
            MinimumRemainingHours = 720
        });

        _evaluator = new PaEfficiencyEvaluator(
            _mockLogger.Object,
            _mockEfficiencyRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            _options);
    }

    #region 效率计算公式准确性测试

    [Fact]
    public async Task CalculateEfficiency_NormalConditions_ReturnsExpectedRange()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelId = channel.Id.ToString(),
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(1);
        var result = results[0];

        result.EfficiencyPercent.Should().BeInRange(30, 60);
        result.PowerAddedEfficiencyPercent.Should().BeInRange(20, 50);
        result.GainDb.Should().BeApproximately(28.0, 1.0);
        result.DcPowerW.Should().BeGreaterThan(0);
        result.RfPowerW.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(43.0, 45.0, 40, 50)]
    [InlineData(40.0, 60.0, 30, 45)]
    [InlineData(45.0, 35.0, 42, 52)]
    [InlineData(38.0, 75.0, 25, 40)]
    public async Task CalculateEfficiency_VariousConditions_ExpectedEfficiencyRange(
        double outputPowerDbm, double temperature, double expectedMin, double expectedMax)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = outputPowerDbm
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = outputPowerDbm,
                PaTemperature = temperature,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyPercent.Should().BeInRange(expectedMin, expectedMax);
    }

    [Fact]
    public async Task CalculateEfficiency_FormulaVerification_MathematicallyCorrect()
    {
        var outputPowerDbm = 43.0;
        var temperature = 45.0;
        var nominalGainDb = 28.0;
        var dcVoltage = 28.0;

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = outputPowerDbm
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = outputPowerDbm,
                PaTemperature = temperature,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);
        var result = results[0];

        var expectedOutputPowerW = Math.Pow(10, outputPowerDbm / 10) / 1000;
        var expectedInputPowerDbm = outputPowerDbm - nominalGainDb;
        var expectedInputPowerW = Math.Pow(10, expectedInputPowerDbm / 10) / 1000;
        var expectedDcCurrent = (expectedOutputPowerW / 28.0) * 2.2 * (1 + (temperature - 25) * 0.005);
        var expectedDcPower = expectedDcCurrent * dcVoltage;
        var expectedDrainEfficiency = (expectedOutputPowerW / expectedDcPower) * 100;

        result.OutputPowerDbm.Should().BeApproximately(outputPowerDbm, 0.1);
        result.InputPowerDbm.Should().BeApproximately(expectedInputPowerDbm, 0.1);
        result.RfPowerW.Should().BeApproximately(expectedOutputPowerW, 0.1);
        result.DcCurrentA.Should().BeApproximately(expectedDcCurrent, 0.5);
        result.DcPowerW.Should().BeApproximately(expectedDcPower, 5.0);
    }

    [Fact]
    public async Task CalculateTemperatureDerating_HighTemperature_ReducesEfficiency()
    {
        var temperatures = new[] { 30, 50, 60, 70, 80 };
        var efficiencies = new List<double>();

        foreach (var temp in temperatures)
        {
            var channel = new Channel
            {
                Id = Guid.NewGuid(),
                ChannelIndex = 0,
                TxPower = 43.0
            };

            var recentMetrics = new[]
            {
                new ChannelMetric
                {
                    ChannelIndex = 0,
                    TxPower = 43.0,
                    PaTemperature = temp,
                    Timestamp = DateTime.UtcNow
                }
            };

            var request = new PaEfficiencyRequest
            {
                StationId = _testStationId,
                Channels = new[] { channel },
                RecentMetrics = recentMetrics
            };

            _mockEfficiencyRepo
                .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                    channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

            var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);
            efficiencies.Add(results[0].EfficiencyPercent);
        }

        for (int i = 1; i < efficiencies.Count; i++)
        {
            efficiencies[i].Should().BeLessOrEqualTo(efficiencies[i - 1]);
        }

        efficiencies[0].Should().BeGreaterThan(efficiencies[4]);
    }

    [Fact]
    public async Task CalculatePowerAddedEfficiency_LowGain_LowerPAE()
    {
        var highGainChannel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var lowGainMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 38.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var highGainMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var requestLowGain = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { highGainChannel },
            RecentMetrics = lowGainMetrics
        };

        var requestHighGain = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { highGainChannel },
            RecentMetrics = highGainMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                highGainChannel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var resultsLowGain = await _evaluator.RunEfficiencyEvaluationAsync(requestLowGain, CancellationToken.None);
        var resultsHighGain = await _evaluator.RunEfficiencyEvaluationAsync(requestHighGain, CancellationToken.None);

        resultsLowGain[0].GainDb.Should().BeLessThan(resultsHighGain[0].GainDb);
        resultsLowGain[0].PowerAddedEfficiencyPercent.Should().BeLessThan(resultsHighGain[0].PowerAddedEfficiencyPercent);
    }

    #endregion

    #region 效率衰减趋势预测测试

    [Fact]
    public async Task CalculateEfficiencyDecayRate_StableEfficiency_NearZeroDecay()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var history = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            EfficiencyPercent = 45.0 - i * 0.01,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyDecayRate.Should().BeInRange(-0.001, 0.01);
        results[0].PredictedRemainingHours.Should().BeGreaterThan(720);
        results[0].NeedsReplacement.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateEfficiencyDecayRate_FastDecay_TriggersAlarm()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var history = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            EfficiencyPercent = 50.0 - i * 0.5,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyDecayRate.Should().BeGreaterThan(0.001);
        results[0].PredictedRemainingHours.Should().BeLessThan(720);
        results[0].NeedsReplacement.Should().BeTrue();
    }

    [Fact]
    public async Task PredictRemainingLifetime_HighTemperature_AcceleratedAging()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var history = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            EfficiencyPercent = 45.0 - i * 0.1,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var normalTempMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var highTempMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 80.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var requestNormal = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = normalTempMetrics
        };

        var requestHigh = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = highTempMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.AsReadOnly());

        var resultsNormal = await _evaluator.RunEfficiencyEvaluationAsync(requestNormal, CancellationToken.None);
        var resultsHigh = await _evaluator.RunEfficiencyEvaluationAsync(requestHigh, CancellationToken.None);

        resultsHigh[0].PredictedRemainingHours.Should().BeLessThan(resultsNormal[0].PredictedRemainingHours);
    }

    [Fact]
    public async Task BuildHistoryArray_InsufficientHistory_FillsWithEstimates()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var history = Enumerable.Range(0, 5).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            EfficiencyPercent = 45.0,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyHistory.Should().HaveCount(24);
        results[0].HistoryTimestamps.Should().HaveCount(24);

        var historyValues = results[0].EfficiencyHistory;
        for (int i = 1; i < historyValues.Length; i++)
        {
            historyValues[i].Should().BeGreaterOrEqualTo(historyValues[i - 1] * 0.95);
        }
    }

    #endregion

    #region 更换建议时效性测试

    [Fact]
    public async Task GenerateReplacementReason_BelowThreshold_SuggestsReplacement()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 38.0,
                PaTemperature = 75.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyPercent.Should().BeLessThan(40);
        results[0].NeedsReplacement.Should().BeTrue();
        results[0].ReplacementReason.Should().Contain("效率过低");
    }

    [Fact]
    public async Task GenerateReplacementReason_NormalEfficiency_NoReplacementNeeded()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 40.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyPercent.Should().BeGreaterThan(40);
        results[0].NeedsReplacement.Should().BeFalse();
        results[0].ReplacementReason.Should().Contain("工作正常");
    }

    [Fact]
    public async Task GenerateReplacementReason_HighTemperature_WarnsAboutAging()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 78.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].ReplacementReason.Should().Contain("温度过高");
    }

    [Fact]
    public async Task GenerateReplacementReason_LowGain_WarnsAboutGainReduction()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 37.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].ReplacementReason.Should().Contain("增益偏低");
    }

    [Fact]
    public async Task GenerateReplacementReason_MultipleIssues_ListsAllReasons()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var history = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            EfficiencyPercent = 48.0 - i * 0.4,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 37.0,
                PaTemperature = 78.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].NeedsReplacement.Should().BeTrue();
        var reason = results[0].ReplacementReason;
        reason.Should().Contain("效率过低");
        reason.Should().Contain("温度过高");
        reason.Should().Contain("增益偏低");
        reason.Should().Contain("效率衰减过快");
        reason.Should().Contain("剩余寿命不足");
    }

    #endregion

    #region 事件触发测试

    [Fact]
    public async Task RunEfficiencyEvaluation_NeedsReplacement_PublishesEvent()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var history = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            EfficiencyPercent = 50.0 - i * 0.5,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(history.AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].NeedsReplacement.Should().BeTrue();

        _mockMediator.Verify(m => m.Publish(
            It.Is<PaEfficiencyLowEvent>(e =>
                e.StationId == _testStationId &&
                e.EfficiencyPercent < 40),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<PaEfficiencyCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_NormalEfficiency_NoLowEfficiencyEvent()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 40.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].NeedsReplacement.Should().BeFalse();

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<PaEfficiencyLowEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_MultipleChannelsSomeFail_MultipleEvents()
    {
        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, TxPower = 43.0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 1, TxPower = 43.0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 2, TxPower = 43.0 }
        };

        var badHistory = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = channels[0].Id,
            EfficiencyPercent = 50.0 - i * 0.5,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var goodHistory = Enumerable.Range(0, 24).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            EfficiencyPercent = 45.0,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new[]
        {
            new ChannelMetric { ChannelIndex = 0, TxPower = 43.0, PaTemperature = 45.0, Timestamp = DateTime.UtcNow },
            new ChannelMetric { ChannelIndex = 1, TxPower = 43.0, PaTemperature = 40.0, Timestamp = DateTime.UtcNow },
            new ChannelMetric { ChannelIndex = 2, TxPower = 43.0, PaTemperature = 42.0, Timestamp = DateTime.UtcNow }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .SetupSequence(r => r.GetByChannelIdAndTimeRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(badHistory.AsReadOnly())
            .ReturnsAsync(goodHistory.AsReadOnly())
            .ReturnsAsync(goodHistory.AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Count(r => r.NeedsReplacement).Should().Be(1);
        results.Count(r => !r.NeedsReplacement).Should().Be(2);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<PaEfficiencyLowEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 异常输入处理测试

    [Fact]
    public async Task RunEfficiencyEvaluation_EmptyChannels_ReturnsEmpty()
    {
        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = Array.Empty<Channel>(),
            RecentMetrics = Array.Empty<ChannelMetric>()
        };

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().BeEmpty();

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<PaEfficiencyCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_NegativePower_HandlesGracefully()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = -10.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = -10.0,
                PaTemperature = 45.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].EfficiencyPercent.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_ExtremeTemperature_Capped()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = 0,
            TxPower = 43.0
        };

        var recentMetrics = new[]
        {
            new ChannelMetric
            {
                ChannelIndex = 0,
                TxPower = 43.0,
                PaTemperature = 150.0,
                Timestamp = DateTime.UtcNow
            }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = new[] { channel },
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                channel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results[0].EfficiencyPercent.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_SingleChannelException_ContinuesProcessing()
    {
        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, TxPower = 43.0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 1, TxPower = 43.0 }
        };

        var recentMetrics = new[]
        {
            new ChannelMetric { ChannelIndex = 0, TxPower = 43.0, PaTemperature = 45.0, Timestamp = DateTime.UtcNow },
            new ChannelMetric { ChannelIndex = 1, TxPower = 43.0, PaTemperature = 45.0, Timestamp = DateTime.UtcNow }
        };

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .SetupSequence(r => r.GetByChannelIdAndTimeRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Count.Should().BeGreaterOrEqualTo(1);

        VerifyLog(_mockLogger, LogLevel.Error, "Error evaluating PA efficiency", Times.Once);
    }

    #endregion

    #region 性能测试

    [Fact]
    public async Task RunEfficiencyEvaluation_Performance_ProcessesQuickly()
    {
        var channels = Enumerable.Range(0, 64).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 8,
            ColumnIndex = i % 8,
            TxPower = 43.0 - i * 0.01
        }).ToList();

        var recentMetrics = channels.Select(c => new ChannelMetric
        {
            ChannelIndex = c.ChannelIndex,
            TxPower = c.TxPower ?? 43.0,
            PaTemperature = 40.0 + c.ChannelIndex * 0.5,
            Timestamp = DateTime.UtcNow
        }).ToList();

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var startTime = DateTime.UtcNow;
        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        results.Should().HaveCount(64);
    }

    #endregion

    #region 根因验证测试 - 温度传感器漂移时偏差大修复

    [Fact]
    public async Task RunEfficiencyEvaluation_WithDriftedTemperature_ShouldApplyCalibration()
    {
        var channels = Enumerable.Range(0, 8).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            TxPower = 43.0
        }).ToList();

        var targetChannel = channels[3];

        var historyRecords = Enumerable.Range(0, 10).Select(i => new PaEfficiencyRecord
        {
            Id = Guid.NewGuid(),
            StationId = _testStationId,
            ChannelId = targetChannel.Id,
            ChannelIndex = targetChannel.ChannelIndex,
            PaTemperature = 45.0,
            EfficiencyPercent = 42.0,
            MeasurementTime = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        var recentMetrics = new List<ChannelMetric>();

        for (int i = 0; i < 8; i++)
        {
            var channel = channels[i];
            var temp = i == 3 ? 45.0 + 10.0 : 45.0;

            for (int j = 0; j < 5; j++)
            {
                recentMetrics.Add(new ChannelMetric
                {
                    ChannelIndex = channel.ChannelIndex,
                    ChannelId = channel.Id.ToString(),
                    TxPower = 43.0,
                    PaTemperature = temp + (j - 2) * 0.5,
                    Timestamp = DateTime.UtcNow.AddMinutes(-j)
                });
            }
        }

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                targetChannel.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(historyRecords.AsReadOnly());

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                It.Is<Guid>(id => id != targetChannel.Id), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(8);

        var targetResult = results.First(r => r.ChannelIndex == 3);

        targetResult.TemperatureDriftDetected.Should().BeTrue();
        targetResult.TemperatureDriftAmount.Should().BeApproximately(10.0, 3.0);
        targetResult.RawPaTemperature.Should().BeApproximately(55.0, 1.0);
        targetResult.PaTemperature.Should().BeLessThan(targetResult.RawPaTemperature);
        targetResult.PaTemperature.Should().BeApproximately(48.0, 3.0);

        targetResult.EfficiencyPercent.Should().BeGreaterThan(0);
        targetResult.EfficiencyPercent.Should().BeLessThan(100);

        VerifyLog(_mockLogger, LogLevel.Warning, "drift", Times.AtLeastOnce());
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_StableTemperature_ShouldNotApplyCalibration()
    {
        var channels = Enumerable.Range(0, 8).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            TxPower = 43.0
        }).ToList();

        var recentMetrics = new List<ChannelMetric>();

        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                recentMetrics.Add(new ChannelMetric
                {
                    ChannelIndex = i,
                    ChannelId = channels[i].Id.ToString(),
                    TxPower = 43.0,
                    PaTemperature = 45.0 + (j - 2) * 0.3,
                    Timestamp = DateTime.UtcNow.AddMinutes(-j)
                });
            }
        }

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(8);

        foreach (var result in results)
        {
            result.TemperatureDriftDetected.Should().BeFalse();
            result.TemperatureDriftAmount.Should().BeApproximately(0, 0.5);
            result.PaTemperature.Should().BeApproximately(result.RawPaTemperature, 0.5);
            result.EfficiencyPercent.Should().BeGreaterThan(0);
            result.EfficiencyPercent.Should().BeLessThan(100);
        }
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_MultipleChannelsWithDrift_ShouldCalibrateEach()
    {
        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            TxPower = 43.0
        }).ToList();

        var recentMetrics = new List<ChannelMetric>();

        for (int i = 0; i < 16; i++)
        {
            double baseTemp;
            if (i == 2 || i == 7 || i == 12)
            {
                baseTemp = 45.0 + 8.0;
            }
            else
            {
                baseTemp = 45.0;
            }

            for (int j = 0; j < 5; j++)
            {
                recentMetrics.Add(new ChannelMetric
                {
                    ChannelIndex = i,
                    ChannelId = channels[i].Id.ToString(),
                    TxPower = 43.0,
                    PaTemperature = baseTemp + (j - 2) * 0.5,
                    Timestamp = DateTime.UtcNow.AddMinutes(-j)
                });
            }
        }

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(16);

        var driftChannels = results.Where(r => r.TemperatureDriftDetected).ToList();
        driftChannels.Count.Should().BeGreaterOrEqualTo(3);

        foreach (var result in driftChannels)
        {
            result.PaTemperature.Should().BeLessThan(result.RawPaTemperature);
            result.TemperatureDriftAmount.Should().BeGreaterThan(3.0);
        }

        foreach (var result in results)
        {
            double.IsNaN(result.EfficiencyPercent).Should().BeFalse();
            double.IsInfinity(result.EfficiencyPercent).Should().BeFalse();
            result.EfficiencyPercent.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task RunEfficiencyEvaluation_CalibratedVsRaw_ShouldShowImprovedAccuracy()
    {
        var channels = Enumerable.Range(0, 4).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 2,
            ColumnIndex = i % 2,
            TxPower = 43.0
        }).ToList();

        var recentMetrics = new List<ChannelMetric>();

        for (int i = 0; i < 4; i++)
        {
            double drift = i == 1 ? 15.0 : 0;

            for (int j = 0; j < 10; j++)
            {
                recentMetrics.Add(new ChannelMetric
                {
                    ChannelIndex = i,
                    ChannelId = channels[i].Id.ToString(),
                    TxPower = 43.0,
                    PaTemperature = 45.0 + drift + (j - 5) * 0.3,
                    Timestamp = DateTime.UtcNow.AddMinutes(-j)
                });
            }
        }

        var request = new PaEfficiencyRequest
        {
            StationId = _testStationId,
            Channels = channels,
            RecentMetrics = recentMetrics
        };

        _mockEfficiencyRepo
            .Setup(r => r.GetByChannelIdAndTimeRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaEfficiencyRecord>().AsReadOnly());

        var results = await _evaluator.RunEfficiencyEvaluationAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(4);

        var driftedChannel = results.First(r => r.ChannelIndex == 1);
        var normalChannel = results.First(r => r.ChannelIndex == 0);

        driftedChannel.TemperatureDriftDetected.Should().BeTrue();
        normalChannel.TemperatureDriftDetected.Should().BeFalse();

        var rawDriftedEfficiency = driftedChannel.EfficiencyPercent;
        var expectedDriftedEfficiencyWithCalibration = driftedChannel.EfficiencyPercent;

        expectedDriftedEfficiencyWithCalibration.Should().BeGreaterThan(30);
        expectedDriftedEfficiencyWithCalibration.Should().BeLessThan(50);

        driftedChannel.PaTemperature.Should().BeLessThan(driftedChannel.RawPaTemperature);
    }

    #endregion
}
