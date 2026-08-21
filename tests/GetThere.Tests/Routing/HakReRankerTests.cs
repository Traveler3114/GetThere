using TransitInfoAPI.Entities;
using TransitInfoAPI.Routing.Otp;

namespace GetThere.Tests.Routing;

/// <summary>
/// HAK road closures cannot reach OTP, so they are applied after planning: an itinerary crossing a
/// closure is demoted but <b>kept and annotated</b>, because the user decides whether the detour is
/// worth it. These pin "kept, explained, demoted" — silently dropping the option is the failure mode.
/// </summary>
public class HakReRankerTests
{
    private static PlanPlaceDto At(double lat, double lon) => new(null, lat, lon);

    // Geometry is null so the ranker falls back to the straight line from→to.
    private static PlanLegDto Leg(string mode, bool transit, PlanPlaceDto from, PlanPlaceDto to) =>
        new(mode, default, default, 100, transit, from, to, null, null, null, null, false, null, []);

    private static PlanItineraryDto Itin(params PlanLegDto[] legs) => new(600, default, default, 0, legs);

    /// <summary>A closure sitting exactly on the disrupted leg's path (GeoJSON is lon,lat).</summary>
    private static Alert Closure(double lat, double lon) => new()
    {
        Kind = "Road",
        HeaderText = "Radovi na cesti",
        GeometryGeoJson = $"{{\"type\":\"Point\",\"coordinates\":[{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}]}}"
    };

    [Fact]
    public async Task DemotesTheDisruptedItineraryButStillReturnsIt()
    {
        var through = Itin(Leg("BUS", true, At(45.8100, 15.9800), At(45.8101, 15.9801)));
        var clear = Itin(Leg("BUS", true, At(45.9000, 16.1000), At(45.9001, 16.1001)));

        var result = await HakReRanker.AnnotateAndReRankAsync([through, clear], [Closure(45.8100, 15.9800)]);

        Assert.Equal(2, result.Count);                       // nothing dropped
        Assert.Empty(result[0].Legs[0].Alerts);              // clean option first
        Assert.NotEmpty(result[1].Legs[0].Alerts);           // disrupted one kept, last
        Assert.Equal("Radovi na cesti", result[1].Legs[0].Alerts[0].Header);
    }

    [Fact]
    public async Task LeavesRailUntouchedByARoadClosure()
    {
        // A tram on its own track is not affected by a road being dug up.
        var rail = Itin(Leg("TRAM", true, At(45.8100, 15.9800), At(45.8101, 15.9801)));

        var result = await HakReRanker.AnnotateAndReRankAsync([rail], [Closure(45.8100, 15.9800)]);

        Assert.Empty(result[0].Legs[0].Alerts);
    }

    [Fact]
    public async Task KeepsOtpOrderingWhenNoClosuresAreActive()
    {
        var first = Itin(Leg("BUS", true, At(45.81, 15.98), At(45.82, 15.99)));
        var second = Itin(Leg("BUS", true, At(45.83, 15.97), At(45.84, 15.96)));

        var result = await HakReRanker.AnnotateAndReRankAsync([first, second], []);

        Assert.Same(first, result[0]);
        Assert.Same(second, result[1]);
    }
}
