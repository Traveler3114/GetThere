namespace GetThereShared.Enums;

/// <summary>Lifecycle of a buy-on-board leg reserved inside a booked journey.</summary>
public enum ReservationStatus
{
    /// <summary>Funds are held; the ticket will be bought on board.</summary>
    Reserved,

    /// <summary>The hold was released (journey cancelled) and funds returned to spendable.</summary>
    Released,

    /// <summary>The ticket was obtained on board; the hold was released and the ticket imported.</summary>
    Fulfilled,
}
