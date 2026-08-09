using NetTopologySuite.Geometries;

using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CanonicalRoute
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string LongName { get; set; } = string.Empty;
    public RouteType RouteType { get; set; }
    public string? Color { get; set; }
    public string? TextColor { get; set; }
    public bool IsActive { get; set; }

    public string? SupersedesIds { get; set; }
    public Geometry? Geometry { get; set; }
    public bool ShapeEdited { get; set; }

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;

    /// <summary>
    /// The feed version that last imported this route.
    /// <para>
    /// Canonical routes are otherwise reachable from a feed version only through their trips, which
    /// leaves a route from a timetable-less feed with no link to the import that created it — and
    /// so indistinguishable from one orphaned by an old import. The per-operator sweep in
    /// <c>FeedManager.FinalizeVersionPhaseAsync</c> deactivates routes with no trips, which without
    /// this switched off every route a Network-completeness feed had just imported.
    /// </para>
    /// </summary>
    public int? LastSeenFeedVersionId { get; set; }
}

