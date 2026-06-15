namespace TransitInfoAPI.Entities;

public class Agency
{
    public int Id { get; set; }
    public int FeedVersionId { get; set; }
    public FeedVersion FeedVersion { get; set; } = null!;

    public string AgencyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Timezone { get; set; }
    public string? Language { get; set; }
    public string? Phone { get; set; }
    public string? FareUrl { get; set; }
    public string? Email { get; set; }

    public int? OperatorId { get; set; }
    public Operator? Operator { get; set; }
}
