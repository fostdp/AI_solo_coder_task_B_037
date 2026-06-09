using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Algorithms;

public interface IBeamformingCalibration
{
    string AlgorithmName { get; }
    Task<CalibrationResult> CalibrateAsync(
        Guid stationId,
        IEnumerable<Channel> channels,
        IEnumerable<ChannelMetrics> currentMetrics,
        CancellationToken cancellationToken);
    double CalculateSLL(IEnumerable<Channel> channels, IEnumerable<ChannelMetrics> metrics);
    double[] CalculateBeamPattern(IEnumerable<Channel> channels, IEnumerable<ChannelMetrics> metrics,
        double startAngle = -90, double endAngle = 90, double step = 0.5);
}

public class CalibrationResult
{
    public bool Success { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public double SllBefore { get; set; }
    public double SllAfter { get; set; }
    public bool Converged { get; set; }
    public int Iterations { get; set; }
    public List<ChannelCalibration> ChannelCalibrations { get; set; } = new();
    public DateTime CalibrationTime { get; set; } = DateTime.UtcNow;
}

public class ChannelCalibration
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double CalibrationCoeffAmplitude { get; set; }
    public double CalibrationCoeffPhase { get; set; }
}
