using NetTopologySuite.Geometries;

namespace TransitInfoAPI.Entities;

public class Shape
{
    public int Id { get; set; }
    public int FeedVersionId { get; set; }
    public FeedVersion FeedVersion { get; set; } = null!;

    public string ShapeId { get; set; } = string.Empty;
    public LineString Geometry { get; set; } = null!;
    public bool IsManuallyEdited { get; set; }
}
