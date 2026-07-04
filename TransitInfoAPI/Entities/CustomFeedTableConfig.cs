using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CustomFeedTableConfig
{
    public int Id { get; set; }
    public int CustomFeedId { get; set; }
    public CustomFeed CustomFeed { get; set; } = null!;
    public int SortOrder { get; set; }
    public string Url { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public ResponseFormat ResponseFormat { get; set; }
    public string DataPath { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string? PaginationConfig { get; set; }
    public string? DistinctBy { get; set; }
    public bool IsStatic { get; set; }
    public ICollection<CustomFeedTableFieldMapping> FieldMappings { get; set; } = [];
}
