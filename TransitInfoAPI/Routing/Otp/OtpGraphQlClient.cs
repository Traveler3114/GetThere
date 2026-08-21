using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

namespace TransitInfoAPI.Routing.Otp;

/// <summary>
/// Thin GraphQL client for OTP2's <c>plan</c> query. Owns only the transport and the query text; the
/// response shaping lives in <see cref="OtpResponseParser"/> so it stays testable without a server.
/// OTP is not internet-exposed — this client is its only caller from inside TransitInfoAPI.
/// </summary>
public sealed class OtpGraphQlClient(
    IHttpClientFactory httpClientFactory,
    IOptions<RoutingOptions> options)
{
    // OTP Mode names we let a caller allow-list for transit. Guarded so a bad value can't be injected
    // into the GraphQL enum (which would fail the whole query); anything unknown is simply dropped.
    private static readonly HashSet<string> AllowedTransitModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRAM", "SUBWAY", "RAIL", "BUS", "FERRY", "CABLE_CAR", "GONDOLA", "FUNICULAR", "TROLLEYBUS",
        "MONORAIL", "AIRPLANE", "COACH",
    };

    private const string PlanQuery = """
        query Plan($fromLat: Float!, $fromLon: Float!, $toLat: Float!, $toLon: Float!, $date: String, $time: String, $num: Int, $modes: [TransportMode!], $arriveBy: Boolean, $walkReluctance: Float, $transferPenalty: Int, $wheelchair: Boolean) {
          plan(
            from: { lat: $fromLat, lon: $fromLon },
            to: { lat: $toLat, lon: $toLon },
            date: $date, time: $time, numItineraries: $num,
            transportModes: $modes,
            arriveBy: $arriveBy,
            walkReluctance: $walkReluctance,
            transferPenalty: $transferPenalty,
            wheelchair: $wheelchair
          ) {
            itineraries {
              duration startTime endTime walkDistance
              legs {
                mode transitLeg realTime distance startTime endTime
                legGeometry { points length }
                from { name lat lon }
                to { name lat lon }
                route { shortName longName agency { gtfsId name } }
                trip { gtfsId }
                alerts { alertHeaderText alertDescriptionText alertEffect }
              }
            }
          }
        }
        """;

    public async Task<IReadOnlyList<PlanItineraryDto>> PlanAsync(PlanRequest request, CancellationToken ct = default)
    {
        var endpoint = options.Value.Otp.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new OtpPlanException("No OTP endpoint is configured (Routing:Otp:Endpoint).");

        var departAt = request.DepartAt ?? DateTimeOffset.UtcNow;
        var variables = new JsonObject
        {
            ["fromLat"] = request.FromLat,
            ["fromLon"] = request.FromLon,
            ["toLat"] = request.ToLat,
            ["toLon"] = request.ToLon,
            ["date"] = departAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = departAt.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["num"] = Math.Clamp(request.NumItineraries, 1, 10),
            ["modes"] = BuildTransportModes(request),
            ["arriveBy"] = request.ArriveBy,
            // Null variables are treated as "unset" by OTP, which then applies its own defaults.
            ["walkReluctance"] = request.WalkReluctance,
            ["transferPenalty"] = request.TransferPenalty,
            ["wheelchair"] = request.Wheelchair,
        };

        var body = new JsonObject { ["query"] = PlanQuery, ["variables"] = variables };

        var client = httpClientFactory.CreateClient("otp");
        using var response = await client.PostAsJsonAsync(endpoint, body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new OtpPlanException($"OTP responded {(int)response.StatusCode}: {Truncate(json)}");

        return OtpResponseParser.Parse(json);
    }

    /// <summary>
    /// Builds OTP's <c>transportModes</c> list. Always includes WALK; expands the caller's transit
    /// allow-list (or all TRANSIT when none is given); appends bike rental unless turned off. Passing
    /// an explicit subset is how a filter excludes a mode — OTP only considers the modes listed here.
    /// </summary>
    private static JsonArray BuildTransportModes(PlanRequest request)
    {
        var modes = new JsonArray { new JsonObject { ["mode"] = "WALK" } };

        if (request.TransitModes is { Count: > 0 })
        {
            foreach (var mode in request.TransitModes)
            {
                if (AllowedTransitModes.Contains(mode))
                    modes.Add(new JsonObject { ["mode"] = mode.ToUpperInvariant() });
            }
        }
        else
        {
            modes.Add(new JsonObject { ["mode"] = "TRANSIT" });
        }

        if (request.IncludeBikeRental)
            modes.Add(new JsonObject { ["mode"] = "BICYCLE", ["qualifier"] = "RENT" });

        return modes;
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
