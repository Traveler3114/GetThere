using System.Text.Json;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Routing.Otp;

public static class HakReRanker
{
    private static readonly HashSet<string> RoadModes = new(StringComparer.OrdinalIgnoreCase) { "WALK", "BICYCLE", "BIKE", "CAR", "BUS" };

    public static Task<IReadOnlyList<PlanItineraryDto>> AnnotateAndReRankAsync(
        IReadOnlyList<PlanItineraryDto> itineraries,
        IReadOnlyList<Alert> roadAlerts,
        CancellationToken ct = default)
    {
        if (itineraries.Count == 0 || roadAlerts.Count == 0)
            return Task.FromResult(itineraries);

        // Pre-parse road geometries
        var roadGeoms = roadAlerts.Select(a => new RoadGeom(a, ParseGeoJson(a.GeometryGeoJson))).Where(r => r.Geom != null).ToList();
        if (roadGeoms.Count == 0)
            return Task.FromResult(itineraries);

        var annotated = new List<(PlanItineraryDto Itin, int OriginalIndex, bool HasDisruption)>();
        for (var idx = 0; idx < itineraries.Count; idx++)
        {
            var itin = itineraries[idx];
            var legs = new List<PlanLegDto>();
            bool itinHasDisruption = false;

            foreach (var leg in itin.Legs)
            {
                // Rail/tram/ferry never penalised
                if (!IsRoadCapable(leg.Mode))
                {
                    legs.Add(leg);
                    continue;
                }

                var legCoords = DecodeGeometry(leg);
                if (legCoords.Count == 0)
                {
                    legs.Add(leg);
                    continue;
                }

                var legBbox = ComputeBbox(legCoords);
                PlanLegDto newLeg = leg;
                bool legDisrupted = false;

                foreach (var road in roadGeoms)
                {
                    // Bbox pre-filter (expand by ~0.001 deg ~ 111m)
                    if (!BboxesOverlap(legBbox, road.Bbox))
                        continue;

                    if (IsLegAffected(legCoords, road.Geom!, 50.0))
                    {
                        // Attach disruption note (same field as transit alerts)
                        var header = road.Alert.HeaderText ?? "Road closure";
                        var desc = road.Alert.DescriptionText ?? road.Alert.Severity ?? "Disruption on road";
                        var effect = "DETOUR";
                        var alertDto = new PlanAlertDto(header, desc, effect);
                        var alerts = newLeg.Alerts.ToList();
                        alerts.Add(alertDto);
                        newLeg = newLeg with { Alerts = alerts };
                        legDisrupted = true;
                    }
                }

                if (legDisrupted) itinHasDisruption = true;
                legs.Add(newLeg);
            }

            var newItin = new PlanItineraryDto(itin.DurationSeconds, itin.StartTime, itin.EndTime, itin.WalkDistanceMeters, legs);
            annotated.Add((newItin, idx, itinHasDisruption));
        }

        // Stable-sort: clean first, preserving original rank order within each group
        var sorted = annotated
            .OrderBy(x => x.HasDisruption)
            .ThenBy(x => x.OriginalIndex)
            .Select(x => x.Itin)
            .ToList();

        return Task.FromResult<IReadOnlyList<PlanItineraryDto>>(sorted);
    }

    private static bool IsRoadCapable(string mode) => RoadModes.Contains(mode);

    private sealed class RoadGeom
    {
        public Alert Alert { get; }
        public GeoJsonGeom? Geom { get; }
        public Bbox Bbox { get; }
        public RoadGeom(Alert alert, GeoJsonGeom? geom)
        {
            Alert = alert;
            Geom = geom;
            Bbox = geom is null ? new Bbox(0, 0, 0, 0) : ComputeBboxFromGeom(geom);
        }
    }

    private sealed class GeoJsonGeom
    {
        public string Type { get; set; } = "";
        public List<double[]> Points { get; set; } = []; // for Point = single, LineString = list, MultiLineString flattened? We'll handle.
        public List<List<double[]>> MultiLines { get; set; } = [];
    }

    private static GeoJsonGeom? ParseGeoJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? "";
            var geom = new GeoJsonGeom { Type = type };
            if (type.Equals("Point", StringComparison.OrdinalIgnoreCase))
            {
                var coords = root.GetProperty("coordinates");
                if (coords.ValueKind == JsonValueKind.Array && coords.GetArrayLength() >= 2)
                    geom.Points.Add([coords[0].GetDouble(), coords[1].GetDouble()]);
            }
            else if (type.Equals("LineString", StringComparison.OrdinalIgnoreCase))
            {
                var coords = root.GetProperty("coordinates");
                foreach (var pt in coords.EnumerateArray())
                    geom.Points.Add([pt[0].GetDouble(), pt[1].GetDouble()]);
            }
            else if (type.Equals("MultiLineString", StringComparison.OrdinalIgnoreCase))
            {
                var coords = root.GetProperty("coordinates");
                foreach (var line in coords.EnumerateArray())
                {
                    var list = new List<double[]>();
                    foreach (var pt in line.EnumerateArray())
                        list.Add([pt[0].GetDouble(), pt[1].GetDouble()]);
                    geom.MultiLines.Add(list);
                }
            }
            else if (type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
            {
                // Take outer ring as line
                var coords = root.GetProperty("coordinates");
                if (coords.GetArrayLength() > 0)
                {
                    var outer = coords[0];
                    foreach (var pt in outer.EnumerateArray())
                        geom.Points.Add([pt[0].GetDouble(), pt[1].GetDouble()]);
                }
            }
            return geom;
        }
        catch
        {
            return null;
        }
    }

    private record Bbox(double MinLat, double MinLon, double MaxLat, double MaxLon);

    private static Bbox ComputeBbox(List<(double Lat, double Lon)> coords)
    {
        double minLat = coords.Min(c => c.Lat), maxLat = coords.Max(c => c.Lat);
        double minLon = coords.Min(c => c.Lon), maxLon = coords.Max(c => c.Lon);
        return new Bbox(minLat, minLon, maxLat, maxLon);
    }

    private static Bbox ComputeBboxFromGeom(GeoJsonGeom geom)
    {
        var pts = new List<(double Lat, double Lon)>();
        if (geom.Points.Count > 0)
            foreach (var p in geom.Points) pts.Add((p[1], p[0]));
        foreach (var line in geom.MultiLines)
            foreach (var p in line) pts.Add((p[1], p[0]));
        if (pts.Count == 0) return new Bbox(0, 0, 0, 0);
        return ComputeBbox(pts);
    }

    private static bool BboxesOverlap(Bbox a, Bbox b)
    {
        const double pad = 0.001; // ~111m
        return !(a.MaxLat + pad < b.MinLat || b.MaxLat + pad < a.MinLat || a.MaxLon + pad < b.MinLon || b.MaxLon + pad < a.MinLon);
    }

    private static List<(double Lat, double Lon)> DecodeGeometry(PlanLegDto leg)
    {
        if (!string.IsNullOrEmpty(leg.Geometry))
        {
            try { return DecodePolyline(leg.Geometry); }
            catch { }
        }
        // fallback to straight line from→to
        return [(leg.From.Lat, leg.From.Lon), (leg.To.Lat, leg.To.Lon)];
    }

    private static List<(double Lat, double Lon)> DecodePolyline(string encoded)
    {
        var coords = new List<(double Lat, double Lon)>();
        int index = 0, lat = 0, lng = 0;
        const double factor = 1e5;
        while (index < encoded.Length)
        {
            int result = 0, shift = 0, b;
            do { b = encoded[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            lat += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
            result = 0; shift = 0;
            do { b = encoded[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            lng += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
            coords.Add((lat / factor, lng / factor));
        }
        return coords;
    }

    private static bool IsLegAffected(List<(double Lat, double Lon)> legCoords, GeoJsonGeom geom, double bufferMeters)
    {
        // If geom is Point – check distance from any leg point/segment
        if (geom.Type.Equals("Point", StringComparison.OrdinalIgnoreCase) && geom.Points.Count > 0)
        {
            var pt = geom.Points[0];
            double gLat = pt[1], gLon = pt[0];
            // Check each segment of leg
            for (int i = 0; i < legCoords.Count - 1; i++)
            {
                var a = legCoords[i]; var b = legCoords[i + 1];
                double d = PointToSegmentDistance(gLat, gLon, a.Lat, a.Lon, b.Lat, b.Lon);
                if (d <= bufferMeters) return true;
            }
            // also check last point alone
            if (legCoords.Count == 1)
            {
                if (Haversine(legCoords[0].Lat, legCoords[0].Lon, gLat, gLon) <= bufferMeters) return true;
            }
            return false;
        }

        // LineString / Multi / Polygon as lines: check segment-to-segment
        var roadSegments = new List<(double Lat, double Lon)[]>();
        if (geom.Points.Count > 1)
            roadSegments.Add(geom.Points.Select(p => (p[1], p[0])).ToArray());
        foreach (var line in geom.MultiLines)
            roadSegments.Add(line.Select(p => (p[1], p[0])).ToArray());

        foreach (var roadLine in roadSegments)
        {
            for (int i = 0; i < legCoords.Count - 1; i++)
            {
                var legA = legCoords[i]; var legB = legCoords[i + 1];
                for (int j = 0; j < roadLine.Length - 1; j++)
                {
                    var roadA = roadLine[j]; var roadB = roadLine[j + 1];
                    double d = SegmentToSegmentDistance(legA.Lat, legA.Lon, legB.Lat, legB.Lon, roadA.Lat, roadA.Lon, roadB.Lat, roadB.Lon);
                    if (d <= bufferMeters) return true;
                }
                // road as single point segment? already handled but check point distance also
            }
            // If leg is single point and road is line: check point distance
            if (legCoords.Count == 1 && roadLine.Length >= 2)
            {
                var p = legCoords[0];
                for (int j = 0; j < roadLine.Length - 1; j++)
                {
                    var roadA = roadLine[j]; var roadB = roadLine[j + 1];
                    double d = PointToSegmentDistance(p.Lat, p.Lon, roadA.Lat, roadA.Lon, roadB.Lat, roadB.Lon);
                    if (d <= bufferMeters) return true;
                }
            }
        }
        // Also check case where road is Point but leg is line – already handled
        return false;
    }

    private static double SegmentToSegmentDistance(double latA1, double lonA1, double latA2, double lonA2,
                                                   double latB1, double lonB1, double latB2, double lonB2)
    {
        // Minimum of 4 point-to-segment distances
        double d1 = PointToSegmentDistance(latA1, lonA1, latB1, lonB1, latB2, lonB2);
        double d2 = PointToSegmentDistance(latA2, lonA2, latB1, lonB1, latB2, lonB2);
        double d3 = PointToSegmentDistance(latB1, lonB1, latA1, lonA1, latA2, lonA2);
        double d4 = PointToSegmentDistance(latB2, lonB2, latA1, lonA1, latA2, lonA2);
        return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
    }

    private static double PointToSegmentDistance(double latP, double lonP, double latA, double lonA, double latB, double lonB)
    {
        double avgLat = (latP + latA + latB) / 3.0;
        double cosLat = Math.Cos(avgLat * Math.PI / 180.0);
        // Scale lon by cos
        double xP = lonP * cosLat, yP = latP;
        double xA = lonA * cosLat, yA = latA;
        double xB = lonB * cosLat, yB = latB;
        double dx = xB - xA, dy = yB - yA;
        double len2 = dx * dx + dy * dy;
        if (len2 == 0)
            return Haversine(latP, lonP, latA, lonA);
        double t = ((xP - xA) * dx + (yP - yA) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        double projX = xA + t * dx, projY = yA + t * dy;
        double projLon = projX / cosLat, projLat = projY;
        return Haversine(latP, lonP, projLat, projLon);
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
}
