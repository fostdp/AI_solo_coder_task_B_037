using MediatR;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Modules.AlarmForwarder;
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
