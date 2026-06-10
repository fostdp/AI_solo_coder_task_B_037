using System.Text.Json;
using MediatR;
using MQTTnet;
using MQTTnet.Client;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Modules.AlarmForwarder;

public class AlarmForwarder : IAlarmForwarder
{
    private readonly ILogger<AlarmForwarder> _logger;
    private readonly IMediator _mediator;
    private readonly IDataChannels _dataChannels;
    private readonly IAlarmRepository _alarmRepo;
    private readonly IChannelRepository _channelRepo;
    private readonly IManagedMqttClient _mqttClient;
    private readonly MQTTOptions _mqttOptions;
    private readonly DiagnosisOptions _diagnosisOptions;
    private readonly JsonSerializerOptions _jsonOptions;

    public AlarmForwarder(
        ILogger<AlarmForwarder> logger,
        IMediator mediator,
        IDataChannels dataChannels,
        IAlarmRepository alarmRepo,
        IChannelRepository channelRepo,
        IManagedMqttClient mqttClient,
        IOptions<MQTTOptions> mqttOptions,
        IOptions<DiagnosisOptions> diagnosisOptions)
    {
        _logger = logger;
        _mediator = mediator;
        _dataChannels = dataChannels;
        _alarmRepo = alarmRepo;
        _channelRepo = channelRepo;
        _mqttClient = mqttClient;
        _mqttOptions = mqttOptions.Value;
        _diagnosisOptions = diagnosisOptions.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<Alarm?> CheckChannelAlarmsAsync(
        Guid stationId,
        Channel channel,
        double swr,
        double temperature,
        CancellationToken cancellationToken)
    {
        Alarm? alarm = null;
        var alarmCode = string.Empty;
        var severity = string.Empty;
        var title = string.Empty;
        var description = string.Empty;
        var threshold = 0.0;
        var actualValue = 0.0;
        var alarmLevel = string.Empty;

        if (swr >= _diagnosisOptions.SWRAlarmThreshold)
        {
            alarmCode = "SWR_HIGH";
            severity = "critical";
            title = "通道驻波比过高";
            description = $"通道 {channel.ChannelIndex} 驻波比 {swr:0.00} 超过阈值 {_diagnosisOptions.SWRAlarmThreshold}";
            threshold = _diagnosisOptions.SWRAlarmThreshold;
            actualValue = swr;
            alarmLevel = "level1";
        }
        else if (swr >= 1.5)
        {
            alarmCode = "SWR_WARNING";
            severity = "warning";
            title = "通道驻波比预警";
            description = $"通道 {channel.ChannelIndex} 驻波比 {swr:0.00} 接近阈值 1.5";
            threshold = 1.5;
            actualValue = swr;
            alarmLevel = "level1";
        }
        else if (temperature > 80.0)
        {
            alarmCode = "PA_TEMP_HIGH";
            severity = "critical";
            title = "功放温度过高";
            description = $"通道 {channel.ChannelIndex} 功放温度 {temperature:0.0}°C 超过阈值 80°C";
            threshold = 80.0;
            actualValue = temperature;
            alarmLevel = "level1";
        }

        if (!string.IsNullOrEmpty(alarmCode))
        {
            alarm = await CreateOrUpdateAlarmAsync(
                stationId,
                channel.Id,
                channel.ChannelIndex,
                alarmCode,
                alarmLevel,
                severity,
                title,
                description,
                actualValue,
                threshold,
                false,
                1,
                cancellationToken);
        }

        return alarm;
    }

    public async Task<Alarm?> CheckSectorFailureAsync(
        Guid stationId,
        IReadOnlyList<Channel> channels,
        CancellationToken cancellationToken)
    {
        int faultCount = channels.Count(c => c.Status == "fault");
        double faultRatio = (double)faultCount / channels.Count;

        if (faultRatio >= _diagnosisOptions.SectorFailureChannelRatio)
        {
            return await CreateOrUpdateAlarmAsync(
                stationId,
                null,
                null,
                "SECTOR_FAILURE",
                "level2",
                "critical",
                "扇区失效告警",
                $"基站 {stationId} 有 {faultCount}/{channels.Count} 个通道故障，占比 {faultRatio:P1}",
                faultRatio,
                _diagnosisOptions.SectorFailureChannelRatio,
                true,
                faultCount,
                cancellationToken);
        }

        return null;
    }

    public async Task<bool> PublishAlarmAsync(Alarm alarm, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                alarm.Id,
                alarm.StationId,
                alarm.ChannelId,
                alarm.AlarmCode,
                alarm.AlarmLevel,
                alarm.Severity,
                alarm.Title,
                alarm.Description,
                alarm.ActualValue,
                alarm.ThresholdValue,
                alarm.TriggeredAt,
                alarm.IsSectorLevel,
                alarm.AffectedChannelCount,
                alarm.Status
            }, _jsonOptions);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(_mqttOptions.Topics.Alarm)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(alarm.Status == "active")
                .Build();

            await _mqttClient.EnqueueAsync(message);

            alarm.IsPublished = true;
            alarm.PublishedAt = DateTime.UtcNow;
            await _alarmRepo.UpdateAsync(alarm, cancellationToken);

            _logger.LogInformation(
                "Alarm published: {AlarmCode} for station {StationId}, level={Level}, severity={Severity}",
                alarm.AlarmCode, alarm.StationId, alarm.AlarmLevel, alarm.Severity);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish alarm {AlarmId}", alarm.Id);
            return false;
        }
    }

    public async Task ProcessAlarmTriggeredAsync(AlarmTriggeredEvent eventData, CancellationToken cancellationToken)
    {
        try
        {
            var alarm = await _alarmRepo.GetByIdAsync(eventData.AlarmId, cancellationToken);
            if (alarm == null) return;

            await PublishAlarmAsync(alarm, cancellationToken);

            var eventData2 = new AlarmTriggeredEvent
            {
                AlarmId = alarm.Id,
                StationId = alarm.StationId,
                ChannelId = alarm.ChannelId,
                AlarmCode = alarm.AlarmCode,
                AlarmLevel = alarm.AlarmLevel,
                Severity = alarm.Severity,
                Title = alarm.Title,
                Description = alarm.Description,
                ActualValue = (double)(alarm.ActualValue ?? 0),
                ThresholdValue = (double)(alarm.ThresholdValue ?? 0),
                TriggeredAt = alarm.TriggeredAt,
                IsSectorLevel = alarm.IsSectorLevel,
                AffectedChannelCount = alarm.AffectedChannelCount
            };

            await _mediator.Publish(eventData2, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alarm triggered event for alarm {AlarmId}", eventData.AlarmId);
        }
    }

    private async Task<Alarm> CreateOrUpdateAlarmAsync(
        Guid stationId,
        Guid? channelId,
        int? channelIndex,
        string alarmCode,
        string alarmLevel,
        string severity,
        string title,
        string description,
        double actualValue,
        double thresholdValue,
        bool isSectorLevel,
        int affectedChannelCount,
        CancellationToken cancellationToken)
    {
        var existing = (await _alarmRepo.GetByStationIdAsync(stationId, "active", cancellationToken))
            .FirstOrDefault(a => a.AlarmCode == alarmCode &&
                               a.ChannelId == channelId &&
                               a.Status == "active");

        if (existing != null)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ActualValue = (decimal)actualValue;
            existing.ThresholdValue = (decimal)thresholdValue;
            existing.IsPublished = false;
            await _alarmRepo.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var alarm = new Alarm
        {
            Id = Guid.NewGuid(),
            StationId = stationId,
            ChannelId = channelId,
            ChannelIndex = channelIndex,
            AlarmCode = alarmCode,
            AlarmLevel = alarmLevel,
            Severity = severity,
            Title = title,
            Description = description,
            ActualValue = (decimal)actualValue,
            ThresholdValue = (decimal)thresholdValue,
            IsSectorLevel = isSectorLevel,
            AffectedChannelCount = affectedChannelCount,
            Status = "active",
            TriggeredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPublished = false
        };

        await _alarmRepo.CreateAsync(alarm, cancellationToken);

        var eventData = new AlarmTriggeredEvent
        {
            AlarmId = alarm.Id,
            StationId = alarm.StationId,
            ChannelId = alarm.ChannelId,
            AlarmCode = alarm.AlarmCode,
            AlarmLevel = alarm.AlarmLevel,
            Severity = alarm.Severity,
            Title = alarm.Title,
            Description = alarm.Description,
            ActualValue = actualValue,
            ThresholdValue = thresholdValue,
            TriggeredAt = alarm.TriggeredAt,
            IsSectorLevel = alarm.IsSectorLevel,
            AffectedChannelCount = alarm.AffectedChannelCount
        };

        await _dataChannels.AlarmTriggeredWriter.WriteAsync(eventData, cancellationToken);

        return alarm;
    }
}
