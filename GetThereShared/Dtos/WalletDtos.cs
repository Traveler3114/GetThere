using GetThereShared.Enums;

namespace GetThereShared.Dtos;

/// <summary>The user's wallet balance and last update time.</summary>
public class WalletDto
{
    public int      Id          { get; set; }
    public decimal  Balance     { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// A single transaction in the wallet history.
/// Could be a top-up, a ticket purchase, or a refund.
/// </summary>
public class WalletTransactionDto
{
    public int                   Id          { get; set; }
    public WalletTransactionType Type        { get; set; }
    public decimal               Amount      { get; set; }
    public DateTime              Timestamp   { get; set; }
    public string?               Description { get; set; }
    public int                   WalletId    { get; set; }

    /// <summary>Linked ticket if this transaction was a ticket purchase.</summary>
    public int?                  TicketId    { get; set; }
}
