using MediatR;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Modules.AlarmForwarder;
using AntennaMonitoring.Modules.DeformationMonitor;
using AntennaMonitoring.Modules.CoSiteInterferenceAnalyzer;
using AntennaMonitoring.Modules.PaEfficiencyEvaluator;
using AntennaMonitoring.Modules.SpectrumScanner;
using AntennaMonitoring.Repositories;
using AntennaMonitoring.Models;
using Microsoft.Extensions.Logging;

namespace AntennaMonitoring.Modules;

public class EcpriDataReceivedHandler : INotificationHandler<EcpriDataReceivedEvent>
{
    private readonly ILogger<EcpriDataReceivedHandler> _logger;
    private readonly IChannelRepository _channelRepo;
    private readonly IAlarmForwarder _alarmForwarder;
    private readonly DiagnosisOptions _diagnosisOptions;

    public EcpriDataReceivedHandler(
        ILogger<EcpriDataReceivedHandler> logger,
        IChannelRepository channelRepo,
        IAlarmForwarder alarmForwarder,
        Microsoft.Extensions.Options.IOptions<DiagnosisOptions> diagnosisOptions)
    {
        _logger = logger;
        _channelRepo = channelRepo;
        _alarmForwarder = alarmForwarder;
        _diagnosisOptions = diagnosisOptions.Value;
    }

    public async Task Handle(EcpriDataReceivedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var channels = (await _channelRepo.GetByStationIdAsync(
                notification.StationId, cancellationToken)).ToList();

            if (!channels.Any()) return;

            foreach (var channelData in notification.ChannelData)
            {
                var channel = channels.FirstOrDefault(c => c.ChannelIndex == channelData.ChannelIndex);
                if (channel == null) continue;

                channel.AmplitudeDeviation = (decimal)((channelData.Amplitude - 1.0) * 10);
                channel.PhaseDeviation = (decimal)(channelData.Phase * 180 / Math.PI);
                channel.LastSwr = (decimal)channelData.Swr;
                channel.LastTemperature = (decimal)channelData.Temperature;
                channel.LastUpdatedAt = notification.Timestamp;

                if (channelData.Swr >= 1.5 || channelData.Temperature > 80.0)
                {
                    await _alarmForwarder.CheckChannelAlarmsAsync(
                        notification.StationId, channel, channelData.Swr, channelData.Temperature, cancellationToken);
                }
            }

            await _channelRepo.BulkUpdateAsync(channels, cancellationToken);

            if (channels.Count > 0)
            {
                await _alarmForwarder.CheckSectorFailureAsync(
                    notification.StationId, channels.AsReadOnly(), cancellationToken);
            }

            _logger.LogDebug(
                "Processed eCPRI data event for station {StationId}: {Count} channels updated",
                notification.StationId, channels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling eCPRI data received event for station {StationId}", notification.StationId);
        }
    }
}

public class CalibrationCompletedHandler : INotificationHandler<CalibrationCompletedEvent>
{
    private readonly ILogger<CalibrationCompletedHandler> _logger;
    private readonly IInfluxDBRepository _influxRepo;

    public CalibrationCompletedHandler(
        ILogger<CalibrationCompletedHandler> logger,
        IInfluxDBRepository influxRepo)
    {
        _logger = logger;
        _influxRepo = influxRepo;
    }

    public async Task Handle(CalibrationCompletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _influxRepo.WriteBeamformingMetricsAsync(
                notification.StationId,
                notification.Algorithm,
                (notification.SllBefore + notification.SllAfter) / 2,
                notification.SllBefore,
                notification.SllAfter,
                10.0,
                8.0,
                notification.Converged,
                cancellationToken);

            _logger.LogInformation(
                "Calibration completed event processed: Station={StationId}, SLL {Before:0.00}dB → {After:0.00}dB",
                notification.StationId, notification.SllBefore, notification.SllAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling calibration completed event for station {StationId}", notification.StationId);
        }
    }
}

public class DiagnosisCompletedHandler : INotificationHandler<DiagnosisCompletedEvent>
{
    private readonly ILogger<DiagnosisCompletedHandler> _logger;
    private readonly IAlarmForwarder _alarmForwarder;
    private readonly IChannelRepository _channelRepo;

    public DiagnosisCompletedHandler(
        ILogger<DiagnosisCompletedHandler> logger,
        IAlarmForwarder alarmForwarder,
        IChannelRepository channelRepo)
    {
        _logger = logger;
        _alarmForwarder = alarmForwarder;
        _channelRepo = channelRepo;
    }

    public async Task Handle(DiagnosisCompletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Diagnosis completed event processed: Station={StationId}, HighRisk={HighRisk}/{Total}, AvgProb={AvgProb:P1}",
                notification.StationId, notification.HighRiskChannelCount,
                notification.ChannelCount, notification.AverageFailureProbability);

            if (notification.HighRiskChannelCount > 0)
            {
                var channels = (await _channelRepo.GetByStationIdAsync(
                    notification.StationId, cancellationToken)).ToList();

                var highRiskChannels = channels
                    .Where(c => notification.HighRiskChannelIds.Contains(c.Id))
                    .ToList();

                foreach (var channel in highRiskChannels)
                {
                    channel.Status = "fault";
                }

                await _channelRepo.BulkUpdateAsync(highRiskChannels, cancellationToken);

                await _alarmForwarder.CheckSectorFailureAsync(
                    notification.StationId, channels.AsReadOnly(), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling diagnosis completed event for station {StationId}", notification.StationId);
        }
    }
}

public class AlarmTriggeredHandler : INotificationHandler<AlarmTriggeredEvent>
{
    private readonly ILogger<AlarmTriggeredHandler> _logger;

    public AlarmTriggeredHandler(ILogger<AlarmTriggeredHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(AlarmTriggeredEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Alarm triggered event: {AlarmCode} for station {StationId}, Level={Level}, Severity={Severity}",
            notification.AlarmCode, notification.StationId, notification.AlarmLevel, notification.Severity);

        return Task.CompletedTask;
    }
}

public class SensorDataReceivedHandler : INotificationHandler<SensorDataReceivedEvent>
{
    private readonly ILogger<SensorDataReceivedHandler> _logger;
    private readonly IDataChannels _channels;
    private readonly IInfluxDBRepository _influxRepo;

    public SensorDataReceivedHandler(
        ILogger<SensorDataReceivedHandler> logger,
        IDataChannels channels,
        IInfluxDBRepository influxRepo)
    {
        _logger = logger;
        _channels = channels;
        _influxRepo = influxRepo;
    }

    public async Task Handle(SensorDataReceivedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _influxRepo.WriteSensorMetricAsync(
                notification.StationId.ToString(), notification.SensorData, cancellationToken);

            await _channels.SensorDataWriter.WriteAsync(notification.SensorData, cancellationToken);

            _logger.LogDebug(
                "Sensor data event processed: Station={StationId}, Sensor={SensorType}, Tilt={TiltMag:F2}°, Strain={Strain:F4}",
                notification.StationId, notification.SensorData.SensorType,
                notification.SensorData.TiltMagnitude, notification.SensorData.StrainValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling sensor data received event for station {StationId}", notification.StationId);
        }
    }
}

public class DeformationAnalyzedHandler : INotificationHandler<DeformationAnalyzedEvent>
{
    private readonly ILogger<DeformationAnalyzedHandler> _logger;
    private readonly IDeformationRecordRepository _deformationRepo;
    private readonly IAlarmForwarder _alarmForwarder;

    public DeformationAnalyzedHandler(
        ILogger<DeformationAnalyzedHandler> logger,
        IDeformationRecordRepository deformationRepo,
        IAlarmForwarder alarmForwarder)
    {
        _logger = logger;
        _deformationRepo = deformationRepo;
        _alarmForwarder = alarmForwarder;
    }

    public async Task Handle(DeformationAnalyzedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var record = new DeformationRecord
            {
                Id = Guid.NewGuid(),
                StationId = notification.StationId,
                TiltAngleX = notification.Result.TiltAngleX,
                TiltAngleY = notification.Result.TiltAngleY,
                TiltMagnitude = notification.Result.TiltMagnitude,
                StrainValue = notification.Result.StrainValue,
                CalculatedDisplacementMm = notification.Result.DisplacementMm,
                StressMpa = notification.Result.StressMpa,
                DeformationZone = notification.Result.DeformationZone,
                IsExceedingThreshold = notification.Result.IsExceedingThreshold,
                BeamCorrectionApplied = notification.Result.BeamCorrectionApplied,
                CorrectionAngleAzimuth = notification.Result.CorrectionAngleAzimuth,
                CorrectionAngleElevation = notification.Result.CorrectionAngleElevation,
                MeasurementTime = notification.Timestamp,
                CreatedAt = DateTime.UtcNow
            };

            await _deformationRepo.AddAsync(record, cancellationToken);

            if (notification.Result.IsExceedingThreshold)
            {
                await _alarmForwarder.GenerateAlarmAsync(
                    notification.StationId,
                    "DEFORMATION_EXCEEDED",
                    "deformation",
                    "high",
                    0.5,
                    notification.Result.DisplacementMm,
                    $"形变超过阈值: {notification.Result.DisplacementMm:F3}mm, 区域: {notification.Result.DeformationZone}",
                    cancellationToken);
            }

            _logger.LogInformation(
                "Deformation analysis processed: Station={StationId}, Displacement={Disp:F3}mm, Exceeded={Exceeded}, Corrected={Corrected}",
                notification.StationId, notification.Result.DisplacementMm,
                notification.Result.IsExceedingThreshold, notification.Result.BeamCorrectionApplied);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling deformation analyzed event for station {StationId}", notification.StationId);
        }
    }
}

public class InterferenceAnalyzedHandler : INotificationHandler<InterferenceAnalyzedEvent>
{
    private readonly ILogger<InterferenceAnalyzedHandler> _logger;
    private readonly ICoSiteInterferenceRecordRepository _interferenceRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly IAlarmForwarder _alarmForwarder;

    public InterferenceAnalyzedHandler(
        ILogger<InterferenceAnalyzedHandler> logger,
        ICoSiteInterferenceRecordRepository interferenceRepo,
        IInfluxDBRepository influxRepo,
        IAlarmForwarder alarmForwarder)
    {
        _logger = logger;
        _interferenceRepo = interferenceRepo;
        _influxRepo = influxRepo;
        _alarmForwarder = alarmForwarder;
    }

    public async Task Handle(InterferenceAnalyzedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _influxRepo.WriteInterferenceMetricAsync(
                notification.StationId.ToString(), notification.Result, cancellationToken);

            var record = new CoSiteInterferenceRecord
            {
                Id = Guid.NewGuid(),
                StationId = notification.StationId,
                InterferingAntennaId = notification.Result.InterferingAntennaId,
                InterferingOperator = notification.Result.InterferingOperator,
                InterferingAntennaType = notification.Result.InterferingAntennaType,
                DistanceMeters = notification.Result.DistanceMeters,
                IsolationDb = notification.Result.IsolationDb,
                CouplingCoefficient = notification.Result.CouplingCoefficient,
                IsIsolationSufficient = notification.Result.IsIsolationSufficient,
                InterferenceVectorX = notification.Result.InterferenceVectorX,
                InterferenceVectorY = notification.Result.InterferenceVectorY,
                InterferenceVectorZ = notification.Result.InterferenceVectorZ,
                Recommendation = notification.Result.Recommendation,
                MeasurementTime = notification.Timestamp,
                CreatedAt = DateTime.UtcNow
            };

            await _interferenceRepo.AddAsync(record, cancellationToken);

            if (!notification.Result.IsIsolationSufficient)
            {
                await _alarmForwarder.GenerateAlarmAsync(
                    notification.StationId,
                    "INTERFERENCE_ISOLATION_LOW",
                    "interference",
                    "medium",
                    30.0,
                    notification.Result.IsolationDb,
                    $"共址隔离度不足: {notification.Result.IsolationDb:F1}dB, 建议: {notification.Result.Recommendation}",
                    cancellationToken);
            }

            _logger.LogInformation(
                "Interference analysis processed: Station={StationId}, Isolation={Iso:F1}dB, Sufficient={Sufficient}",
                notification.StationId, notification.Result.IsolationDb, notification.Result.IsIsolationSufficient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling interference analyzed event for station {StationId}", notification.StationId);
        }
    }
}

public class PaEfficiencyEvaluatedHandler : INotificationHandler<PaEfficiencyEvaluatedEvent>
{
    private readonly ILogger<PaEfficiencyEvaluatedHandler> _logger;
    private readonly IPaEfficiencyRecordRepository _efficiencyRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly IAlarmForwarder _alarmForwarder;

    public PaEfficiencyEvaluatedHandler(
        ILogger<PaEfficiencyEvaluatedHandler> logger,
        IPaEfficiencyRecordRepository efficiencyRepo,
        IInfluxDBRepository influxRepo,
        IAlarmForwarder alarmForwarder)
    {
        _logger = logger;
        _efficiencyRepo = efficiencyRepo;
        _influxRepo = influxRepo;
        _alarmForwarder = alarmForwarder;
    }

    public async Task Handle(PaEfficiencyEvaluatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _influxRepo.WriteEfficiencyMetricAsync(
                notification.StationId.ToString(), notification.Result, cancellationToken);

            var record = new PaEfficiencyRecord
            {
                Id = Guid.NewGuid(),
                StationId = notification.StationId,
                ChannelId = notification.Result.ChannelId,
                PaTemperature = notification.Result.PaTemperature,
                OutputPowerDbm = notification.Result.OutputPowerDbm,
                InputPowerDbm = notification.Result.InputPowerDbm,
                EfficiencyPercent = notification.Result.EfficiencyPercent,
                EfficiencyDecayRate = notification.Result.EfficiencyDecayRate,
                PredictedRemainingHours = notification.Result.PredictedRemainingHours,
                NeedsReplacement = notification.Result.NeedsReplacement,
                ReplacementReason = notification.Result.ReplacementReason,
                EfficiencyHistory = notification.Result.EfficiencyHistory,
                MeasurementTime = notification.Timestamp,
                CreatedAt = DateTime.UtcNow
            };

            await _efficiencyRepo.AddAsync(record, cancellationToken);

            if (notification.Result.NeedsReplacement)
            {
                await _alarmForwarder.GenerateAlarmAsync(
                    notification.StationId,
                    "PA_EFFICIENCY_LOW",
                    "pa_efficiency",
                    "medium",
                    40.0,
                    notification.Result.EfficiencyPercent,
                    notification.Result.ReplacementReason ?? "功放效率低于阈值",
                    cancellationToken);
            }

            _logger.LogInformation(
                "PA efficiency evaluated: Station={StationId}, Channel={ChannelId}, Efficiency={Eff:F1}%, NeedsReplace={Replace}",
                notification.StationId, notification.Result.ChannelId,
                notification.Result.EfficiencyPercent, notification.Result.NeedsReplacement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PA efficiency evaluated event for station {StationId}", notification.StationId);
        }
    }
}

public class SpectrumScannedHandler : INotificationHandler<SpectrumScannedEvent>
{
    private readonly ILogger<SpectrumScannedHandler> _logger;
    private readonly ISpectrumScanRecordRepository _spectrumRepo;
    private readonly IInfluxDBRepository _influxRepo;
    private readonly IAlarmForwarder _alarmForwarder;

    public SpectrumScannedHandler(
        ILogger<SpectrumScannedHandler> logger,
        ISpectrumScanRecordRepository spectrumRepo,
        IInfluxDBRepository influxRepo,
        IAlarmForwarder alarmForwarder)
    {
        _logger = logger;
        _spectrumRepo = spectrumRepo;
        _influxRepo = influxRepo;
        _alarmForwarder = alarmForwarder;
    }

    public async Task Handle(SpectrumScannedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _influxRepo.WriteSpectrumMetricAsync(
                notification.StationId.ToString(), notification.Result, cancellationToken);

            var interferenceCount = notification.Result.InterferenceFrequenciesMhz?.Length ?? 0;
            var record = new SpectrumScanRecord
            {
                Id = Guid.NewGuid(),
                StationId = notification.StationId,
                FrequencyPointsMhz = notification.Result.FrequencyPointsMhz,
                PowerLevelsDbm = notification.Result.PowerLevelsDbm,
                InterferenceCount = interferenceCount,
                InterferenceFrequenciesMhz = notification.Result.InterferenceFrequenciesMhz,
                InterferenceDirectionsDeg = notification.Result.InterferenceDirectionsDeg,
                NullAnglesDeg = notification.Result.NullAnglesDeg,
                NullDepthsDb = notification.Result.NullDepthsDb,
                AutoNullSteeringApplied = notification.Result.AutoNullSteeringApplied,
                InterferenceDetails = interferenceCount > 0
                    ? $"检测到{interferenceCount}个干扰信号, 已自动调整{notification.Result.NullAnglesDeg?.Length ?? 0}个零陷方向"
                    : null,
                ScanTime = notification.Timestamp,
                CreatedAt = DateTime.UtcNow
            };

            await _spectrumRepo.AddAsync(record, cancellationToken);

            if (interferenceCount > 0)
            {
                await _alarmForwarder.GenerateAlarmAsync(
                    notification.StationId,
                    "SPECTRUM_INTERFERENCE_DETECTED",
                    "spectrum",
                    "low",
                    0,
                    interferenceCount,
                    $"检测到{interferenceCount}个外部干扰信号",
                    cancellationToken);
            }

            _logger.LogInformation(
                "Spectrum scan processed: Station={StationId}, InterferenceCount={Count}, NullSteering={Nulling}",
                notification.StationId, interferenceCount, notification.Result.AutoNullSteeringApplied);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling spectrum scanned event for station {StationId}", notification.StationId);
        }
    }
}
