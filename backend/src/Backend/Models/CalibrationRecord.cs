namespace AntennaMonitoring.Models;

public class CalibrationRecord
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public Guid ChannelId { get; set; }
    public DateTime CalibrationTime { get; set; }
    public decimal? AmplitudeDeviation { get; set; }
    public decimal? PhaseDeviation { get; set; }
    public decimal? CalibrationCoeffAmplitude { get; set; }
    public decimal? CalibrationCoeffPhase { get; set; }
    public decimal? SllBefore { get; set; }
    public decimal? SllAfter { get; set; }
    public string? Algorithm { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
    public Channel? Channel { get; set; }
}
