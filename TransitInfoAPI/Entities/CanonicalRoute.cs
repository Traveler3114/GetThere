using TransitInfoAPI.Enums;

using NetTopologySuite.Geometries;

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

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
}

