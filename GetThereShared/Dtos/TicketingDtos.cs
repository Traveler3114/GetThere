using GetThereShared.Enums;

namespace GetThereShared.Dtos;

/// <summary>
/// A purchased transit ticket stored in the user's wallet.
/// </summary>
public class TicketDto
{
    public int           Id                  { get; set; }
    public string        UserId              { get; set; } = string.Empty;
    public string        TicketType          { get; set; } = string.Empty;
    public DateTime      PurchasedAt         { get; set; }
    public DateTime?     ValidFrom           { get; set; }
    public DateTime?     ValidUntil          { get; set; }
    public TicketFormat? Format              { get; set; }

    /// <summary>The actual ticket payload — QR code data, barcode, PDF URL etc.</summary>
    public string?       Payload             { get; set; }

    /// <summary>Human-readable instructions for using this ticket.</summary>
    public string?       DisplayInstructions { get; set; }

    public TicketStatus  Status              { get; set; } = TicketStatus.Active;
    public int?          TransitOperatorId   { get; set; }
}
