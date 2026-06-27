using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

public class CreateCustomFeedRequest
{
    [Range(1, int.MaxValue)] public int OperatorId { get; set; }
    public int? MobilityProviderId { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, Url] public string BaseUrl { get; set; } = string.Empty;
    [StringLength(10)] public string HttpMethod { get; set; } = "GET";
    public string? AuthConfig { get; set; }
    [Required] public string ResponseFormat { get; set; } = string.Empty;
    [Required] public string OutputFormat { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    [StringLength(100)] public string? TargetTable { get; set; }
    public string? PaginationConfig { get; set; }
    [Range(60, int.MaxValue)] public int RefreshIntervalSeconds { get; set; } = 3600;
    public List<CreateFieldMappingRequest> FieldMappings { get; set; } = [];
}

public class UpdateCustomFeedRequest
{
    public int? OperatorId { get; set; }
    public int? MobilityProviderId { get; set; }
    [StringLength(200)] public string? Name { get; set; }
    [Url] public string? BaseUrl { get; set; }
    [StringLength(10)] public string? HttpMethod { get; set; }
    public string? AuthConfig { get; set; }
    public string? ResponseFormat { get; set; }
    public string? OutputFormat { get; set; }
    public string? DataPath { get; set; }
    [StringLength(100)] public string? TargetTable { get; set; }
    public string? PaginationConfig { get; set; }
    [Range(60, int.MaxValue)] public int? RefreshIntervalSeconds { get; set; }
    public bool? IsActive { get; set; }
    public List<CreateFieldMappingRequest>? FieldMappings { get; set; }
}

public class CreateFieldMappingRequest
{
    [Required, StringLength(200)] public string SourceExpression { get; set; } = string.Empty;
    [Required, StringLength(100)] public string TargetField { get; set; } = string.Empty;
    [Required] public string MappingKind { get; set; } = string.Empty;
}

public class CustomFeedResponse
{
    public int Id { get; set; }
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public int? MobilityProviderId { get; set; }
    public string? MobilityProviderName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string? AuthConfig { get; set; }
    public string ResponseFormat { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string? TargetTable { get; set; }
    public string? PaginationConfig { get; set; }
    public int RefreshIntervalSeconds { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public List<FieldMappingResponse> FieldMappings { get; set; } = [];
}

public class FieldMappingResponse
{
    public int Id { get; set; }
    public int CustomFeedId { get; set; }
    public int SortOrder { get; set; }
    public string SourceExpression { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public string MappingKind { get; set; } = string.Empty;
}

public class CustomFeedRunResponse
{
    public int Id { get; set; }
    public int CustomFeedId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RecordsProduced { get; set; }
    public string? LogText { get; set; }
}

public class CustomFeedPreviewResponse
{
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public int TotalRows { get; set; }
    public List<string> LogLines { get; set; } = [];
}

public class CustomFeedDiscoverResponse
{
    public JsonStructureNode? Structure { get; set; }
    public List<string> ArrayPaths { get; set; } = [];
    public List<string> LogLines { get; set; } = [];
}

public class JsonStructureNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Sample { get; set; }
    public int? ArrayItemCount { get; set; }
    public string? ArrayItemType { get; set; }
    public List<JsonStructureNode> Children { get; set; } = [];
}
