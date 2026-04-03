using System.Text.Json;

namespace GetThereShared.Dtos;

/// <summary>
/// Unified map feature envelope sent to the client.
/// The client reads <see cref="Type"/> to determine how to render the marker,
/// then deserialises <see cref="Data"/> into the appropriate model.
///
/// Known types: "Stop" | "Vehicle" | "BikeStation"
/// Future:      "Scooter" | "Ferry" | "FlightGate" etc.
/// </summary>
public class MapFeatureDto
{
    /// <summary>Discriminator — tells the client which shape <see cref="Data"/> holds.</summary>
    public string Type { get; set; } = "";

    /// <summary>WGS-84 latitude (top-level for fast bounding-box queries on the client).</summary>
    public double Lat { get; set; }

    /// <summary>WGS-84 longitude.</summary>
    public double Lon { get; set; }

    /// <summary>
    /// The full typed payload, serialised to JSON at the API boundary.
    /// Cast to the correct DTO based on <see cref="Type"/>.
    /// </summary>
    public JsonElement Data { get; set; }
}
