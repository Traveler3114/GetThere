using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CustomFeed
{
    public int Id { get; set; }
    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string? AuthConfig { get; set; }
    public ResponseFormat ResponseFormat { get; set; }
    public OutputFormat OutputFormat { get; set; }
    public string DataPath { get; set; } = string.Empty;
    public string? TargetTable { get; set; }
    public string? PaginationConfig { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 3600;
    public bool IsActive { get; set; } = true;
    public bool IsScheduleCapable { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public ICollection<CustomFeedFieldMapping> FieldMappings { get; set; } = [];
    public ICollection<CustomFeedRun> Runs { get; set; } = [];
    public ICollection<CustomFeedTableConfig> TableConfigs { get; set; } = [];
}
