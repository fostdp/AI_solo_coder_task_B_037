namespace AntennaMonitoring.DTOs;

public class ChannelDTO
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public int ChannelIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public decimal? TxPower { get; set; }
    public decimal NominalAmplitude { get; set; }
    public decimal NominalPhase { get; set; }
    public decimal CalibrationCoeffAmplitude { get; set; }
    public decimal CalibrationCoeffPhase { get; set; }
    public DateTime? LastCalibrationTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal FailureProbability { get; set; }
    public double CurrentAmplitude { get; set; }
    public double CurrentPhase { get; set; }
    public double CurrentSwr { get; set; }
    public double CurrentTemperature { get; set; }
}

public class ChannelStatusDTO
{
    public Guid Id { get; set; }
    public int ChannelIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public string Status { get; set; } = string.Empty;
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double Swr { get; set; }
    public double Temperature { get; set; }
    public double FailureProbability { get; set; }
}

public class ChannelMetricsDTO
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public DateTime Timestamp { get; set; }
    public double Amplitude { get; set; }
    public double Phase { get; set; }
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double Swr { get; set; }
    public double PaTemperature { get; set; }
    public double TxPower { get; set; }
}

public class ChannelTrendDTO
{
    public DateTime Timestamp { get; set; }
    public double Amplitude { get; set; }
    public double Swr { get; set; }
    public double Temperature { get; set; }
}

public class CalibrationResultDTO
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double AmplitudeDeviation { get; set; }
    public double PhaseDeviation { get; set; }
    public double CalibrationCoeffAmplitude { get; set; }
    public double CalibrationCoeffPhase { get; set; }
    public double SllBefore { get; set; }
    public double SllAfter { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public DateTime CalibrationTime { get; set; }
}

public class DiagnosisResultDTO
{
    public Guid ChannelId { get; set; }
    public int ChannelIndex { get; set; }
    public double SwrValue { get; set; }
    public double TemperatureValue { get; set; }
    public double FailureProbability { get; set; }
    public string ModelType { get; set; } = string.Empty;
    public int PredictionHorizonHours { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public DateTime DiagnosisTime { get; set; }
}

public class RunCalibrationRequest
{
    public Guid StationId { get; set; }
    public string AlgorithmType { get; set; } = string.Empty;
}

public class CalibrationRecordDTO
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public string? StationName { get; set; }
    public string? StationCode { get; set; }
    public Guid ChannelId { get; set; }
    public int? ChannelIndex { get; set; }
    public DateTime CalibrationTime { get; set; }
    public decimal? AmplitudeDeviation { get; set; }
    public decimal? PhaseDeviation { get; set; }
    public decimal? CalibrationCoeffAmplitude { get; set; }
    public decimal? CalibrationCoeffPhase { get; set; }
    public decimal? SllBefore { get; set; }
    public decimal? SllAfter { get; set; }
    public string? Algorithm { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AlgorithmInfoDTO
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
