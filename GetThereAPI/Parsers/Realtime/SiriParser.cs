using GetThereAPI.Entities;
using System.Diagnostics;
using System.Xml.Linq;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Parses SIRI (Service Interface for Real Time Information) XML feeds.
/// Used by UK, Scandinavia, and some other European operators.
/// SIRI VM (Vehicle Monitoring) is the relevant service.
/// </summary>
public class SiriParser : IRealtimeParser
{
    public Task<List<ParsedVehicle>> ParseAsync(
        byte[] data,
        TransitOperator op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<ParsedVehicle>();

        try
        {
            var xml  = XDocument.Parse(System.Text.Encoding.UTF8.GetString(data));
            XNamespace siri = "http://www.siri.org.uk/siri";

            // SIRI VM response path:
            // Siri/ServiceDelivery/VehicleMonitoringDelivery/VehicleActivity/MonitoredVehicleJourney
            var journeys = xml.Descendants(siri + "MonitoredVehicleJourney");

            foreach (var j in journeys)
            {
                var loc = j.Element(siri + "VehicleLocation");
                if (loc is null) continue;

                var lat = ParseDouble(loc.Element(siri + "Latitude")?.Value);
                var lon = ParseDouble(loc.Element(siri + "Longitude")?.Value);
                if (lat == 0 && lon == 0) continue;

                var vehicleRef = j.Element(siri + "VehicleRef")?.Value;
                var routeId    = j.Element(siri + "LineRef")?.Value
                              ?? j.Element(siri + "PublishedLineName")?.Value;

                result.Add(new ParsedVehicle
                {
                    VehicleId = vehicleRef ?? Guid.NewGuid().ToString(),
                    RouteId   = routeId,
                    Label     = j.Element(siri + "PublishedLineName")?.Value,
                    Lat       = lat,
                    Lon       = lon,
                    Bearing   = (float)ParseDouble(j.Element(siri + "Bearing")?.Value),
                });
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
        double.TryParse(s,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) ? d : 0;
}
