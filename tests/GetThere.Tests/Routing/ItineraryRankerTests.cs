using TransitInfoAPI.Routing.Otp;

namespace GetThere.Tests.Routing;

/// <summary>
/// The preset filters and the ranker are the "custom preferences" layer around OTP: presets resolve to
/// a structured preference (request-shaping + ranking), and the ranker re-orders OTP's Pareto set by
/// the chosen objective. Pinned so "no trains" really excludes rail and "greener" really prefers the
/// lower-emission itinerary.
/// </summary>
public class ItineraryRankerTests
{
    private static PlanPlaceDto P => new(null, 0, 0);
    private static PlanLegDto Leg(string mode, bool transit, double meters) =>
        new(mode, default, default, meters, transit, P, P, null, null, null, null, false, null, []);
    private static PlanItineraryDto Itin(int durationSec, double walkMeters, params PlanLegDto[] legs) =>
        new(durationSec, default, default, walkMeters, legs);

    [Fact]
    public void Fastest_orders_by_duration()
    {
        var set = new[] { Itin(300, 0), Itin(100, 0), Itin(200, 0) };

        var ranked = ItineraryRanker.Rank(set, RankBy.Fastest);

        Assert.Equal(new[] { 100, 200, 300 }, ranked.Select(i => i.DurationSeconds));
    }

    [Fact]
    public void FewestTransfers_prefers_the_itinerary_with_fewer_transit_legs()
    {
        var oneTransfer = Itin(600, 100, Leg("WALK", false, 100), Leg("TRAM", true, 2000), Leg("BUS", true, 2000));
        var direct = Itin(700, 100, Leg("WALK", false, 100), Leg("TRAM", true, 4000));

        var ranked = ItineraryRanker.Rank(new[] { oneTransfer, direct }, RankBy.FewestTransfers);

        Assert.Same(direct, ranked[0]); // 0 transfers beats 1, even though it's slower
        Assert.Equal(0, ItineraryRanker.TransferCount(direct));
        Assert.Equal(1, ItineraryRanker.TransferCount(oneTransfer));
    }

    [Fact]
    public void Greener_prefers_the_lower_emission_itinerary()
    {
        var byBus = Itin(500, 50, Leg("WALK", false, 50), Leg("BUS", true, 5000));
        var byTram = Itin(500, 50, Leg("WALK", false, 50), Leg("TRAM", true, 5000));

        var ranked = ItineraryRanker.Rank(new[] { byBus, byTram }, RankBy.Greener);

        Assert.Same(byTram, ranked[0]); // tram emits less per metre than bus
        Assert.True(ItineraryRanker.EstimatedEmissionGrams(byTram) < ItineraryRanker.EstimatedEmissionGrams(byBus));
    }

    [Fact]
    public void A_single_itinerary_is_returned_unchanged()
    {
        var one = new[] { Itin(300, 0) };
        Assert.Same(one, ItineraryRanker.Rank(one, RankBy.Balanced));
    }

    [Theory]
    [InlineData("greener", RankBy.Greener)]
    [InlineData("Fewest-Transfers", RankBy.FewestTransfers)]
    [InlineData("balanced", RankBy.Balanced)]
    [InlineData(null, RankBy.Fastest)]
    [InlineData("", RankBy.Fastest)]
    public void Presets_resolve_and_normalise(string? preset, RankBy expected)
    {
        Assert.Equal(expected, RoutingPresets.Resolve(preset)!.RankBy);
    }

    [Fact]
    public void No_trains_excludes_rail_from_the_transit_allow_list()
    {
        var pref = RoutingPresets.Resolve("no_trains");
        var allowed = RoutingPresets.AllowedTransitModes(pref);

        Assert.NotNull(allowed);
        Assert.DoesNotContain("RAIL", allowed!);
        Assert.DoesNotContain("SUBWAY", allowed!);
        Assert.Contains("TRAM", allowed!);
        Assert.Contains("BUS", allowed!);
    }

    [Fact]
    public void An_unfiltered_preference_allows_all_transit()
    {
        Assert.Null(RoutingPresets.AllowedTransitModes(new RoutingPreference()));
        Assert.Null(RoutingPresets.Resolve("totally-unknown-preset"));
    }
}
