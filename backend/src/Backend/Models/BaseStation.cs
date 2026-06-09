using NetTopologySuite.Geometries;

namespace AntennaMonitoring.Models;

public class BaseStation
{
    public Guid Id { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal Longitude { get; set; }
    public decimal Latitude { get; set; }
    public decimal? Altitude { get; set; }
    public Point? Location { get; set; }
    public string? AntennaModel { get; set; }
    public int ChannelCount { get; set; } = 64;
    public int ArrayRows { get; set; } = 8;
    public int ArrayColumns { get; set; } = 8;
    public decimal? FrequencyBand { get; set; }
    public DateTime? InstallationDate { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Channel> Channels { get; set; } = new List<Channel>();
    public ICollection<Alarm> Alarms { get; set; } = new List<Alarm>();
    public ICollection<CalibrationRecord> CalibrationRecords { get; set; } = new List<CalibrationRecord>();
    public ICollection<DiagnosisRecord> DiagnosisRecords { get; set; } = new List<DiagnosisRecord>();
}
