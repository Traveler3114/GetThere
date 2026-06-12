namespace GetThereAPI.Entities;

public enum MobilityType
{
    BIKE_STATION,
    BIKE_FREEFORM,
    SCOOTER,
    MOPED
}

public class MobilityProvider
{
    public int    Id           { get; set; }
    public string Name         { get; set; } = string.Empty;
    public string? LogoUrl     { get; set; }

    public MobilityType Type { get; set; }

    public string  ApiBaseUrl    { get; set; } = string.Empty;
    public string? ApiKey        { get; set; }

    /// <summary>Provider-specific JSON config, e.g. {"cityUid": 483} for Nextbike.</summary>
    public string? AdapterConfig { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Country> Countries { get; set; } = [];
    public ICollection<City>    Cities    { get; set; } = [];
}
