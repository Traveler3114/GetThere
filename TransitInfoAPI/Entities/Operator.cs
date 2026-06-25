namespace TransitInfoAPI.Entities;

public class Operator
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? SupersedesIds { get; set; }
    public string? WikidataId { get; set; }
    public string? Tags { get; set; }
    public string? AssociatedFeeds { get; set; }

    public ICollection<Feed> Feeds { get; set; } = [];
    public ICollection<Agency> Agencies { get; set; } = [];
    public ICollection<CanonicalRoute> Routes { get; set; } = [];
    public ICollection<CanonicalStationOperator> StationOperators { get; set; } = [];
    public ICollection<MobilityProvider> MobilityProviders { get; set; } = [];
}
