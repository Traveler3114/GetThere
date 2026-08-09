using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

/// <summary>
/// An operator's data that is not GTFS, described well enough to import it.
/// <para>
/// A source is either declarative — a set of <see cref="CustomSourceRequest"/>s the engine executes —
/// or delegated to a named C# adapter via <see cref="ExtractorKey"/>. The escape hatch exists because
/// some operators cannot be expressed as configuration at any reasonable cost, and the alternative to
/// admitting that is a config language that slowly grows into a bad programming language.
/// </para>
/// </summary>
public class CustomSource
{
    public int Id { get; set; }

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public CustomSourceKind Kind { get; set; }

    /// <summary>
    /// When set, a registered <c>ICustomExtractor</c> with this key produces the document and the
    /// declarative requests below are ignored entirely.
    /// </summary>
    public string? ExtractorKey { get; set; }

    /// <summary>JSON. Shape depends on the auth type — basic, bearer, OAuth2 or a login-token exchange.</summary>
    public string? AuthConfig { get; set; }

    public int RefreshIntervalSeconds { get; set; } = 3600;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The window a synthesized calendar covers, for sources that publish departures with no service
    /// calendar of their own. Null means the completer will not invent one.
    /// </summary>
    public DateOnly? ServiceWindowStart { get; set; }
    public DateOnly? ServiceWindowEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }

    public ICollection<CustomSourceRequest> Requests { get; set; } = [];
    public ICollection<CustomSourceRun> Runs { get; set; } = [];
}

/// <summary>
/// One fetch, filling one section of the document.
/// </summary>
public class CustomSourceRequest
{
    public int Id { get; set; }

    public int CustomSourceId { get; set; }
    public CustomSource CustomSource { get; set; } = null!;

    public int SortOrder { get; set; }
    public TransitSection TargetSection { get; set; }

    public string Url { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public CustomSourceFormat Format { get; set; }

    /// <summary>Path to the array of rows within the response — e.g. <c>data.stops</c>.</summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>JSON; null or <see cref="PaginationKind.None"/> means a single request.</summary>
    public string? PaginationConfig { get; set; }

    /// <summary>Field to deduplicate rows on, for endpoints that repeat entries across pages.</summary>
    public string? DistinctBy { get; set; }

    public ICollection<CustomSourceMapping> Mappings { get; set; } = [];
}

/// <summary>One source value → one field of a <c>Raw*Record</c>.</summary>
public class CustomSourceMapping
{
    public int Id { get; set; }

    public int CustomSourceRequestId { get; set; }
    public CustomSourceRequest CustomSourceRequest { get; set; } = null!;

    public int SortOrder { get; set; }
    public string SourceExpression { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public MappingKind Kind { get; set; }
}

/// <summary>
/// One execution of a source. Kept even when it produced no feed version, because a source that
/// silently stops returning rows is the failure mode worth seeing.
/// </summary>
public class CustomSourceRun
{
    public int Id { get; set; }

    public int CustomSourceId { get; set; }
    public CustomSource CustomSource { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public CustomSourceRunStatus Status { get; set; }
    public int RecordsProduced { get; set; }
    public string LogText { get; set; } = string.Empty;

    /// <summary>Null when the run failed, or produced content identical to the active version.</summary>
    public int? FeedVersionId { get; set; }
    public FeedVersion? FeedVersion { get; set; }
}
