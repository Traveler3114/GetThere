using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;
using GetThereAPI.Sdk;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GetThere.Tests.Journeys;

/// <summary>
/// Pricing is per operator: a journey spanning several operators yields several tickets + a total, and
/// the orchestrator has no operator knowledge — every fare rule lives in an adapter or the catalogue.
/// </summary>
public class JourneyQuoteManagerTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"quote-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static TicketOption Opt(int id, string name, decimal price, int? minutes) => new()
    {
        Id = id, Name = name, Price = price, Currency = "EUR", DurationMinutes = minutes,
        TicketFormat = TicketFormat.QR, IsActive = true,
    };

    private static QuoteLegDto Leg(string? op, string mode, bool transit, int startMin, int endMin) =>
        new(op, mode, transit,
            new DateTime(2026, 8, 18, 8, 0, 0).AddMinutes(startMin),
            new DateTime(2026, 8, 18, 8, 0, 0).AddMinutes(endMin),
            0, 0, 0, 0);

    private static async Task SeedAsync(AppDbContext db)
    {
        db.TicketingAdapters.AddRange(
            new TicketingAdapter { Id = 1, TransitInfoGlobalId = "gt-zet", Name = "ZET", AdapterType = "mock.zet", IsActive = true,
                TicketOptions = [Opt(11, "30 min", 0.55m, 30), Opt(12, "60 min", 0.95m, 60), Opt(13, "90 min", 1.35m, 90)] },
            new TicketingAdapter { Id = 2, TransitInfoGlobalId = "gt-hzpp", Name = "HŽPP", AdapterType = "mock.hzpp", IsActive = true,
                TicketOptions = [Opt(21, "Single", 3.00m, null)] });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Multi_operator_journey_yields_one_offer_per_operator_plus_a_total()
    {
        using var db = NewContext();
        await SeedAsync(db);
        var mgr = new JourneyQuoteManager(db, new AdapterRegistry(), NullLogger<JourneyQuoteManager>.Instance);

        var request = new JourneyQuoteRequest([
            Leg(null, "WALK", false, 0, 4),        // walk — no operator, no offer
            Leg("gt-zet", "TRAM", true, 4, 25),    // ~21 min ZET → cheapest covering = 30 min €0.55
            Leg("gt-hzpp", "RAIL", true, 30, 70),  // HŽPP → single €3.00
        ]);

        var result = await mgr.QuoteAsync(request);

        Assert.Equal(2, result.Offers.Count);
        Assert.Equal(3.55m, result.Total);

        var zet = result.Offers.Single(o => o.OperatorGlobalId == "gt-zet");
        Assert.Equal("30 min", zet.ProductName);
        Assert.Equal(0.55m, zet.Price);
        Assert.Equal(FulfillmentModes.BuyOnBoard, zet.FulfillmentMode); // no code adapter registered

        var hzpp = result.Offers.Single(o => o.OperatorGlobalId == "gt-hzpp");
        Assert.Equal(3.00m, hzpp.Price);
    }

    [Fact]
    public async Task A_longer_ZET_trip_picks_the_product_that_covers_it()
    {
        using var db = NewContext();
        await SeedAsync(db);
        var mgr = new JourneyQuoteManager(db, new AdapterRegistry(), NullLogger<JourneyQuoteManager>.Instance);

        // 70-minute ZET trip → the 30/60 min products don't cover it; the 90 min (€1.35) is cheapest that does.
        var request = new JourneyQuoteRequest([Leg("gt-zet", "TRAM", true, 0, 70)]);

        var offer = Assert.Single((await mgr.QuoteAsync(request)).Offers);
        Assert.Equal("90 min", offer.ProductName);
        Assert.Equal(1.35m, offer.Price);
    }

    [Fact]
    public async Task An_operator_without_a_configured_adapter_is_flagged_buy_on_board_unpriced()
    {
        using var db = NewContext();
        await SeedAsync(db);
        var mgr = new JourneyQuoteManager(db, new AdapterRegistry(), NullLogger<JourneyQuoteManager>.Instance);

        var request = new JourneyQuoteRequest([Leg("gt-unknown", "BUS", true, 0, 20)]);

        var offer = Assert.Single((await mgr.QuoteAsync(request)).Offers);
        Assert.Null(offer.Price);
        Assert.Equal(FulfillmentModes.BuyOnBoard, offer.FulfillmentMode);
        Assert.NotNull(offer.Note);
    }
}
