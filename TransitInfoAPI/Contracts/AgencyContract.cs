namespace TransitInfoAPI.Contracts;

public class AgencyResponse
{
    public int Id { get; set; }
    public string AgencyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Timezone { get; set; }
    public string? Phone { get; set; }
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }
    public int FeedVersionId { get; set; }
}
