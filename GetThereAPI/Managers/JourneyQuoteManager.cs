using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Sdk;

using GetThereShared.Contracts;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Prices a routed itinerary per operator. A journey can span several operators, each with its own
/// fare model and its own ticket, so the result is a breakdown (one offer per operator segment) plus
/// a combined total — never a single fare. This is deliberately operator-agnostic: it splits the legs
/// by operator, resolves each operator's ticketing adapter (by <c>TransitInfoGlobalId</c>), and lets
/// that adapter price its own segment. Every operator-specific rule lives inside its adapter, never
/// here.
/// </summary>
public class JourneyQuoteManager(AppDbContext db, AdapterRegistry registry, ILogger<JourneyQuoteManager> logger)
{
    private const string DefaultCurrency = "EUR";

    public async Task<JourneyQuoteResponse> QuoteAsync(JourneyQuoteRequest request, CancellationToken ct = default)
    {
        // Group the transit legs by operator, keeping first-seen order.
        var groups = request.Legs
            .Where(l => l.IsTransit && !string.IsNullOrWhiteSpace(l.OperatorGlobalId))
            .GroupBy(l => l.OperatorGlobalId!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var offers = new List<OperatorOfferDto>();
        var total = 0m;
        var currency = DefaultCurrency;

        foreach (var group in groups)
        {
            var globalId = group.Key;
            var legs = group.ToList();

            var adapter = await db.TicketingAdapters
                .Include(a => a.TicketOptions)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.TransitInfoGlobalId == globalId && a.IsActive, ct);

            if (adapter is null)
            {
                offers.Add(new OperatorOfferDto(globalId, globalId, null, null, currency,
                    FulfillmentModes.BuyOnBoard, null, null, "No ticketing configured for this operator."));
                continue;
            }

            var options = adapter.TicketOptions.Where(o => o.IsActive).ToList();
            var code = registry.Get(adapter.AdapterType);
            var fulfillment = code is { CanPurchase: true } ? FulfillmentModes.PurchasableNow : FulfillmentModes.BuyOnBoard;

            var segmentMinutes = SegmentMinutes(legs);

            QuoteOffer? chosen = null;
            if (code is not null)
            {
                var context = new QuoteContext(
                    globalId,
                    [.. legs.Select(l => new QuoteLeg(l.Mode, l.StartTime, l.EndTime))],
                    [.. options.Select(o => new QuoteCatalogueOption(o.Id, o.Name, o.Price, o.Currency, o.DurationMinutes))]);
                try
                {
                    chosen = await code.QuoteAsync(context, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "QuoteAsync threw for operator {Operator}; falling back to the catalogue", globalId);
                }
            }

            chosen ??= DefaultQuote(options, segmentMinutes);

            if (chosen is null)
            {
                offers.Add(new OperatorOfferDto(globalId, adapter.Name, null, null, currency,
                    fulfillment, adapter.Id, null, "No priced product; buy from the operator directly."));
                continue;
            }

            offers.Add(new OperatorOfferDto(globalId, adapter.Name, chosen.ProductName, chosen.Price,
                chosen.Currency, fulfillment, adapter.Id, chosen.TicketOptionId, null));
            total += chosen.Price;
            currency = chosen.Currency;
        }

        return new JourneyQuoteResponse(offers, total, currency);
    }

    private static int SegmentMinutes(List<QuoteLegDto> legs)
    {
        var start = legs.Min(l => l.StartTime);
        var end = legs.Max(l => l.EndTime);
        return Math.Max(1, (int)Math.Ceiling((end - start).TotalMinutes));
    }

    /// <summary>Catalogue fallback: cheapest active product whose window covers the segment; else the cheapest.</summary>
    private static QuoteOffer? DefaultQuote(List<TicketOption> options, int segmentMinutes)
    {
        if (options.Count == 0)
            return null;

        var covering = options
            .Where(o => o.DurationMinutes is null || o.DurationMinutes >= segmentMinutes)
            .OrderBy(o => o.Price)
            .FirstOrDefault();

        var pick = covering ?? options.OrderBy(o => o.Price).First();
        return new QuoteOffer(pick.Name, pick.Price, pick.Currency, pick.Id);
    }
}
