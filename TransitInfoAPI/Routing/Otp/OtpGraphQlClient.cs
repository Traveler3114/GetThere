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
    private const string PlanQuery = """
        query Plan($fromLat: Float!, $fromLon: Float!, $toLat: Float!, $toLon: Float!, $date: String, $time: String, $num: Int) {
          plan(
            from: { lat: $fromLat, lon: $fromLon },
            to: { lat: $toLat, lon: $toLon },
            date: $date, time: $time, numItineraries: $num,
            transportModes: [{ mode: WALK }, { mode: TRANSIT }, { mode: BICYCLE, qualifier: RENT }]
          ) {
            itineraries {
              duration startTime endTime walkDistance
              legs {
                mode transitLeg realTime distance startTime endTime
                from { name lat lon }
                to { name lat lon }
                route { shortName longName agency { gtfsId name } }
                trip { gtfsId }
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
        };

        var body = new JsonObject { ["query"] = PlanQuery, ["variables"] = variables };

        var client = httpClientFactory.CreateClient("otp");
        using var response = await client.PostAsJsonAsync(endpoint, body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new OtpPlanException($"OTP responded {(int)response.StatusCode}: {Truncate(json)}");

        return OtpResponseParser.Parse(json);
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
