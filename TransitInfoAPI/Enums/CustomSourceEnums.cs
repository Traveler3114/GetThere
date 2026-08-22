namespace TransitInfoAPI.Enums;

/// <summary>Where a custom source's data physically comes from.</summary>
public enum CustomSourceKind
{
    /// <summary>An HTTP endpoint returning JSON, XML or CSV.</summary>
    Http,

    /// <summary>An HTML page whose timetable is in the markup.</summary>
    Html,

    /// <summary>A PDF timetable.</summary>
    Pdf,

    /// <summary>A spreadsheet or CSV an admin uploads by hand — no polling.</summary>
    Upload
}

/// <summary>How to interpret a response body.</summary>
public enum CustomSourceFormat
{
    Json,
    Xml,
    Csv,
    Html,
    Pdf,
    Xlsx
}

/// <summary>
/// Which part of a <see cref="Core.TransitDocument"/> a request fills.
/// <para>
/// One request per section is the structural fix over the old <c>CustomFeedTableConfig</c>: it maps
/// straight onto the document's optional sections, so "this operator only publishes stops" is a
/// configuration rather than a validation failure.
/// </para>
/// </summary>
public enum TransitSection
{
    Agencies,
    Stops,
    Routes,
    Trips,
    StopTimes,
    Calendar,
    CalendarDates,
    MobilityStations,

    /// <summary>Live vehicle positions. Ephemeral — never becomes a FeedVersion.</summary>
    Vehicles,

    /// <summary>Per-stop realtime delays. Ephemeral — never becomes a FeedVersion.</summary>
    TripUpdates
}

/// <summary>How a single source value becomes a target field.</summary>
public enum MappingKind
{
    /// <summary>Copy the value at <c>SourceExpression</c> verbatim.</summary>
    Direct,

    /// <summary>Ignore the source; use <c>SourceExpression</c> as a literal.</summary>
    Static,

    /// <summary>A small expression over the row — concatenation and the like.</summary>
    Expression,

    /// <summary>Pull a GTFS-style HH:MM:SS out of a larger value.</summary>
    TimeExtract,

    /// <summary>Pull a yyyyMMdd date out of a larger value.</summary>
    DateExtract
}

/// <summary>How a request asks for the next page.</summary>
public enum PaginationKind
{
    None,
    Page,
    Offset,
    Cursor
}

public enum CustomSourceRunStatus
{
    Running,
    Success,
    Failed
}
