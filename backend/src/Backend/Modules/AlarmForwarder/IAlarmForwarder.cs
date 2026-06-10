using AntennaMonitoring.Messages;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Modules.AlarmForwarder;

public interface IAlarmForwarder
{
    Task<Alarm?> CheckChannelAlarmsAsync(Guid stationId, Channel channel, double swr, double temperature, CancellationToken cancellationToken);
    Task<Alarm?> CheckSectorFailureAsync(Guid stationId, IReadOnlyList<Channel> channels, CancellationToken cancellationToken);
    Task<bool> PublishAlarmAsync(Alarm alarm, CancellationToken cancellationToken);
    Task ProcessAlarmTriggeredAsync(AlarmTriggeredEvent eventData, CancellationToken cancellationToken);
}
