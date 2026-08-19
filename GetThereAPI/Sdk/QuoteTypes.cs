namespace GetThereAPI.Sdk;

/// <summary>What an operator's adapter is given to price one segment of a journey: the segment's legs
/// plus that operator's sellable catalogue. Operator-specific fare logic (zones, station pairs,
/// rental minutes) lives inside the adapter using these.</summary>
public record QuoteContext(
    string OperatorGlobalId,
    IReadOnlyList<QuoteLeg> Legs,
    IReadOnlyList<QuoteCatalogueOption> Options);

public record QuoteLeg(string Mode, DateTime StartTime, DateTime EndTime);

public record QuoteCatalogueOption(int Id, string Name, decimal Price, string Currency, int? DurationMinutes);

/// <summary>An adapter's chosen product + price for its segment.</summary>
public record QuoteOffer(string? ProductName, decimal Price, string Currency, int? TicketOptionId);
