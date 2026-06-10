using MediatR;
using AntennaMonitoring.Messages;
using AntennaMonitoring.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Modules.AlarmForwarder;

public class AlarmForwarderHostedService : BackgroundService
{
    private readonly ILogger<AlarmForwarderHostedService> _logger;
    private readonly IAlarmForwarder _forwarder;
    private readonly IDataChannels _dataChannels;
    private readonly IAlarmRepository _alarmRepo;
    private readonly MQTTOptions _mqttOptions;

    public AlarmForwarderHostedService(
        ILogger<AlarmForwarderHostedService> logger,
        IAlarmForwarder forwarder,
        IDataChannels dataChannels,
        IAlarmRepository alarmRepo,
        IOptions<MQTTOptions> mqttOptions)
    {
        _logger = logger;
        _forwarder = forwarder;
        _dataChannels = dataChannels;
        _alarmRepo = alarmRepo;
        _mqttOptions = mqttOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alarm Forwarder started: MQTT Broker={Broker}:{Port}",
            _mqttOptions.Broker, _mqttOptions.Port);

        var unpublishedTask = PublishUnpublishedAlarmsAsync(stoppingToken);
        var channelTask = ProcessAlarmChannelAsync(stoppingToken);

        await Task.WhenAny(unpublishedTask, channelTask);
    }

    private async Task PublishUnpublishedAlarmsAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var unpublished = await _alarmRepo.GetUnpublishedAsync(100, stoppingToken);
                foreach (var alarm in unpublished)
                {
                    await _forwarder.PublishAlarmAsync(alarm, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing unpublished alarms");
            }
        }
    }

    private async Task ProcessAlarmChannelAsync(CancellationToken stoppingToken)
    {
        await foreach (var eventData in _dataChannels.AlarmTriggeredReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _forwarder.ProcessAlarmTriggeredAsync(eventData, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing alarm from channel for alarm {AlarmId}", eventData.AlarmId);
            }
        }
    }
}
