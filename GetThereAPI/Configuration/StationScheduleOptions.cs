namespace GetThereAPI.Configuration;

public class StationScheduleOptions
{
    public const string SectionName = "StationSchedule";
    public List<StationSourceStrategyConfig> Stations { get; set; } = [];
}

public class StationSourceStrategyConfig
{
    public string StationKey { get; set; } = "";
    public string SourceMode { get; set; } = "OperatorMerge"; // StationApiPreferred|OperatorMerge|StationApiPlusFallback
    public string? StationApiType { get; set; } // e.g. Noop|HttpJson
    public string? StationApiUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 4;
}
