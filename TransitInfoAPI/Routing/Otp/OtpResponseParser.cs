using System.Text.Json;

namespace TransitInfoAPI.Routing.Otp;

/// <summary>
/// Maps an OTP GraphQL <c>plan</c> response to itinerary DTOs. Pure and tolerant of missing optional
/// fields, so it is unit-testable against recorded OTP JSON without standing up an OTP server.
/// </summary>
public static class OtpResponseParser
{
    public static IReadOnlyList<PlanItineraryDto> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // GraphQL surfaces failures as a top-level "errors" array — surface them rather than returning
        // an empty plan, which would be indistinguishable from "no route found".
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var messages = errors.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                .Where(m => !string.IsNullOrEmpty(m));
            throw new OtpPlanException($"OTP returned errors: {string.Join("; ", messages)}");
        }

        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("plan", out var plan)
            || plan.ValueKind != JsonValueKind.Object
            || !plan.TryGetProperty("itineraries", out var itineraries)
            || itineraries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<PlanItineraryDto>();
        foreach (var it in itineraries.EnumerateArray())
        {
            var legs = new List<PlanLegDto>();
            if (it.TryGetProperty("legs", out var legsEl) && legsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var leg in legsEl.EnumerateArray())
                    legs.Add(ParseLeg(leg));
            }

            results.Add(new PlanItineraryDto(
                DurationSeconds: (int)GetDouble(it, "duration"),
                StartTime: GetTime(it, "startTime"),
                EndTime: GetTime(it, "endTime"),
                WalkDistanceMeters: GetDouble(it, "walkDistance"),
                Legs: legs));
        }
        return results;
    }

    private static PlanLegDto ParseLeg(JsonElement leg)
    {
        string? routeShort = null, routeLong = null, operatorGlobalId = null, tripGtfsId = null;
        if (leg.TryGetProperty("route", out var route) && route.ValueKind == JsonValueKind.Object)
        {
            routeShort = GetString(route, "shortName");
            routeLong = GetString(route, "longName");
            if (route.TryGetProperty("agency", out var agency) && agency.ValueKind == JsonValueKind.Object)
                operatorGlobalId = StripFeedScope(GetString(agency, "gtfsId"));
        }
        if (leg.TryGetProperty("trip", out var trip) && trip.ValueKind == JsonValueKind.Object)
            tripGtfsId = GetString(trip, "gtfsId");

        var mode = GetString(leg, "mode") ?? "UNKNOWN";
        var isTransit = leg.TryGetProperty("transitLeg", out var tl) && tl.ValueKind == JsonValueKind.True
            || !string.IsNullOrEmpty(operatorGlobalId);

        return new PlanLegDto(
            Mode: mode,
            StartTime: GetTime(leg, "startTime"),
            EndTime: GetTime(leg, "endTime"),
            DistanceMeters: GetDouble(leg, "distance"),
            IsTransit: isTransit,
            From: ParsePlace(leg, "from"),
            To: ParsePlace(leg, "to"),
            RouteShortName: routeShort,
            RouteLongName: routeLong,
            OperatorGlobalId: operatorGlobalId,
            TripGtfsId: tripGtfsId,
            RealtimeState: leg.TryGetProperty("realTime", out var rt) && rt.ValueKind == JsonValueKind.True);
    }

    private static PlanPlaceDto ParsePlace(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var place) || place.ValueKind != JsonValueKind.Object)
            return new PlanPlaceDto(null, 0, 0);
        return new PlanPlaceDto(GetString(place, "name"), GetDouble(place, "lat"), GetDouble(place, "lon"));
    }

    // OTP prefixes every gtfsId with its own feed scope (e.g. agency "gt-zet" is served as
    // "1:gt-zet"). The operator id that GetThereAPI ticketing joins on is the un-prefixed value.
    private static string? StripFeedScope(string? gtfsId)
    {
        if (string.IsNullOrEmpty(gtfsId))
            return gtfsId;
        var colon = gtfsId.IndexOf(':');
        return colon >= 0 ? gtfsId[(colon + 1)..] : gtfsId;
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double GetDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    // OTP epoch-millis timestamps.
    private static DateTimeOffset GetTime(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64())
            : DateTimeOffset.MinValue;
}
