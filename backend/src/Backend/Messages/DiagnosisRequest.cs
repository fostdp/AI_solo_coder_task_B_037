using MediatR;
using AntennaMonitoring.Models;
using AntennaMonitoring.Algorithms;

namespace AntennaMonitoring.Messages;

public class DiagnosisRequest : IRequest<DiagnosisResponse>
{
    public Guid StationId { get; set; }
    public string ModelType { get; set; } = string.Empty;
    public IReadOnlyList<Channel> Channels { get; set; } = Array.Empty<Channel>();
    public IReadOnlyList<ChannelMetrics> Metrics { get; set; } = Array.Empty<ChannelMetrics>();
}

public class DiagnosisResponse
{
    public bool Success { get; set; }
    public DateTime DiagnosisTime { get; set; }
    public string ModelType { get; set; } = string.Empty;
    public IReadOnlyList<ChannelDiagnosisResult> Results { get; set; } = Array.Empty<ChannelDiagnosisResult>();
    public string? ErrorMessage { get; set; }
}

public class ChannelDiagnosisResult
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double SwrValue { get; set; }
    public double TemperatureValue { get; set; }
    public double FailureProbability { get; set; }
    public double SwrPredicted { get; set; }
    public double TemperaturePredicted { get; set; }
    public double AnomalyScore { get; set; }
    public int PredictedFailureHours { get; set; }
    public double HealthScore { get; set; }
    public int PredictionHorizonHours { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}
