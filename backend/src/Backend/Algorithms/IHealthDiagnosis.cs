using AntennaMonitoring.DTOs;
using AntennaMonitoring.Models;

namespace AntennaMonitoring.Algorithms;

public interface IHealthDiagnosis
{
    string ModelName { get; }
    Task<DiagnosisResult> DiagnoseAsync(
        Guid stationId,
        Channel channel,
        IEnumerable<ChannelMetrics> historicalMetrics,
        CancellationToken cancellationToken);
    Task<IEnumerable<DiagnosisResult>> DiagnoseBatchAsync(
        Guid stationId,
        IEnumerable<Channel> channels,
        IEnumerable<ChannelMetrics> historicalMetrics,
        CancellationToken cancellationToken);
}

public class DiagnosisResult
{
    public bool Success { get; set; }
    public string ModelType { get; set; } = string.Empty;
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double SwrValue { get; set; }
    public double TemperatureValue { get; set; }
    public double FailureProbability { get; set; }
    public double SwrPredicted { get; set; }
    public double TemperaturePredicted { get; set; }
    public double AnomalyScore { get; set; }
    public int PredictionHorizonHours { get; set; }
    public int PredictedFailureHours { get; set; }
    public double HealthScore { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public DateTime DiagnosisTime { get; set; } = DateTime.UtcNow;
}

public class DiagnosisFeature
{
    public double SwrMean { get; set; }
    public double SwrStd { get; set; }
    public double SwrMax { get; set; }
    public double SwrTrend { get; set; }
    public double TempMean { get; set; }
    public double TempStd { get; set; }
    public double TempMax { get; set; }
    public double TempTrend { get; set; }
    public double AmpMean { get; set; }
    public double AmpStd { get; set; }
    public double PhaseMean { get; set; }
    public double PhaseStd { get; set; }
    public double TxPowerMean { get; set; }
    public double RxPowerMean { get; set; }
    public double BerMean { get; set; }
}
