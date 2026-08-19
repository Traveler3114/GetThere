namespace GetThereShared.Contracts;

/// <summary>
/// A routed itinerary handed to GetThereAPI for pricing. It carries only routing facts — operator id,
/// mode, times, stop coordinates — and nothing about money; GetThereAPI prices it. TransitInfoAPI
/// produces these legs (its <c>PlanLegDto</c>) and the app forwards them; the two servers never call
/// each other.
/// </summary>
public record JourneyQuoteRequest(List<QuoteLegDto> Legs);

public record QuoteLegDto(
    string? OperatorGlobalId,
    string Mode,
    bool IsTransit,
    DateTime StartTime,
    DateTime EndTime,
    double FromLat,
    double FromLon,
    double ToLat,
    double ToLon);

/// <summary>
/// The priced journey: one offer per operator segment (a journey spanning several operators yields
/// several tickets), plus the combined total. Each offer says how it can be obtained.
/// </summary>
public record JourneyQuoteResponse(List<OperatorOfferDto> Offers, decimal Total, string Currency);

public record OperatorOfferDto(
    string OperatorGlobalId,
    string OperatorName,
    string? ProductName,
    decimal? Price,
    string Currency,
    string FulfillmentMode,
    int? TicketingAdapterId,
    int? TicketOptionId,
    string? Note);

/// <summary>How a ticket is obtained. Kept as strings on the wire so adding a mode needs no client change.</summary>
public static class FulfillmentModes
{
    public const string PurchasableNow = "purchasable-now";
    public const string BuyOnBoard = "buy-on-board";
}
