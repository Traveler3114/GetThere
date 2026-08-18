using TransitInfoAPI.Routing.Otp;

namespace GetThere.Tests.Routing;

/// <summary>
/// The parser turns OTP's GraphQL response into itineraries whose transit legs carry the operator
/// GlobalId — the join a later ticketing hook depends on. Pinned against recorded OTP JSON so the
/// mapping (including epoch-millis times and the agency→operator id) is verifiable without a server.
/// </summary>
public class OtpResponseParserTests
{
    private const string WalkThenTram = """
    {
      "data": {
        "plan": {
          "itineraries": [
            {
              "duration": 1320,
              "startTime": 1755417600000,
              "endTime": 1755418920000,
              "walkDistance": 210.5,
              "legs": [
                {
                  "mode": "WALK", "transitLeg": false, "realTime": false, "distance": 210.5,
                  "startTime": 1755417600000, "endTime": 1755417780000,
                  "from": { "name": "Origin", "lat": 45.813, "lon": 15.977 },
                  "to": { "name": "Trg", "lat": 45.812, "lon": 15.979 },
                  "route": null, "trip": null
                },
                {
                  "mode": "TRAM", "transitLeg": true, "realTime": true, "distance": 3100.0,
                  "startTime": 1755417900000, "endTime": 1755418920000,
                  "legGeometry": { "points": "_p~iF~ps|U_ulLnnqC", "length": 3 },
                  "from": { "name": "Trg", "lat": 45.812, "lon": 15.979 },
                  "to": { "name": "Airport", "lat": 45.74, "lon": 16.07 },
                  "route": { "shortName": "1", "longName": "Line 1", "agency": { "gtfsId": "1:o-ZET", "name": "ZET" } },
                  "trip": { "gtfsId": "1:T1" }
                }
              ]
            }
          ]
        }
      }
    }
    """;

    [Fact]
    public void Parses_itinerary_legs_times_and_operator_id()
    {
        var itineraries = OtpResponseParser.Parse(WalkThenTram);

        var it = Assert.Single(itineraries);
        Assert.Equal(1320, it.DurationSeconds);
        Assert.Equal(210.5, it.WalkDistanceMeters);
        Assert.Equal(2, it.Legs.Count);

        var walk = it.Legs[0];
        Assert.Equal("WALK", walk.Mode);
        Assert.False(walk.IsTransit);
        Assert.Null(walk.OperatorGlobalId);

        var tram = it.Legs[1];
        Assert.True(tram.IsTransit);
        Assert.True(tram.RealtimeState);
        Assert.Equal("1", tram.RouteShortName);
        Assert.Equal("o-ZET", tram.OperatorGlobalId); // the leg carries the operator GlobalId
        Assert.Equal("Airport", tram.To.Name);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1755417900000), tram.StartTime);
        Assert.Equal("_p~iF~ps|U_ulLnnqC", tram.Geometry); // encoded polyline surfaced for map drawing
        Assert.Null(walk.Geometry); // absent when OTP omits it
    }

    [Fact]
    public void Surfaces_graphql_errors_rather_than_returning_empty()
    {
        const string errorJson = """{"errors":[{"message":"Variable 'fromLat' has an invalid value"}]}""";

        var ex = Assert.Throws<OtpPlanException>(() => OtpResponseParser.Parse(errorJson));
        Assert.Contains("invalid value", ex.Message);
    }

    [Fact]
    public void No_route_found_is_an_empty_list_not_an_error()
    {
        const string empty = """{"data":{"plan":{"itineraries":[]}}}""";

        Assert.Empty(OtpResponseParser.Parse(empty));
    }
}
