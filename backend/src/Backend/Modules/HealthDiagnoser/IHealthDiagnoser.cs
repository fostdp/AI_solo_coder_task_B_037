using AntennaMonitoring.Messages;

namespace AntennaMonitoring.Modules.HealthDiagnoser;

public interface IHealthDiagnoser
{
    Task<DiagnosisResponse> RunDiagnosisAsync(DiagnosisRequest request, CancellationToken cancellationToken);
    Task<DiagnosisFeature> ExtractFeaturesAsync(
        Models.Channel channel,
        IReadOnlyList<Models.ChannelMetrics> metrics,
        CancellationToken cancellationToken);
}
