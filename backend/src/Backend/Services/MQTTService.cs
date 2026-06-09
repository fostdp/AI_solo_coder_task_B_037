using System.Text;
using System.Text.Json;
using AntennaMonitoring.Models;
using AntennaMonitoring.Repositories;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntennaMonitoring.Services;

public class MQTTService : BackgroundService
{
    private readonly ILogger<MQTTService> _logger;
    private readonly MQTTOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private IManagedMqttClient? _mqttClient;

    public MQTTService(
        ILogger<MQTTService> logger,
        IOptions<MQTTOptions> options,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ConnectAsync(stoppingToken);
            await SubscribeAsync(stoppingToken);

            _ = Task.Run(() => ProcessUnpublishedAlarmsAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT Service error");
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var factory = new MqttFactory();
        _mqttClient = factory.CreateManagedMqttClient();

        var options = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Broker, _options.Port)
                .WithCredentials(_options.UserName, _options.Password)
                .WithClientId(_options.ClientId)
                .WithCleanSession()
                .Build())
            .Build();

        _mqttClient.ConnectedAsync += async (e) =>
        {
            _logger.LogInformation("MQTT Client connected");
            await Task.CompletedTask;
        };

        _mqttClient.DisconnectedAsync += async (e) =>
        {
            _logger.LogWarning($"MQTT Client disconnected: {e.Reason}");
            await Task.CompletedTask;
        };

        _mqttClient.ApplicationMessageReceivedAsync += async (e) =>
        {
            await HandleMessageAsync(e.ApplicationMessage);
        };

        await _mqttClient.StartAsync(options);
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient == null) return;

        var topics = new[]
        {
            _options.Topics.ECPRI
        };

        foreach (var topic in topics)
        {
            await _mqttClient.SubscribeAsync(topic);
            _logger.LogInformation($"Subscribed to MQTT topic: {topic}");
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessage message)
    {
        try
        {
            var topic = message.Topic;
            var payload = Encoding.UTF8.GetString(message.PayloadSegment);

            _logger.LogInformation($"Received MQTT message on {topic}");

            if (topic.StartsWith("5g/antenna/ecpri/"))
            {
                using var scope = _serviceProvider.CreateScope();
                var ecpriService = scope.ServiceProvider.GetRequiredService<ECPRIService>();
                var packet = JsonSerializer.Deserialize<ECPRIDataPacket>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (packet != null)
                {
                    await ecpriService.ProcessHttpPacketAsync(packet, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling MQTT message");
        }
    }

    private async Task ProcessUnpublishedAlarmsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alarmRepo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();

                var unpublished = await alarmRepo.GetUnpublishedAsync(100, cancellationToken);

                foreach (var alarm in unpublished)
                {
                    await PublishAlarmAsync(alarm, cancellationToken);
                    await alarmRepo.MarkAsPublishedAsync(alarm.Id, cancellationToken);
                    _logger.LogInformation($"Published alarm {alarm.Id} to MQTT");
                }

                await Task.Delay(5000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing unpublished alarms");
                await Task.Delay(10000, cancellationToken);
            }
        }
    }

    public async Task PublishAlarmAsync(Alarm alarm, CancellationToken cancellationToken)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected) return;

        var alarmMessage = new
        {
            id = alarm.Id,
            alarmCode = alarm.AlarmCode,
            alarmType = alarm.AlarmType,
            alarmLevel = alarm.AlarmLevel,
            stationId = alarm.StationId,
            stationName = alarm.Station?.StationName,
            stationCode = alarm.Station?.StationCode,
            channelId = alarm.ChannelId,
            channelIndex = alarm.Channel?.ChannelIndex,
            title = alarm.Title,
            description = alarm.Description,
            thresholdValue = alarm.ThresholdValue,
            actualValue = alarm.ActualValue,
            status = alarm.Status,
            createdAt = alarm.CreatedAt
        };

        var payload = JsonSerializer.Serialize(alarmMessage);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(_options.Topics.Alarm)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .Build();

        await _mqttClient.EnqueueAsync(message);
    }

    public async Task PublishCalibrationAsync(Guid stationId, string stationCode,
        double sllBefore, double sllAfter, string algorithm, CancellationToken cancellationToken)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected) return;

        var message = new
        {
            stationId,
            stationCode,
            sllBefore,
            sllAfter,
            algorithm,
            timestamp = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(message);
        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(_options.Topics.Calibration)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _mqttClient.EnqueueAsync(mqttMessage);
        _logger.LogInformation($"Published calibration data for {stationCode}");
    }

    public override void Dispose()
    {
        _mqttClient?.StopAsync().Wait();
        _mqttClient?.Dispose();
        base.Dispose();
    }
}
