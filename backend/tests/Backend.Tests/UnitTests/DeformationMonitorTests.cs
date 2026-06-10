using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using DeformationMonitor.Module;
using DeformationMonitor.Module.Models;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

using DeformationMonitor = DeformationMonitor.Module.DeformationMonitor;
using IDeformationMonitor = DeformationMonitor.Module.IDeformationMonitor;
using DeformationOptions = DeformationMonitor.Module.Models.DeformationOptions;
using SensorData = DeformationMonitor.Module.Models.SensorData;
using DeformationRequest = DeformationMonitor.Module.Models.DeformationRequest;
using DeformationResult = DeformationMonitor.Module.Models.DeformationResult;

namespace AntennaMonitoring.Tests.UnitTests;

public class DeformationMonitorTests : TestBase
{
    private readonly Mock<ILogger<DeformationMonitor>> _mockLogger;
    private readonly Mock<IDeformationRecordRepository> _mockDeformationRepo;
    private readonly Mock<IChannelRepository> _mockChannelRepo;
    private readonly Mock<IMediator> _mockMediator;
    private readonly IOptions<DeformationOptions> _options;
    private readonly DeformationMonitor _monitor;
    private readonly Guid _testStationId = Guid.NewGuid();

    public DeformationMonitorTests()
    {
        _mockLogger = CreateMockLogger<DeformationMonitor>();
        _mockDeformationRepo = new Mock<IDeformationRecordRepository>();
        _mockChannelRepo = new Mock<IChannelRepository>();
        _mockMediator = new Mock<IMediator>();
        _options = CreateOptions(new DeformationOptions
        {
            ThresholdMm = 0.5,
            YoungModulusGpa = 70.0,
            PoissonRatio = 0.33,
            PlateThicknessMm = 15.0,
            AutoBeamCorrection = true
        });

        _monitor = new DeformationMonitor(
            _mockLogger.Object,
            _mockDeformationRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            _options);
    }

    #region FEM形变计算精度测试

    [Fact]
    public async Task CalculateDisplacementFEM_NormalConditions_ReturnsExpectedRange()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005,
            WindSpeed = 10,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(1);
        var result = results[0];

        result.CalculatedDisplacementMm.Should().BeGreaterThan(0);
        result.CalculatedDisplacementMm.Should().BeLessThan(10);
        result.StressMpa.Should().BeGreaterThan(0);
        result.StressMpa.Should().BeLessThan(200);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0001, 0, 0.01, 0.1)]
    [InlineData(1.0, 0.5, 0.2, 0.001, 15, 0.3, 2.0)]
    [InlineData(3.0, 2.0, 1.0, 0.005, 30, 1.5, 5.0)]
    public async Task CalculateDisplacementFEM_VariousConditions_LinearResponse(
        double tiltX, double tiltY, double tiltZ, double strain, double windSpeed,
        double expectedMin, double expectedMax)
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = tiltX,
            TiltAngleY = tiltY,
            TiltAngleZ = tiltZ,
            StrainValue = strain,
            WindSpeed = windSpeed,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);
        var displacement = results[0].CalculatedDisplacementMm;

        displacement.Should().BeInRange(expectedMin, expectedMax);
    }

    [Fact]
    public async Task CalculateDisplacementFEM_ZeroInput_ReturnsNearZero()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0,
            TiltAngleY = 0,
            TiltAngleZ = 0,
            StrainValue = 0,
            WindSpeed = 0,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].CalculatedDisplacementMm.Should().BeInRange(0, 0.05);
        results[0].ExceedsThreshold.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateDisplacementFEM_ExtremeConditions_CappedAtMaximum()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 10,
            TiltAngleY = 10,
            TiltAngleZ = 10,
            StrainValue = 0.01,
            WindSpeed = 50,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].CalculatedDisplacementMm.Should().BeLessOrEqualTo(10.0);
    }

    [Fact]
    public async Task CalculateDisplacementFEM_MultipleSensors_ConsistentResults()
    {
        var sensorDatas = Enumerable.Range(0, 9).Select(i => new SensorData
        {
            StationId = _testStationId,
            SensorIndex = i,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005 + i * 0.00005,
            WindSpeed = 10,
            Temperature = 25
        }).ToList();

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().HaveCount(9);
        results.Select(r => r.CalculatedDisplacementMm).Should().OnlyContain(d => d > 0);

        var centerSensor = results.First(r => r.SensorIndex == 4);
        var edgeSensors = results.Where(r => r.SensorIndex != 4);

        centerSensor.CalculatedDisplacementMm.Should().BeGreaterThan(edgeSensors.Min(r => r.CalculatedDisplacementMm));
    }

    #endregion

    #region 波束指向修正测试

    [Fact]
    public async Task ApplyBeamCorrection_ExceedsThreshold_AppliesCorrection()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 1.5,
            TiltAngleY = 1.0,
            TiltAngleZ = 0.5,
            StrainValue = 0.003,
            WindSpeed = 20,
            Temperature = 25
        };

        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 1, RowIndex = 0, ColumnIndex = 1, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 2, RowIndex = 1, ColumnIndex = 0, CalibrationCoeffPhase = 0 },
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 3, RowIndex = 1, ColumnIndex = 1, CalibrationCoeffPhase = 0 }
        };

        _mockChannelRepo
            .Setup(r => r.GetByStationIdAsync(_testStationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels.ToList().AsReadOnly());

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = channels
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].ExceedsThreshold.Should().BeTrue();
        results[0].CorrectionApplied.Should().BeTrue();
        Math.Abs(results[0].CorrectionAngleAzimuth).Should().BeInRange(0.1, 2.0);
        Math.Abs(results[0].CorrectionAngleElevation).Should().BeInRange(0.1, 2.0);

        _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
            It.IsAny<IReadOnlyList<Channel>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        foreach (var channel in channels)
        {
            channel.CalibrationCoeffPhase.Should().NotBe(0);
            channel.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ApplyBeamCorrection_BelowThreshold_NoCorrection()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0.1,
            TiltAngleY = 0.1,
            TiltAngleZ = 0.05,
            StrainValue = 0.0001,
            WindSpeed = 5,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].ExceedsThreshold.Should().BeFalse();
        results[0].CorrectionApplied.Should().BeFalse();
        results[0].CorrectionAngleAzimuth.Should().Be(0);
        results[0].CorrectionAngleElevation.Should().Be(0);
    }

    [Fact]
    public async Task ApplyBeamCorrection_AutoCorrectionDisabled_NoCorrectionApplied()
    {
        var options = CreateOptions(new DeformationOptions
        {
            ThresholdMm = 0.5,
            YoungModulusGpa = 70.0,
            PoissonRatio = 0.33,
            PlateThicknessMm = 15.0,
            AutoBeamCorrection = false
        });

        var monitor = new DeformationMonitor(
            _mockLogger.Object,
            _mockDeformationRepo.Object,
            _mockChannelRepo.Object,
            _mockMediator.Object,
            options);

        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 2.0,
            TiltAngleY = 1.5,
            TiltAngleZ = 0.8,
            StrainValue = 0.005,
            WindSpeed = 25,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].ExceedsThreshold.Should().BeTrue();
        results[0].CorrectionApplied.Should().BeFalse();

        _mockChannelRepo.Verify(r => r.BulkUpdateAsync(
            It.IsAny<IReadOnlyList<Channel>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyBeamCorrection_MultipleChannels_ConsistentPhaseGradient()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 1.0,
            TiltAngleY = 0,
            TiltAngleZ = 0,
            StrainValue = 0.002,
            WindSpeed = 15,
            Temperature = 25
        };

        var channels = Enumerable.Range(0, 16).Select(i => new Channel
        {
            Id = Guid.NewGuid(),
            ChannelIndex = i,
            RowIndex = i / 4,
            ColumnIndex = i % 4,
            CalibrationCoeffPhase = 0
        }).ToList();

        _mockChannelRepo
            .Setup(r => r.GetByStationIdAsync(_testStationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels.AsReadOnly());

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = channels
        };

        await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        var phasesByColumn = channels
            .GroupBy(c => c.ColumnIndex)
            .Select(g => g.Average(c => c.CalibrationCoeffPhase))
            .ToList();

        for (int i = 0; i < phasesByColumn.Count - 1; i++)
        {
            var phaseDiff = phasesByColumn[i + 1] - phasesByColumn[i];
            phaseDiff.Should().BeLessThan(0);
        }
    }

    #endregion

    #region 告警触发逻辑测试

    [Fact]
    public async Task RunDeformationAnalysis_ThresholdExceeded_PublishesEvent()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 2.0,
            TiltAngleY = 1.5,
            TiltAngleZ = 0.8,
            StrainValue = 0.004,
            WindSpeed = 25,
            Temperature = 25
        };

        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 }
        };

        _mockChannelRepo
            .Setup(r => r.GetByStationIdAsync(_testStationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels.ToList().AsReadOnly());

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = channels
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].ExceedsThreshold.Should().BeTrue();

        _mockMediator.Verify(m => m.Publish(
            It.Is<DeformationThresholdExceededEvent>(e =>
                e.StationId == _testStationId &&
                e.SensorIndex == 4 &&
                e.DisplacementMm > 0.5),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<DeformationCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDeformationAnalysis_NoThresholdExceeded_NoThresholdEvent()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0.1,
            TiltAngleY = 0.1,
            TiltAngleZ = 0.05,
            StrainValue = 0.0001,
            WindSpeed = 5,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<DeformationThresholdExceededEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<DeformationCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDeformationAnalysis_MultipleSensorsSomeExceeded_PublishesMultipleEvents()
    {
        var sensorDatas = new[]
        {
            new SensorData
            {
                StationId = _testStationId,
                SensorIndex = 0,
                TiltAngleX = 0.1, TiltAngleY = 0.1, TiltAngleZ = 0.05,
                StrainValue = 0.0001, WindSpeed = 5, Temperature = 25
            },
            new SensorData
            {
                StationId = _testStationId,
                SensorIndex = 4,
                TiltAngleX = 2.0, TiltAngleY = 1.5, TiltAngleZ = 0.8,
                StrainValue = 0.004, WindSpeed = 25, Temperature = 25
            },
            new SensorData
            {
                StationId = _testStationId,
                SensorIndex = 8,
                TiltAngleX = 1.8, TiltAngleY = 1.2, TiltAngleZ = 0.6,
                StrainValue = 0.0035, WindSpeed = 22, Temperature = 25
            }
        };

        var channels = new[]
        {
            new Channel { Id = Guid.NewGuid(), ChannelIndex = 0, RowIndex = 0, ColumnIndex = 0, CalibrationCoeffPhase = 0 }
        };

        _mockChannelRepo
            .Setup(r => r.GetByStationIdAsync(_testStationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels.ToList().AsReadOnly());

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = channels
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Count(r => r.ExceedsThreshold).Should().Be(2);

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<DeformationThresholdExceededEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunDeformationAnalysis_SensorException_ContinuesProcessing()
    {
        var sensorDatas = new[]
        {
            new SensorData
            {
                StationId = _testStationId,
                SensorIndex = 0,
                TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1,
                StrainValue = 0.0005, WindSpeed = 10, Temperature = 25
            },
            new SensorData
            {
                StationId = _testStationId,
                SensorIndex = 4,
                TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1,
                StrainValue = 0.0005, WindSpeed = 10, Temperature = 25
            }
        };

        _mockDeformationRepo
            .SetupSequence(r => r.AddAsync(It.IsAny<DeformationRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"))
            .ReturnsAsync(new DeformationRecord { Id = Guid.NewGuid() });

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Count.Should().BeGreaterOrEqualTo(1);

        VerifyLog(_mockLogger, LogLevel.Error, "Error analyzing sensor", Times.Once);
    }

    #endregion

    #region 形变区域测试

    [Theory]
    [InlineData(0, "top-left", -0.4, 0.4)]
    [InlineData(1, "top-center", 0, 0.4)]
    [InlineData(2, "top-right", 0.4, 0.4)]
    [InlineData(3, "left", -0.4, 0)]
    [InlineData(4, "center", 0, 0)]
    [InlineData(5, "right", 0.4, 0)]
    [InlineData(6, "bottom-left", -0.4, -0.4)]
    [InlineData(7, "bottom-center", 0, -0.4)]
    [InlineData(8, "bottom-right", 0.4, -0.4)]
    public async Task GetDeformationZone_VariousPositions_CorrectZone(
        int sensorIndex, string expectedZonePrefix, double x, double y)
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = sensorIndex,
            TiltAngleX = 1.0,
            TiltAngleY = 1.0,
            TiltAngleZ = 0.5,
            StrainValue = 0.001,
            WindSpeed = 15,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);
        var zone = results[0].DeformationZone;

        zone.Should().StartWith(expectedZonePrefix);
    }

    [Theory]
    [InlineData(0.05, "none")]
    [InlineData(0.3, "-minor")]
    [InlineData(1.5, "-major")]
    [InlineData(2.5, "-critical")]
    public async Task GetDeformationZone_VariousDisplacements_CorrectSeverity(
        double displacementScale, string expectedSuffix)
    {
        var baseStrain = 0.0001;
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0.5 * displacementScale,
            TiltAngleY = 0.3 * displacementScale,
            TiltAngleZ = 0.1 * displacementScale,
            StrainValue = baseStrain * displacementScale * 10,
            WindSpeed = 10 * displacementScale,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);
        var zone = results[0].DeformationZone;

        if (expectedSuffix == "none")
        {
            zone.Should().Be("none");
        }
        else
        {
            zone.Should().EndWith(expectedSuffix);
        }
    }

    #endregion

    #region 异常输入处理测试

    [Fact]
    public async Task RunDeformationAnalysis_EmptySensorData_ReturnsEmpty()
    {
        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = Array.Empty<SensorData>(),
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().BeEmpty();

        _mockMediator.Verify(m => m.Publish(
            It.IsAny<DeformationCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDeformationAnalysis_NegativeWindSpeed_HandlesGracefully()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.0005,
            WindSpeed = -10,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].CalculatedDisplacementMm.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunDeformationAnalysis_ExtremeStrain_CappedStress()
    {
        var sensorData = new SensorData
        {
            StationId = _testStationId,
            SensorIndex = 4,
            TiltAngleX = 0.5,
            TiltAngleY = 0.3,
            TiltAngleZ = 0.1,
            StrainValue = 0.1,
            WindSpeed = 10,
            Temperature = 25
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = new[] { sensorData },
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results[0].StressMpa.Should().BeLessOrEqualTo(180);
    }

    #endregion

    #region 性能测试

    [Fact]
    public async Task RunDeformationAnalysis_Performance_ProcessesQuickly()
    {
        var sensorDatas = Enumerable.Range(0, 100).Select(i => new SensorData
        {
            StationId = _testStationId,
            SensorIndex = i % 9,
            TiltAngleX = 0.5 + i * 0.01,
            TiltAngleY = 0.3 + i * 0.005,
            TiltAngleZ = 0.1 + i * 0.002,
            StrainValue = 0.0005 + i * 0.00001,
            WindSpeed = 10 + i * 0.1,
            Temperature = 25
        }).ToList();

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = Array.Empty<Channel>()
        };

        var startTime = DateTime.UtcNow;
        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        results.Should().HaveCount(100);
    }

    #endregion

    #region 根因验证测试 - 传感器数据缺失时计算发散修复

    [Fact]
    public async Task RunDeformationAnalysis_WithNaNData_ShouldDetectAnomaly()
    {
        var sensorDatas = new List<SensorData>
        {
            new() { StationId = _testStationId, SensorIndex = 0, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 1, TiltAngleX = double.NaN, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 2, TiltAngleX = 0.52, TiltAngleY = 0.31, TiltAngleZ = 0.12, StrainValue = 0.00052, WindSpeed = 11, Temperature = 26 },
            new() { StationId = _testStationId, SensorIndex = 3, TiltAngleX = 0.48, TiltAngleY = 0.29, TiltAngleZ = 0.09, StrainValue = 0.00048, WindSpeed = 9, Temperature = 24 },
            new() { StationId = _testStationId, SensorIndex = 4, TiltAngleX = 0.51, TiltAngleY = 0.32, TiltAngleZ = 0.11, StrainValue = 0.00051, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 5, TiltAngleX = 0.49, TiltAngleY = 0.30, TiltAngleZ = 0.10, StrainValue = 0.00049, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 6, TiltAngleX = 0.50, TiltAngleY = 0.31, TiltAngleZ = 0.10, StrainValue = 0.00050, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 7, TiltAngleX = 0.51, TiltAngleY = 0.30, TiltAngleZ = 0.11, StrainValue = 0.00051, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 8, TiltAngleX = 0.49, TiltAngleY = 0.29, TiltAngleZ = 0.09, StrainValue = 0.00049, WindSpeed = 10, Temperature = 25 }
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(9);

        var resultWithNaN = results.First(r => r.SensorIndex == 1);
        resultWithNaN.IsInterpolated.Should().BeTrue();

        foreach (var result in results)
        {
            double.IsNaN(result.CalculatedDisplacementMm).Should().BeFalse();
            double.IsInfinity(result.CalculatedDisplacementMm).Should().BeFalse();
            result.CalculatedDisplacementMm.Should().BeGreaterThan(0);
            result.CalculatedDisplacementMm.Should().BeLessThan(100);
        }

        VerifyLog(_mockLogger, LogLevel.Warning, "anomalous", Times.AtLeastOnce());
        VerifyLog(_mockLogger, LogLevel.Information, "Interpolated", Times.AtLeastOnce());
    }

    [Fact]
    public async Task RunDeformationAnalysis_WithOutOfRangeValues_ShouldDetectPhysicalAnomaly()
    {
        var sensorDatas = new List<SensorData>
        {
            new() { StationId = _testStationId, SensorIndex = 0, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 1, TiltAngleX = 20.0, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 2, TiltAngleX = 0.52, TiltAngleY = 0.31, TiltAngleZ = 0.12, StrainValue = 0.00052, WindSpeed = 11, Temperature = 26 },
            new() { StationId = _testStationId, SensorIndex = 3, TiltAngleX = 0.48, TiltAngleY = 0.29, TiltAngleZ = 0.09, StrainValue = 0.00048, WindSpeed = 9, Temperature = 24 },
            new() { StationId = _testStationId, SensorIndex = 4, TiltAngleX = 0.51, TiltAngleY = 0.32, TiltAngleZ = 0.11, StrainValue = 0.00051, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 5, TiltAngleX = 0.49, TiltAngleY = 0.30, TiltAngleZ = 0.10, StrainValue = 0.00049, WindSpeed = -5, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 6, TiltAngleX = 0.50, TiltAngleY = 0.31, TiltAngleZ = 0.10, StrainValue = 0.00050, WindSpeed = 10, Temperature = 150 },
            new() { StationId = _testStationId, SensorIndex = 7, TiltAngleX = 0.51, TiltAngleY = 0.30, TiltAngleZ = 0.11, StrainValue = 0.01, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 8, TiltAngleX = 0.49, TiltAngleY = 0.29, TiltAngleZ = 0.09, StrainValue = 0.00049, WindSpeed = 10, Temperature = 25 }
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(9);

        var anomalies = results.Where(r => r.IsInterpolated).ToList();
        anomalies.Count.Should().BeGreaterOrEqualTo(4);

        foreach (var result in results)
        {
            double.IsNaN(result.CalculatedDisplacementMm).Should().BeFalse();
            double.IsInfinity(result.CalculatedDisplacementMm).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RunDeformationAnalysis_WithZScoreOutliers_ShouldApplyInterpolation()
    {
        var baseSensorData = Enumerable.Range(0, 9).Select(i => new SensorData
        {
            StationId = _testStationId,
            SensorIndex = i,
            TiltAngleX = 0.5 + i * 0.001,
            TiltAngleY = 0.3 + i * 0.001,
            TiltAngleZ = 0.1 + i * 0.001,
            StrainValue = 0.0005 + i * 0.00001,
            WindSpeed = 10,
            Temperature = 25
        }).ToList();

        baseSensorData[4] = baseSensorData[4] with
        {
            TiltAngleX = 0.5 + 5.0,
            StrainValue = 0.0005 + 0.01
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = baseSensorData,
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(9);

        var centerResult = results.First(r => r.SensorIndex == 4);
        centerResult.IsInterpolated.Should().BeTrue();

        var neighbors = results.Where(r => r.SensorIndex != 4 && !r.IsInterpolated).ToList();
        var avgTiltX = neighbors.Average(r => r.TiltAngleX);

        centerResult.TiltAngleX.Should().BeApproximately(avgTiltX, 0.5);
    }

    [Fact]
    public async Task RunDeformationAnalysis_MultipleMissingSensors_ShouldInterpolateCorrectly()
    {
        var sensorDatas = new List<SensorData>
        {
            new() { StationId = _testStationId, SensorIndex = 0, TiltAngleX = double.NaN, TiltAngleY = double.NaN, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 1, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 2, TiltAngleX = double.NaN, TiltAngleY = double.NaN, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 3, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 4, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 5, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 6, TiltAngleX = double.NaN, TiltAngleY = double.NaN, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 7, TiltAngleX = 0.5, TiltAngleY = 0.3, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 },
            new() { StationId = _testStationId, SensorIndex = 8, TiltAngleX = double.NaN, TiltAngleY = double.NaN, TiltAngleZ = 0.1, StrainValue = 0.0005, WindSpeed = 10, Temperature = 25 }
        };

        var request = new DeformationRequest
        {
            StationId = _testStationId,
            SensorData = sensorDatas,
            Channels = Array.Empty<Channel>()
        };

        var results = await _monitor.RunDeformationAnalysisAsync(request, CancellationToken.None);

        results.Should().NotBeNull();
        results.Should().HaveCount(9);

        var interpolatedResults = results.Where(r => r.IsInterpolated).ToList();
        interpolatedResults.Should().HaveCount(4);

        foreach (var result in results)
        {
            double.IsNaN(result.CalculatedDisplacementMm).Should().BeFalse();
            double.IsInfinity(result.CalculatedDisplacementMm).Should().BeFalse();
            result.CalculatedDisplacementMm.Should().BeGreaterThan(0);
        }
    }

    #endregion
}
