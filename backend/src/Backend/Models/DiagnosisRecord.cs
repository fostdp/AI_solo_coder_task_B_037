namespace AntennaMonitoring.Models;

public class DiagnosisRecord
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public Guid ChannelId { get; set; }
    public DateTime DiagnosisTime { get; set; }
    public decimal? SwrValue { get; set; }
    public decimal? TemperatureValue { get; set; }
    public decimal? FailureProbability { get; set; }
    public string? ModelType { get; set; }
    public int? PredictionHorizonHours { get; set; }
    public string? Recommendation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
    public Channel? Channel { get; set; }
}
