using GetThereShared.Dtos;
using System.Diagnostics;
using System.Xml.Linq;

namespace GetThere.Services.Realtime;

/// <summary>
/// Parses SIRI (Service Interface for Real Time Information) XML feeds.
/// Used by UK, Scandinavia, and some other European operators.
/// SIRI VM (Vehicle Monitoring) is the relevant service.
/// </summary>
public class SiriParser : IRealtimeParser
{
    public Task<List<VehiclePositionDto>> ParseAsync(
        byte[] data,
        TransitOperatorDto op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<VehiclePositionDto>();

        try
        {
            var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(data));
            XNamespace siri = "http://www.siri.org.uk/siri";

            // SIRI VM response path:
            // Siri/ServiceDelivery/VehicleMonitoringDelivery/VehicleActivity/MonitoredVehicleJourney
            var journeys = xml.Descendants(siri + "MonitoredVehicleJourney");

            foreach (var j in journeys)
            {
                var dto = new VehiclePositionDto();

                // Location
                var loc = j.Element(siri + "VehicleLocation");
                if (loc == null) continue;
                dto.Lat = ParseDouble(loc.Element(siri + "Latitude")?.Value);
                dto.Lon = ParseDouble(loc.Element(siri + "Longitude")?.Value);
                if (dto.Lat == 0 && dto.Lon == 0) continue;

                dto.Bearing = (float)ParseDouble(j.Element(siri + "Bearing")?.Value);

                // Route / line
                dto.RouteId = j.Element(siri + "LineRef")?.Value
                           ?? j.Element(siri + "PublishedLineName")?.Value;

                // Vehicle
                var vehicleRef = j.Element(siri + "VehicleRef")?.Value;
                dto.VehicleId = vehicleRef ?? Guid.NewGuid().ToString();
                dto.Label     = j.Element(siri + "PublishedLineName")?.Value;

                result.Add(dto);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SIRI:{op.Name}] Parse error: {ex.Message}");
        }

        Trace.WriteLine($"[SIRI:{op.Name}] {result.Count} vehicles");
        return Task.FromResult(result);
    }

    private static double ParseDouble(string? s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
}
