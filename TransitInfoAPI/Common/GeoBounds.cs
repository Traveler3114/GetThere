namespace TransitInfoAPI.Common;

/// <summary>
/// The one definition of a coordinate this service is willing to store.
/// </summary>
/// <remarks>
/// <para>
/// There were five copies of this check — <c>GtfsParser.ParseStops</c>,
/// <c>GtfsParser.ParseShapes</c>, both of <c>MobilityManager</c>'s upserts, and
/// <c>CustomHttpSource.ToStops</c> — each written as a chain of relational comparisons, each with
/// a comment saying it matched one of the others. All five shared the same hole, which is what
/// duplicated validation reliably produces.
/// </para>
/// <para>
/// The hole: <b>every comparison with NaN is false</b>, so <c>lat &lt; -90 || lat &gt; 90</c>
/// rejects nothing when <c>lat</c> is NaN, and <c>lat == 0 &amp;&amp; lon == 0</c> does not catch
/// it either. NaN is not hypothetical here — no source sends it as a number, because neither JSON
/// nor CSV has a way to write one, but every one of these paths reaches a text parse, and
/// <c>double.TryParse</c> accepts the strings <c>"NaN"</c> and <c>"Infinity"</c> against
/// <c>InvariantCulture</c>'s symbols. So a feed only has to contain the three characters
/// <c>NaN</c> in <c>stop_lat</c>.
/// </para>
/// <para>
/// What that cost: the value passed the guard whose entire purpose is to keep junk out of the
/// feed's geometry, then went to a SQL Server <c>float</c> column, which has no NaN to store it
/// in — so the import failed on a bulk-copy error naming neither the stop nor the reason — and
/// into the convex hull that defines the operator's service area on the map.
/// </para>
/// <para>
/// Named <c>GeoBounds</c> rather than the more obvious <c>Coordinates</c> because
/// <c>NetTopologySuite.Geometries</c> exports a type by that name and <c>GtfsParser</c> imports
/// that namespace, which would make every use here an ambiguous reference.
/// </para>
/// </remarks>
public static class GeoBounds
{
    /// <summary>
    /// Whether a latitude/longitude pair is finite, in range, and not Null Island.
    /// </summary>
    /// <remarks>
    /// <para>
    /// (0, 0) is rejected on purpose. It is a legal coordinate in the Gulf of Guinea and, in transit
    /// data, is always a missing value that defaulted to zero rather than a stop anyone can board
    /// at. Keeping it drags the feed's convex hull across the Atlantic.
    /// </para>
    /// <para>
    /// Negative zero compares equal to zero under IEEE 754, so a source that writes <c>-0.0</c> is
    /// caught by the same test.
    /// </para>
    /// </remarks>
    public static bool IsUsable(double latitude, double longitude) =>
        double.IsFinite(latitude)
        && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180
        && !(latitude == 0.0 && longitude == 0.0);
}
