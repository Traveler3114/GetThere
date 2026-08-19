using GetThereShared.Enums;

namespace GetThereAPI.Entities;

/// <summary>
/// A buy-on-board leg held inside a booked journey: funds reserved against the wallet for a ticket the
/// user will obtain on board (scan on the tram, unlock at the dock). Cancelling the journey releases
/// the hold; obtaining the ticket fulfils it. The app never pays the operator — this is a budget hold.
/// </summary>
public class JourneyReservation
{
    public int Id { get; set; }

    public int JourneyId { get; set; }
    public Journey Journey { get; set; } = null!;

    public string OperatorGlobalId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string? ProductName { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";

    public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;

    /// <summary>The wallet Hold transaction's reference, so cancel/fulfil releases exactly this hold.</summary>
    public string? WalletHoldReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
