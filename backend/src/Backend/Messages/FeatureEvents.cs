using MediatR;

namespace AntennaMonitoring.Messages;

public record SensorDataReceivedEvent(
    Guid StationId,
    IReadOnlyList<SensorData> SensorData,
    DateTime Timestamp) : INotification;

public record DeformationCompletedEvent(
    Guid StationId,
    IReadOnlyList<DeformationResult> Results,
    DateTime Timestamp) : INotification;

public record DeformationThresholdExceededEvent(
    Guid StationId,
    int SensorIndex,
    string DeformationZone,
    double DisplacementMm,
    double CorrectionAzimuth,
    double CorrectionElevation,
    DateTime Timestamp) : INotification;

public record CoSiteInterferenceCompletedEvent(
    Guid StationId,
    IReadOnlyList<CoSiteInterferenceResult> Results,
    DateTime Timestamp) : INotification;

public record IsolationInsufficientEvent(
    Guid StationId,
    Guid InterferingAntennaId,
    string InterferingOperator,
    double IsolationDb,
    double ThresholdDb,
    string Recommendation,
    DateTime Timestamp) : INotification;

public record PaEfficiencyCompletedEvent(
    Guid StationId,
    IReadOnlyList<PaEfficiencyResult> Results,
    DateTime Timestamp) : INotification;

public record PaEfficiencyLowEvent(
    Guid StationId,
    Guid ChannelId,
    int ChannelIndex,
    double EfficiencyPercent,
    double ThresholdPercent,
    string ReplacementReason,
    DateTime Timestamp) : INotification;

public record SpectrumScanCompletedEvent(
    Guid StationId,
    SpectrumScanResult Result,
    DateTime Timestamp) : INotification;

public record InterferenceDetectedEvent(
    Guid StationId,
    double[] InterferenceFrequenciesMhz,
    double[] InterferencePowersDbm,
    double[] InterferenceDirectionsDeg,
    DateTime Timestamp) : INotification;

public record NullSteeringAppliedEvent(
    Guid StationId,
    double[] NullAnglesDeg,
    double[] NullDepthsDb,
    DateTime Timestamp) : INotification;
