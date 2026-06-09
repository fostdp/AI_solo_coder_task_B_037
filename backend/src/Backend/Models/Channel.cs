namespace AntennaMonitoring.Models;

public class Channel
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public int ChannelIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public decimal? TxPower { get; set; }
    public decimal NominalAmplitude { get; set; } = 1.0m;
    public decimal NominalPhase { get; set; } = 0.0m;
    public decimal CalibrationCoeffAmplitude { get; set; } = 1.0m;
    public decimal CalibrationCoeffPhase { get; set; } = 0.0m;
    public DateTime? LastCalibrationTime { get; set; }
    public string Status { get; set; } = "normal";
    public decimal FailureProbability { get; set; } = 0.0m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public BaseStation? Station { get; set; }
    public ICollection<Alarm> Alarms { get; set; } = new List<Alarm>();
    public ICollection<CalibrationRecord> CalibrationRecords { get; set; } = new List<CalibrationRecord>();
    public ICollection<DiagnosisRecord> DiagnosisRecords { get; set; } = new List<DiagnosisRecord>();
}
