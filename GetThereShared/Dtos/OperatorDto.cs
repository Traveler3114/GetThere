namespace GetThereShared.Dtos;

/// <summary>
/// Public-facing operator info sent to the app.
/// Auth keys and feed configs are intentionally excluded —
/// those stay on the server and are never sent to clients.
/// Returned by GET /operator
/// </summary>
public class OperatorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = string.Empty;
}

/// <summary>
/// Transport type config served to the app.
/// Drives icon loading and map layer expressions dynamically.
/// Returned by GET /operator/transport-types
/// </summary>
public class TransportTypeDto
{
    public int GtfsRouteType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconFile { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}