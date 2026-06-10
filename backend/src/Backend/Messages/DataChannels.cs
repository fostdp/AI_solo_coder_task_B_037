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

    ChannelWriter<SensorDataReceivedEvent> SensorDataWriter { get; }
    ChannelReader<SensorDataReceivedEvent> SensorDataReader { get; }

    ChannelWriter<DeformationRequest> DeformationRequestWriter { get; }
    ChannelReader<DeformationRequest> DeformationRequestReader { get; }

    ChannelWriter<CoSiteInterferenceRequest> CoSiteInterferenceRequestWriter { get; }
    ChannelReader<CoSiteInterferenceRequest> CoSiteInterferenceRequestReader { get; }

    ChannelWriter<PaEfficiencyRequest> PaEfficiencyRequestWriter { get; }
    ChannelReader<PaEfficiencyRequest> PaEfficiencyRequestReader { get; }

    ChannelWriter<SpectrumScanRequest> SpectrumScanRequestWriter { get; }
    ChannelReader<SpectrumScanRequest> SpectrumScanRequestReader { get; }
}

public class DataChannels : IDataChannels
{
    private readonly Channel<EcpriDataReceivedEvent> _ecpriDataChannel;
    private readonly Channel<CalibrationRequest> _calibrationRequestChannel;
    private readonly Channel<DiagnosisRequest> _diagnosisRequestChannel;
    private readonly Channel<AlarmTriggeredEvent> _alarmTriggeredChannel;
    private readonly Channel<SensorDataReceivedEvent> _sensorDataChannel;
    private readonly Channel<DeformationRequest> _deformationRequestChannel;
    private readonly Channel<CoSiteInterferenceRequest> _cositeInterferenceRequestChannel;
    private readonly Channel<PaEfficiencyRequest> _paEfficiencyRequestChannel;
    private readonly Channel<SpectrumScanRequest> _spectrumScanRequestChannel;

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

        _sensorDataChannel = Channel.CreateUnbounded<SensorDataReceivedEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _deformationRequestChannel = Channel.CreateBounded<DeformationRequest>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _cositeInterferenceRequestChannel = Channel.CreateBounded<CoSiteInterferenceRequest>(
            new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _paEfficiencyRequestChannel = Channel.CreateBounded<PaEfficiencyRequest>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _spectrumScanRequestChannel = Channel.CreateBounded<SpectrumScanRequest>(
            new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public ChannelWriter<EcpriDataReceivedEvent> EcpriDataWriter => _ecpriDataChannel.Writer;
    public ChannelReader<EcpriDataReceivedEvent> EcpriDataReader => _ecpriDataChannel.Reader;

    public ChannelWriter<CalibrationRequest> CalibrationRequestWriter => _calibrationRequestChannel.Writer;
    public ChannelReader<CalibrationRequest> CalibrationRequestReader => _calibrationRequestChannel.Reader;

    public ChannelWriter<DiagnosisRequest> DiagnosisRequestWriter => _diagnosisRequestChannel.Writer;
    public ChannelReader<DiagnosisRequest> DiagnosisRequestReader => _diagnosisRequestChannel.Reader;

    public ChannelWriter<AlarmTriggeredEvent> AlarmTriggeredWriter => _alarmTriggeredChannel.Writer;
    public ChannelReader<AlarmTriggeredEvent> AlarmTriggeredReader => _alarmTriggeredChannel.Reader;

    public ChannelWriter<SensorDataReceivedEvent> SensorDataWriter => _sensorDataChannel.Writer;
    public ChannelReader<SensorDataReceivedEvent> SensorDataReader => _sensorDataChannel.Reader;

    public ChannelWriter<DeformationRequest> DeformationRequestWriter => _deformationRequestChannel.Writer;
    public ChannelReader<DeformationRequest> DeformationRequestReader => _deformationRequestChannel.Reader;

    public ChannelWriter<CoSiteInterferenceRequest> CoSiteInterferenceRequestWriter => _cositeInterferenceRequestChannel.Writer;
    public ChannelReader<CoSiteInterferenceRequest> CoSiteInterferenceRequestReader => _cositeInterferenceRequestChannel.Reader;

    public ChannelWriter<PaEfficiencyRequest> PaEfficiencyRequestWriter => _paEfficiencyRequestChannel.Writer;
    public ChannelReader<PaEfficiencyRequest> PaEfficiencyRequestReader => _paEfficiencyRequestChannel.Reader;

    public ChannelWriter<SpectrumScanRequest> SpectrumScanRequestWriter => _spectrumScanRequestChannel.Writer;
    public ChannelReader<SpectrumScanRequest> SpectrumScanRequestReader => _spectrumScanRequestChannel.Reader;
}
