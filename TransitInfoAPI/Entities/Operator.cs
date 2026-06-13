using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class Operator
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public OperatorType OperatorType { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public ICollection<Feed> Feeds { get; set; } = [];
    public ICollection<CanonicalRoute> Routes { get; set; } = [];
    public ICollection<CanonicalStationOperator> StationOperators { get; set; } = [];
    public ICollection<MobilityProvider> MobilityProviders { get; set; } = [];
}
