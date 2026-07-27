namespace GetThereAPI.Common;

/// <summary>
/// Geographic constants used when translating a radius into a lat/lon bounding box.
/// TransitInfoAPI carries its own copy — the two platforms are deliberately independent.
/// </summary>
public static class GeoConstants
{
    /// <summary>
    /// Approximate kilometres per degree of latitude. Accurate enough for bounding boxes, which are
    /// only a pre-filter; exact distances are computed upstream.
    /// </summary>
    public const double KmPerDegree = 111.0;
}
