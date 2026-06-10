using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Modules.CoSiteInterferenceAnalyzer;
using AntennaMonitoring.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AntennaMonitoring.Tests.UnitTests;

public class CoSiteInterferenceAnalyzerTests : TestBase
{
    private readonly Mock<ILogger<CoSiteInterferenceAnalyzer>> _mockLogger;
    private readonly Mock<ICoSiteInterferenceRecordRepository> _mockInterferenceRepo;
    private readonly Mock<ICoSiteAntennaRepository> _mockCositeAntennaRepo;
    private readonly Mock<IMediator> _mockMediator;
    private readonly IOptions<CoSiteInterferenceOptions> _options;
    private readonly CoSiteInterferenceAnalyzer _analyzer;
    private readonly Guid _testStationId = Guid.NewGuid();

    public CoSiteInterferenceAnalyzerTests()
    {
        _mockLogger = CreateMockLogger<CoSiteInterferenceAnalyzer>();
        _mockInterferenceRepo = new Mock<ICoSiteInterferenceRecordRepository>();
        _mockCositeAntennaRepo = new Mock<ICoSiteAntennaRepository>();
        _mockMediator = new Mock<IMediator>();
        _options = CreateOptions(new CoSiteInterferenceOptions
        {
            IsolationThresholdDb = 30.0,
            FrequencyOverlapThreshold = 0.1,
            CouplingModelAccuracy = 0.85
        });

        _analyzer = new CoSiteInterferenceAnalyzer(
            _mockLogger.Object,
            _mockInterferenceRepo.Object,
            _mockCositeAntennaRepo.Object,
            _mockMediator.Object,
            _options);
    }

    #region 互耦模型计算测试

    [Fact]
    public async Task CalculateIsolation_NormalConditions_ReturnsExpectedRange()
    {
        var interferingAntenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G-Macro",
            FrequencyStartMhz = 3500,
            FrequencyEndMhz = 3600,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 5.0,
            AzimuthAngleDeg = 45,
            ElevationAngleDeg = 10,
            HeightOffsetMeters = 2.0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { interferingAntenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(1);
        var result = results[0];

        result.IsolationDb.Should().BeInRange(20, 100);
        result.CouplingCoefficient.Should().BeInRange(0, 1);
        result.VectorX.Should().BeInRange(-1, 1);
        result.VectorY.Should().BeInRange(-1, 1);
        result.VectorZ.Should().BeInRange(-1, 1);
    }

    [Theory]
    [InlineData(1.0, 0, 0, 0, 3400, 3500, 3450, 3550, 20, 40)]
    [InlineData(3.0, 45, 10, 1.0, 3400, 3500, 3400, 3500, 15, 35)]
    [InlineData(10.0, 90, 30, 3.0, 1800, 1900, 3400, 3500, 40, 80)]
    public async Task CalculateIsolation_VariousConditions_ExpectedIsolationRange(
        double distance, double azimuth, double elevation, double heightOffset,
        double interfererStart, double interfererEnd, double selfStart, double selfEnd,
        double expectedMin, double expectedMax)
    {
        var interferingAntenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国联通",
            AntennaType = "LTE",
            FrequencyStartMhz = interfererStart,
            FrequencyEndMhz = interfererEnd,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = distance,
            AzimuthAngleDeg = azimuth,
            ElevationAngleDeg = elevation,
            HeightOffsetMeters = heightOffset
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { interferingAntenna },
            SelfFrequencyStartMhz = selfStart,
            SelfFrequencyEndMhz = selfEnd
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsolationDb.Should().BeInRange(expectedMin, expectedMax);
    }

    [Fact]
    public async Task CalculateIsolation_LargeDistance_HighIsolation()
    {
        var interferingAntenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国电信",
            AntennaType = "5G",
            FrequencyStartMhz = 2500,
            FrequencyEndMhz = 2600,
            TransmitPowerDbm = 40,
            SeparationDistanceMeters = 50.0,
            AzimuthAngleDeg = 180,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 5.0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { interferingAntenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3600
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsolationDb.Should().BeGreaterThan(60);
        results[0].IsIsolationSufficient.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateIsolation_CloseDistance_LowIsolation()
    {
        var interferingAntenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G-Macro",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 46,
            SeparationDistanceMeters = 0.5,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { interferingAntenna },
            SelfFrequencyStartMhz = 3450,
            SelfFrequencyEndMhz = 3550
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsolationDb.Should().BeLessThan(30);
        results[0].IsIsolationSufficient.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateMutualCouplingLoss_VariousDistances_ExpectedTrend()
    {
        var distances = new[] { 0.5, 1.0, 2.0, 5.0, 10.0 };
        var isolations = new List<double>();

        foreach (var distance in distances)
        {
            var antenna = new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "测试",
                AntennaType = "Test",
                FrequencyStartMhz = 3500,
                FrequencyEndMhz = 3600,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = distance,
                AzimuthAngleDeg = 0,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 0
            };

            var request = new CoSiteInterferenceRequest
            {
                StationId = _testStationId,
                CoSiteAntennas = new[] { antenna },
                SelfFrequencyStartMhz = 3400,
                SelfFrequencyEndMhz = 3500
            };

            var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);
            isolations.Add(results[0].IsolationDb);
        }

        for (int i = 1; i < isolations.Count; i++)
        {
            isolations[i].Should().BeGreaterThan(isolations[i - 1]);
        }
    }

    #endregion

    #region S参数实测偏差验证

    [Fact]
    public async Task CalculateCouplingCoefficient_WithSParaDeviation_WithinExpectedRange()
    {
        var testCases = new[]
        {
            new { Distance = 1.0, FreqStart = 3400, FreqEnd = 3500, ExpectedMin = 0.001, ExpectedMax = 0.1 },
            new { Distance = 3.0, FreqStart = 3400, FreqEnd = 3500, ExpectedMin = 0.0001, ExpectedMax = 0.01 },
            new { Distance = 10.0, FreqStart = 1800, FreqEnd = 1900, ExpectedMin = 1e-7, ExpectedMax = 1e-4 }
        };

        foreach (var testCase in testCases)
        {
            var antenna = new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "TestOp",
                AntennaType = "Test",
                FrequencyStartMhz = testCase.FreqStart,
                FrequencyEndMhz = testCase.FreqEnd,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = testCase.Distance,
                AzimuthAngleDeg = 0,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 0
            };

            var request = new CoSiteInterferenceRequest
            {
                StationId = _testStationId,
                CoSiteAntennas = new[] { antenna },
                SelfFrequencyStartMhz = 3400,
                SelfFrequencyEndMhz = 3500
            };

            var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);
            var coupling = results[0].CouplingCoefficient;

            coupling.Should().BeInRange(testCase.ExpectedMin, testCase.ExpectedMax);

            var sParamFromCoupling = -20 * Math.Log10(coupling);
            sParamFromCoupling.Should().BeInRange(20, 100);
        }
    }

    [Fact]
    public async Task CalculateIsolation_WithFrequencyOverlap_IncreasedCoupling()
    {
        var noOverlapAntenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "NoOverlap",
            AntennaType = "Test",
            FrequencyStartMhz = 1800,
            FrequencyEndMhz = 1900,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 3.0,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var fullOverlapAntenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "FullOverlap",
            AntennaType = "Test",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 3.0,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var requestNoOverlap = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { noOverlapAntenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var requestFullOverlap = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { fullOverlapAntenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var resultsNoOverlap = await _analyzer.RunInterferenceAnalysisAsync(requestNoOverlap, CancellationToken.None);
        var resultsFullOverlap = await _analyzer.RunInterferenceAnalysisAsync(requestFullOverlap, CancellationToken.None);

        resultsNoOverlap[0].IsolationDb.Should().BeGreaterThan(resultsFullOverlap[0].IsolationDb);
        resultsNoOverlap[0].CouplingCoefficient.Should().BeLessThan(resultsFullOverlap[0].CouplingCoefficient);
    }

    #endregion

    #region 调整建议测试

    [Fact]
    public async Task GenerateRecommendation_SufficientIsolation_NoActionNeeded()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G",
            FrequencyStartMhz = 1800,
            FrequencyEndMhz = 1900,
            TransmitPowerDbm = 40,
            SeparationDistanceMeters = 10.0,
            AzimuthAngleDeg = 90,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 3.0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3600
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsIsolationSufficient.Should().BeTrue();
        results[0].Recommendation.Should().Contain("无需额外措施");
    }

    [Fact]
    public async Task GenerateRecommendation_InsufficientIsolation_SuggestsDistanceIncrease()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国联通",
            AntennaType = "5G-Macro",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 46,
            SeparationDistanceMeters = 1.0,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0.5
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3450,
            SelfFrequencyEndMhz = 3550
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsIsolationSufficient.Should().BeFalse();
        results[0].Recommendation.Should().Contain("增加天线间距");
        results[0].Recommendation.Should().Contain("≥3.0m");
    }

    [Fact]
    public async Task GenerateRecommendation_HighFrequencyOverlap_SuggestsFilter()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国电信",
            AntennaType = "5G",
            FrequencyStartMhz = 3420,
            FrequencyEndMhz = 3480,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 5.0,
            AzimuthAngleDeg = 45,
            ElevationAngleDeg = 5,
            HeightOffsetMeters = 2.0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].Recommendation.Should().ContainAny("频率规划", "滤波器");
    }

    [Fact]
    public async Task GenerateRecommendation_SmallHeightOffset_SuggestsVerticalIsolation()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 3.0,
            AzimuthAngleDeg = 45,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0.3
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].Recommendation.Should().Contain("垂直隔离");
        results[0].Recommendation.Should().Contain("≥1.0m");
    }

    [Fact]
    public async Task GenerateRecommendation_SmallAzimuth_SuggestsAzimuthAdjustment()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "广电",
            AntennaType = "5G",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = 3.0,
            AzimuthAngleDeg = 15,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 2.0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].Recommendation.Should().Contain("方位角");
    }

    [Fact]
    public async Task GenerateRecommendation_SevereInterference_WarnsImmediately()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G-Macro",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 48,
            SeparationDistanceMeters = 0.3,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsolationDb.Should().BeLessThan(20);
        results[0].Recommendation.Should().Contain("⚠️");
        results[0].Recommendation.Should().Contain("立即");
    }

    #endregion

    #region 干扰矢量可视化测试

    [Fact]
    public async Task CalculateInterferenceVector_UnitVector_NormalizedCorrectly()
    {
        var testCases = new[]
        {
            new { Azimuth = 0.0, Elevation = 0.0, ExpectedX = 1.0, ExpectedY = 0.0, ExpectedZ = 0.0 },
            new { Azimuth = 90.0, Elevation = 0.0, ExpectedX = 0.0, ExpectedY = 1.0, ExpectedZ = 0.0 },
            new { Azimuth = 0.0, Elevation = 90.0, ExpectedX = 0.0, ExpectedY = 0.0, ExpectedZ = 1.0 },
            new { Azimuth = 180.0, Elevation = 0.0, ExpectedX = -1.0, ExpectedY = 0.0, ExpectedZ = 0.0 }
        };

        foreach (var testCase in testCases)
        {
            var antenna = new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Test",
                AntennaType = "Test",
                FrequencyStartMhz = 3500,
                FrequencyEndMhz = 3600,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = 5.0,
                AzimuthAngleDeg = testCase.Azimuth,
                ElevationAngleDeg = testCase.Elevation,
                HeightOffsetMeters = 0
            };

            var request = new CoSiteInterferenceRequest
            {
                StationId = _testStationId,
                CoSiteAntennas = new[] { antenna },
                SelfFrequencyStartMhz = 3400,
                SelfFrequencyEndMhz = 3500
            };

            var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);
            var result = results[0];

            var magnitude = Math.Sqrt(
                result.VectorX * result.VectorX +
                result.VectorY * result.VectorY +
                result.VectorZ * result.VectorZ);

            magnitude.Should().BeApproximately(1.0, 0.001);

            result.VectorX.Should().BeApproximately(testCase.ExpectedX, 0.001);
            result.VectorY.Should().BeApproximately(testCase.ExpectedY, 0.001);
            result.VectorZ.Should().BeApproximately(testCase.ExpectedZ, 0.001);
        }
    }

    [Fact]
    public async Task CalculateInterferenceVector_VariousDistances_DirectionConsistent()
    {
        var distances = new[] { 1.0, 3.0, 5.0, 10.0 };
        var previousVector = (X: 0.0, Y: 0.0, Z: 0.0);
        var isFirst = true;

        foreach (var distance in distances)
        {
            var antenna = new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Test",
                AntennaType = "Test",
                FrequencyStartMhz = 3500,
                FrequencyEndMhz = 3600,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = distance,
                AzimuthAngleDeg = 45,
                ElevationAngleDeg = 30,
                HeightOffsetMeters = 0
            };

            var request = new CoSiteInterferenceRequest
            {
                StationId = _testStationId,
                CoSiteAntennas = new[] { antenna },
                SelfFrequencyStartMhz = 3400,
                SelfFrequencyEndMhz = 3500
            };

            var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);
            var result = results[0];

            if (isFirst)
            {
                previousVector = (result.VectorX, result.VectorY, result.VectorZ);
                isFirst = false;
            }
            else
            {
                result.VectorX.Should().BeApproximately(previousVector.X, 0.001);
                result.VectorY.Should().BeApproximately(previousVector.Y, 0.001);
                result.VectorZ.Should().BeApproximately(previousVector.Z, 0.001);
            }
        }
    }

    #endregion

    #region 事件触发测试

    [Fact]
    public async Task RunInterferenceAnalysis_InsufficientIsolation_PublishesEvent()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G-Macro",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 46,
            SeparationDistanceMeters = 1.0,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results[0].IsIsolationSufficient.Should().BeFalse();

        _mockMediator.Verify(m => m.Publish(
            It.Is<IsolationInsufficientEvent>(e =>
                e.StationId == _testStationId &&
                e.IsolationDb < 30),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<CoSiteInterferenceCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunInterferenceAnalysis_SufficientIsolation_NoInsufficientEvent()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "中国移动",
            AntennaType = "5G",
            FrequencyStartMhz = 1800,
            FrequencyEndMhz = 1900,
            TransmitPowerDbm = 40,
            SeparationDistanceMeters = 10.0,
            AzimuthAngleDeg = 90,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 3.0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3600
        };

        await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<IsolationInsufficientEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunInterferenceAnalysis_MultipleAntennasSomeInsufficient_MultipleEvents()
    {
        var antennas = new[]
        {
            new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Good",
                AntennaType = "Test",
                FrequencyStartMhz = 1800,
                FrequencyEndMhz = 1900,
                TransmitPowerDbm = 40,
                SeparationDistanceMeters = 10.0,
                AzimuthAngleDeg = 90,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 3.0
            },
            new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Bad1",
                AntennaType = "Test",
                FrequencyStartMhz = 3400,
                FrequencyEndMhz = 3500,
                TransmitPowerDbm = 46,
                SeparationDistanceMeters = 1.0,
                AzimuthAngleDeg = 0,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 0
            },
            new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Bad2",
                AntennaType = "Test",
                FrequencyStartMhz = 3450,
                FrequencyEndMhz = 3550,
                TransmitPowerDbm = 45,
                SeparationDistanceMeters = 1.5,
                AzimuthAngleDeg = 10,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 0.5
            }
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = antennas,
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results.Count(r => r.IsIsolationSufficient).Should().Be(1);
        results.Count(r => !r.IsIsolationSufficient).Should().Be(2);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<IsolationInsufficientEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region 异常输入处理测试

    [Fact]
    public async Task RunInterferenceAnalysis_EmptyAntennas_ReturnsEmpty()
    {
        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = Array.Empty<CoSiteAntenna>(),
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3600
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results.Should().BeEmpty();

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<CoSiteInterferenceCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunInterferenceAnalysis_NegativeDistance_HandlesGracefully()
    {
        var antenna = new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = "Test",
            AntennaType = "Test",
            FrequencyStartMhz = 3400,
            FrequencyEndMhz = 3500,
            TransmitPowerDbm = 43,
            SeparationDistanceMeters = -5.0,
            AzimuthAngleDeg = 0,
            ElevationAngleDeg = 0,
            HeightOffsetMeters = 0
        };

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = new[] { antenna },
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].IsolationDb.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task RunInterferenceAnalysis_ExtremeAzimuth_NormalizedCorrectly()
    {
        var testAngles = new[] { -180, -90, 0, 90, 180, 270, 360 };

        foreach (var angle in testAngles)
        {
            var antenna = new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Test",
                AntennaType = "Test",
                FrequencyStartMhz = 3400,
                FrequencyEndMhz = 3500,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = 5.0,
                AzimuthAngleDeg = angle,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 0
            };

            var request = new CoSiteInterferenceRequest
            {
                StationId = _testStationId,
                CoSiteAntennas = new[] { antenna },
                SelfFrequencyStartMhz = 3400,
                SelfFrequencyEndMhz = 3500
            };

            var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

            results[0].IsolationDb.Should().BeInRange(0, 100);

            var magnitude = Math.Sqrt(
                results[0].VectorX * results[0].VectorX +
                results[0].VectorY * results[0].VectorY +
                results[0].VectorZ * results[0].VectorZ);

            magnitude.Should().BeApproximately(1.0, 0.001);
        }
    }

    [Fact]
    public async Task RunInterferenceAnalysis_SingleAntennaException_ContinuesProcessing()
    {
        var antennas = new[]
        {
            new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Antenna1",
                AntennaType = "Test",
                FrequencyStartMhz = 3400,
                FrequencyEndMhz = 3500,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = 5.0,
                AzimuthAngleDeg = 0,
                ElevationAngleDeg = 0,
                HeightOffsetMeters = 0
            },
            new CoSiteAntenna
            {
                Id = Guid.NewGuid(),
                OperatorName = "Antenna2",
                AntennaType = "Test",
                FrequencyStartMhz = 3400,
                FrequencyEndMhz = 3500,
                TransmitPowerDbm = 43,
                SeparationDistanceMeters = 3.0,
                AzimuthAngleDeg = 45,
                ElevationAngleDeg = 10,
                HeightOffsetMeters = 1.0
            }
        };

        _mockInterferenceRepo
            .SetupSequence(r => r.AddAsync(It.IsAny<CoSiteInterferenceRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"))
            .ReturnsAsync(new CoSiteInterferenceRecord { Id = Guid.NewGuid() });

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = antennas,
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3500
        };

        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Count.Should().BeGreaterOrEqualTo(1);

        VerifyLog(_mockLogger, LogLevel.Error, "Error analyzing interference", Times.Once);
    }

    #endregion

    #region 性能测试

    [Fact]
    public async Task RunInterferenceAnalysis_Performance_ProcessesQuickly()
    {
        var antennas = Enumerable.Range(0, 50).Select(i => new CoSiteAntenna
        {
            Id = Guid.NewGuid(),
            OperatorName = $"Op{i}",
            AntennaType = "Test",
            FrequencyStartMhz = 3400 + i * 10,
            FrequencyEndMhz = 3450 + i * 10,
            TransmitPowerDbm = 40 + i % 10,
            SeparationDistanceMeters = 1.0 + i * 0.5,
            AzimuthAngleDeg = i * 7.2,
            ElevationAngleDeg = i % 30,
            HeightOffsetMeters = i % 5
        }).ToList();

        var request = new CoSiteInterferenceRequest
        {
            StationId = _testStationId,
            CoSiteAntennas = antennas,
            SelfFrequencyStartMhz = 3400,
            SelfFrequencyEndMhz = 3600
        };

        var startTime = DateTime.UtcNow;
        var results = await _analyzer.RunInterferenceAnalysisAsync(request, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        results.Should().HaveCount(50);
    }

    #endregion
}
