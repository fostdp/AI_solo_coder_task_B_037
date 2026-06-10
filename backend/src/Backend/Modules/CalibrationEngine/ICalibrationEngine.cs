using AntennaMonitoring.Messages;

namespace AntennaMonitoring.Modules.CalibrationEngine;

public interface ICalibrationEngine
{
    Task<CalibrationResponse> RunCalibrationAsync(CalibrationRequest request, CancellationToken cancellationToken);
    Task<Dictionary<int, (double Amplitude, double Phase)>> ExtractDeviationsAsync(
        Guid stationId,
        IReadOnlyList<Models.Channel> channels,
        IReadOnlyList<Models.ChannelMetrics> metrics,
        CancellationToken cancellationToken);
}
