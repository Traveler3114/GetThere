namespace GetThereShared.Contracts;

/// <summary>"Buy all" for a routed itinerary: name the trip and hand over its legs. GetThereAPI prices
/// each operator segment, purchases the ones it can, and reserves wallet funds for the buy-on-board ones.</summary>
public record BookJourneyRequest(string Name, List<QuoteLegDto> Legs);

/// <summary>The booked journey: what was purchased, what was reserved, and the money moved.</summary>
public record JourneyBookingResponse(
    int JourneyId,
    string Name,
    List<BookedOfferDto> Items,
    decimal TotalPriced,
    decimal Charged,
    decimal Reserved);

public record BookedOfferDto(
    string OperatorGlobalId,
    string OperatorName,
    string? ProductName,
    decimal? Price,
    string Outcome);

/// <summary>What happened to an operator segment when the journey was booked.</summary>
public static class BookingOutcomes
{
    public const string Purchased = "purchased";
    public const string Reserved = "reserved";
    public const string BuyOnBoardUnpriced = "buy-on-board-unpriced";
}
