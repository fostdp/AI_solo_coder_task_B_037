using MediatR;
using AntennaMonitoring.Models;
using AntennaMonitoring.Algorithms;

namespace AntennaMonitoring.Messages;

public class CalibrationRequest : IRequest<CalibrationResponse>
{
    public Guid StationId { get; set; }
    public string AlgorithmType { get; set; } = string.Empty;
    public IReadOnlyList<Channel> Channels { get; set; } = Array.Empty<Channel>();
    public IReadOnlyList<ChannelMetrics> Metrics { get; set; } = Array.Empty<ChannelMetrics>();
}

public class CalibrationResponse
{
    public bool Success { get; set; }
    public bool Converged { get; set; }
    public double SllBefore { get; set; }
    public double SllAfter { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public DateTime CalibrationTime { get; set; }
    public IReadOnlyList<ChannelCalibrationResult> Results { get; set; } = Array.Empty<ChannelCalibrationResult>();
    public string? ErrorMessage { get; set; }
}

public class ChannelCalibrationResult
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double CalibrationCoeffAmplitude { get; set; }
    public double CalibrationCoeffPhase { get; set; }
}
