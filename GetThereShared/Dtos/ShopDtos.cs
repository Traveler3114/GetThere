namespace GetThereShared.Dtos;

/// <summary>
/// An operator available for ticket purchase.
/// Returned by GET /operator/ticketable.
/// </summary>
public class TicketableOperatorDto
{
    public int     Id          { get; set; }
    public string  Name        { get; set; } = string.Empty;
    public string? LogoUrl     { get; set; }

    /// <summary>Transport category: "TRANSIT" | "BIKE" | "TRAIN"</summary>
    public string  Type        { get; set; } = string.Empty;

    /// <summary>Brand hex colour used as UI accent, e.g. "#1264AB".</summary>
    public string  Color       { get; set; } = string.Empty;

    /// <summary>Short human-readable description shown on the shop card.</summary>
    public string  Description { get; set; } = string.Empty;

    public string  City        { get; set; } = string.Empty;
    public string  Country     { get; set; } = string.Empty;

    /// <summary>
    /// True when tickets are mocked (no real API integrated yet).
    /// The UI must display a disclaimer when this is true.
    /// </summary>
    public bool    IsMock      { get; set; }
}

/// <summary>
/// A single purchasable ticket option for a specific operator.
/// Returned by GET /mock-tickets/{operatorId}/options.
/// </summary>
public class MockTicketOptionDto
{
    public string  OptionId    { get; set; } = string.Empty;
    public string  Name        { get; set; } = string.Empty;
    public string  Description { get; set; } = string.Empty;
    public decimal Price       { get; set; }

    /// <summary>Human-readable validity period, e.g. "90 minutes".</summary>
    public string  Validity    { get; set; } = string.Empty;
}

/// <summary>
/// The result returned after a mock ticket purchase.
/// Returned by POST /mock-tickets/{operatorId}/purchase.
/// </summary>
public class MockTicketResultDto
{
    public string  TicketId      { get; set; } = string.Empty;
    public string  OperatorName  { get; set; } = string.Empty;
    public string  TicketName    { get; set; } = string.Empty;
    public decimal Price         { get; set; }

    /// <summary>ISO 8601 datetime string.</summary>
    public string  ValidFrom     { get; set; } = string.Empty;

    /// <summary>ISO 8601 datetime string.</summary>
    public string  ValidUntil    { get; set; } = string.Empty;

    /// <summary>Base64 placeholder or ticket ID used as QR code data.</summary>
    public string  QrCodeData    { get; set; } = string.Empty;

    /// <summary>Always true — these are mock tickets, not valid for travel.</summary>
    public bool    IsMock        { get; set; } = true;
}

/// <summary>
/// Request body for POST /mock-tickets/{operatorId}/purchase.
/// </summary>
public class MockTicketPurchaseRequest
{
    public string OptionId { get; set; } = string.Empty;
    public int    Quantity { get; set; } = 1;
}
