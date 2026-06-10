using System.Threading.Channels;
using AntennaMonitoring.Messages;

namespace AntennaMonitoring.Messages;

public interface IDataChannels
{
    ChannelWriter<EcpriDataReceivedEvent> EcpriDataWriter { get; }
    ChannelReader<EcpriDataReceivedEvent> EcpriDataReader { get; }

    ChannelWriter<CalibrationRequest> CalibrationRequestWriter { get; }
    ChannelReader<CalibrationRequest> CalibrationRequestReader { get; }

    ChannelWriter<DiagnosisRequest> DiagnosisRequestWriter { get; }
    ChannelReader<DiagnosisRequest> DiagnosisRequestReader { get; }

    ChannelWriter<AlarmTriggeredEvent> AlarmTriggeredWriter { get; }
    ChannelReader<AlarmTriggeredEvent> AlarmTriggeredReader { get; }
}

public class DataChannels : IDataChannels
{
    private readonly Channel<EcpriDataReceivedEvent> _ecpriDataChannel;
    private readonly Channel<CalibrationRequest> _calibrationRequestChannel;
    private readonly Channel<DiagnosisRequest> _diagnosisRequestChannel;
    private readonly Channel<AlarmTriggeredEvent> _alarmTriggeredChannel;

    public DataChannels()
    {
        _ecpriDataChannel = Channel.CreateUnbounded<EcpriDataReceivedEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _calibrationRequestChannel = Channel.CreateBounded<CalibrationRequest>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _diagnosisRequestChannel = Channel.CreateBounded<DiagnosisRequest>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _alarmTriggeredChannel = Channel.CreateUnbounded<AlarmTriggeredEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    }

    public ChannelWriter<EcpriDataReceivedEvent> EcpriDataWriter => _ecpriDataChannel.Writer;
    public ChannelReader<EcpriDataReceivedEvent> EcpriDataReader => _ecpriDataChannel.Reader;

    public ChannelWriter<CalibrationRequest> CalibrationRequestWriter => _calibrationRequestChannel.Writer;
    public ChannelReader<CalibrationRequest> CalibrationRequestReader => _calibrationRequestChannel.Reader;

    public ChannelWriter<DiagnosisRequest> DiagnosisRequestWriter => _diagnosisRequestChannel.Writer;
    public ChannelReader<DiagnosisRequest> DiagnosisRequestReader => _diagnosisRequestChannel.Reader;

    public ChannelWriter<AlarmTriggeredEvent> AlarmTriggeredWriter => _alarmTriggeredChannel.Writer;
    public ChannelReader<AlarmTriggeredEvent> AlarmTriggeredReader => _alarmTriggeredChannel.Reader;
}
