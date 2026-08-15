using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

/// <summary>A non-GTFS source and everything needed to read it.</summary>
public class CustomSourceResponse
{
    public int Id { get; set; }
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? ExtractorKey { get; set; }

    /// <summary>
    /// Whether a credential is configured — deliberately <b>not</b> the credential itself.
    /// <para>
    /// This was <c>AuthConfig</c>, the raw JSON holding an operator's bearer token or basic-auth
    /// password, returned verbatim by <c>GET /custom-sources</c> to anyone holding
    /// <c>customsources.view</c>. The editor only ever needed to know whether one was set; sending
    /// the secret to render a form field made every viewer a credential holder.
    /// </para>
    /// </summary>
    public bool HasAuth { get; set; }

    public int RefreshIntervalSeconds { get; set; }
    public bool IsActive { get; set; }
    public DateOnly? ServiceWindowStart { get; set; }
    public DateOnly? ServiceWindowEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public List<CustomSourceRequestResponse> Requests { get; set; } = [];
}

public class CustomSourceRequestResponse
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string TargetSection { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string Format { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string? PaginationConfig { get; set; }
    public string? DistinctBy { get; set; }
    public List<CustomSourceMappingResponse> Mappings { get; set; } = [];
}

public class CustomSourceMappingResponse
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string SourceExpression { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

public class CustomSourceRunResponse
{
    public int Id { get; set; }
    public int CustomSourceId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RecordsProduced { get; set; }
    public string? LogText { get; set; }
    public int? FeedVersionId { get; set; }
}

public record CreateCustomSourceRequest
{
    [Range(1, int.MaxValue)] public int OperatorId { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public string Kind { get; set; } = string.Empty;
    [StringLength(100)] public string? ExtractorKey { get; set; }
    public string? AuthConfig { get; set; }
    [Range(60, int.MaxValue)] public int RefreshIntervalSeconds { get; set; } = 3600;
    public DateOnly? ServiceWindowStart { get; set; }
    public DateOnly? ServiceWindowEnd { get; set; }
    public List<CreateCustomSourceRequestItem> Requests { get; set; } = [];
}

public record UpdateCustomSourceRequest
{
    public int? OperatorId { get; set; }
    [StringLength(200)] public string? Name { get; set; }
    public string? Kind { get; set; }
    [StringLength(100)] public string? ExtractorKey { get; set; }

    /// <summary>
    /// Null leaves the stored credential untouched; an empty string clears it. The editor never
    /// receives the current value, so it can only send one the admin has just typed.
    /// </summary>
    public string? AuthConfig { get; set; }

    [Range(60, int.MaxValue)] public int? RefreshIntervalSeconds { get; set; }
    public bool? IsActive { get; set; }
    public DateOnly? ServiceWindowStart { get; set; }
    public DateOnly? ServiceWindowEnd { get; set; }

    /// <summary>Null leaves the existing requests alone; a list replaces them wholesale.</summary>
    public List<CreateCustomSourceRequestItem>? Requests { get; set; }
}

public record CreateCustomSourceRequestItem
{
    [Required] public string TargetSection { get; set; } = string.Empty;

    /// <summary>
    /// An <c>http(s)://</c> endpoint, or <c>upload://name</c> for a file stored against this source.
    /// <para>
    /// Deliberately not <c>[Url]</c>: that attribute only accepts http/https/ftp, so it rejected
    /// every uploaded file — and because it fails at model binding, the request came back 400 with
    /// nothing pointing at the scheme as the cause. The scheme is checked in the manager instead,
    /// where the message can say which schemes are allowed.
    /// </para>
    /// </summary>
    [Required, StringLength(2000)] public string Url { get; set; } = string.Empty;
    [StringLength(10)] public string HttpMethod { get; set; } = "GET";
    [Required] public string Format { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string? PaginationConfig { get; set; }
    [StringLength(100)] public string? DistinctBy { get; set; }
    public List<CreateCustomSourceMappingItem> Mappings { get; set; } = [];
}

public record CreateCustomSourceMappingItem
{
    [Required, StringLength(400)] public string SourceExpression { get; set; } = string.Empty;
    [Required, StringLength(100)] public string TargetField { get; set; } = string.Empty;
    [Required] public string Kind { get; set; } = string.Empty;
}

/// <summary>A file stored for a source, and the URL that points a request at it.</summary>
public class UploadedFileResponse
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }

    /// <summary>Paste into a request's URL — e.g. <c>upload://timetable.xlsx</c>.</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>A registered C# extractor an admin can assign to a source.</summary>
public class ExtractorResponse
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// What a sample fetch found, so the editor can offer the response's own shape instead of asking an
/// admin to guess a data path.
/// </summary>
public class CustomSourceDiscoverResponse
{
    public StructureNode? Structure { get; set; }

    /// <summary>Every path that holds an array — the candidates for <c>DataPath</c>.</summary>
    public List<string> ArrayPaths { get; set; } = [];
    public List<string> LogLines { get; set; } = [];
}

public class StructureNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Sample { get; set; }
    public int? ArrayItemCount { get; set; }
    public List<StructureNode> Children { get; set; } = [];
}

/// <summary>
/// The result of running the extraction without importing it, so a mapping can be checked against
/// real rows before it writes anything.
/// </summary>
public class CustomSourcePreviewResponse
{
    public string Completeness { get; set; } = string.Empty;
    public List<string> SectionsPresent { get; set; } = [];
    public List<string> SectionsSynthesized { get; set; } = [];
    public Dictionary<string, int> RowCounts { get; set; } = [];

    /// <summary>A handful of mapped rows per section, for eyeballing.</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> Samples { get; set; } = [];
    public List<string> LogLines { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
