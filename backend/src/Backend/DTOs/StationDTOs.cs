namespace AntennaMonitoring.DTOs;

public class BaseStationDTO
{
    public Guid Id { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal Longitude { get; set; }
    public decimal Latitude { get; set; }
    public decimal? Altitude { get; set; }
    public string? AntennaModel { get; set; }
    public int ChannelCount { get; set; }
    public int ArrayRows { get; set; }
    public int ArrayColumns { get; set; }
    public decimal? FrequencyBand { get; set; }
    public string Status { get; set; } = string.Empty;
    public int NormalChannels { get; set; }
    public int WarningChannels { get; set; }
    public int FaultChannels { get; set; }
    public int ActiveAlarms { get; set; }
}

public class BaseStationSummaryDTO
{
    public Guid Id { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public decimal Longitude { get; set; }
    public decimal Latitude { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ActiveAlarms { get; set; }
    public int CriticalAlarms { get; set; }
    public int WarningAlarms { get; set; }
}

public class CreateBaseStationDTO
{
    public string StationName { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal Longitude { get; set; }
    public decimal Latitude { get; set; }
    public decimal? Altitude { get; set; }
    public string? AntennaModel { get; set; }
    public int ChannelCount { get; set; } = 64;
    public int ArrayRows { get; set; } = 8;
    public int ArrayColumns { get; set; } = 8;
    public decimal? FrequencyBand { get; set; }
}

public class UpdateBaseStationDTO
{
    public string StationName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? Latitude { get; set; }
    public string Status { get; set; } = string.Empty;
}
