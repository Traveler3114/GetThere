using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

/// <summary>Which half of the wallet a <see cref="WalletTicket"/> came from.</summary>
public enum WalletTicketKind
{
    /// <summary>Brought in by the user from a file or typed by hand.</summary>
    Imported,

    /// <summary>Bought in-app through a ticketing adapter.</summary>
    Purchased
}

/// <summary>
/// One row in the wallet, whichever kind of ticket it is.
/// <para>
/// The wallet listed <em>only</em> imported tickets. Purchased ones were reachable for the few
/// seconds after buying one — <c>TicketPurchaseViewModel</c> navigates straight to the detail page —
/// and nowhere else, so <c>GET /tickets</c> was never surfaced as a list and a ticket the user had
/// paid for effectively vanished. That contradicts the product's own premise: "one app that holds
/// every ticket a traveller has — the ones it sold them and the ones they already had".
/// </para>
/// <para>
/// The two contracts do not share a base type and should not: they have separate tables, separate
/// lifecycles and separate status enums. This projects both onto what a card actually shows, keeping
/// the source object for whatever the detail screen needs. The property names deliberately match the
/// ones <c>TicketsPage</c>'s template already bound to, so the template did not have to be rewritten
/// around a new shape.
/// </para>
/// </summary>
public sealed class WalletTicket
{
    public required WalletTicketKind Kind { get; init; }

    /// <summary>Row id within its own table. Not unique across kinds — pair it with <see cref="Kind"/>.</summary>
    public required int Id { get; init; }

    public string? TicketName { get; init; }
    public string? RouteDescription { get; init; }

    /// <summary>
    /// The status enum's name. Held as a string because the two kinds use different enums that
    /// happen to share the names the converters switch on — `Active`, `Used`, `Expired`,
    /// `Cancelled`. Widening either enum without checking `TicketStatusColorConverter` would leave a
    /// badge silently unstyled.
    /// </summary>
    public required string Status { get; init; }

    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public decimal? Price { get; init; }
    public string? Currency { get; init; }

    /// <summary>When the row was created, used only for ordering. Purchased tickets carry no other timestamp.</summary>
    public DateTime SortDate { get; init; }

    /// <summary>Set for <see cref="WalletTicketKind.Imported"/>; the source contract, for the actions a card offers.</summary>
    public ImportedTicketResponse? Imported { get; init; }

    /// <summary>Set for <see cref="WalletTicketKind.Purchased"/>.</summary>
    public TicketResponse? Purchased { get; init; }

    /// <summary>
    /// Whether this ticket can still be cancelled or marked used. Only imported tickets can: there
    /// is no API that moves a purchased ticket out of `Active`, and offering the action would be a
    /// button that cannot work.
    /// </summary>
    public bool HasActions => Kind is WalletTicketKind.Imported && IsRecordedActive && !IsPending;

    /// <summary>Whether the *stored* status is the active one. Both enums spell it the same way.</summary>
    private bool IsRecordedActive =>
        string.Equals(Status, nameof(ImportedTicketStatus.Active), StringComparison.Ordinal);

    /// <summary>
    /// The status to show, which is not always the one stored.
    /// <para>
    /// The server's expiry sweep runs hourly, so a ticket whose window closed ten minutes ago still
    /// reads `Active` — and a ticket served from the device's cache may be far staler than that.
    /// Showing `Active` over a window that has shut is the one error worth avoiding at a barrier, so
    /// the display downgrades where the dates say it should.
    /// </para>
    /// <para>
    /// Downgrade only, and display only: nothing here is written back or sent anywhere. See
    /// <see cref="TicketValidity.IsPastValidity"/> for why it can never restore a status.
    /// </para>
    /// </summary>
    public string DisplayStatus =>
        TicketValidity.IsPastValidity(IsRecordedActive, ValidTo, DateTime.UtcNow)
            ? nameof(ImportedTicketStatus.Expired)
            : Status;

    /// <summary>
    /// A ticket created on this device that the server has not accepted yet — imported by a guest,
    /// or while offline.
    /// <para>
    /// It has no server id, so <see cref="Id"/> is 0 and it cannot be opened: the detail screens
    /// fetch by id. Showing it in the list anyway is the honest choice — the ticket exists and the
    /// user made it. <see cref="IsPending"/> is what the card uses to say so.
    /// </para>
    /// </summary>
    public static WalletTicket FromPending(CreateImportedTicketRequest r) => new()
    {
        Kind = WalletTicketKind.Imported,
        Id = 0,
        TicketName = r.TicketName,
        RouteDescription = r.RouteDescription,
        Status = nameof(ImportedTicketStatus.Active),
        ValidFrom = r.ValidFrom,
        ValidTo = r.ValidTo,
        Price = r.Price,
        Currency = r.Currency,
        SortDate = r.ValidFrom ?? DateTime.UtcNow,
        IsPending = true
    };

    /// <summary>True while this ticket is still only on the device.</summary>
    public bool IsPending { get; init; }

    public static WalletTicket FromImported(ImportedTicketResponse t) => new()
    {
        Kind = WalletTicketKind.Imported,
        Id = t.Id,
        TicketName = t.TicketName,
        RouteDescription = t.RouteDescription,
        Status = t.Status.ToString(),
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        Price = t.Price,
        Currency = t.Currency,
        SortDate = t.CreatedAt,
        Imported = t
    };

    public static WalletTicket FromPurchased(TicketResponse t) => new()
    {
        Kind = WalletTicketKind.Purchased,
        Id = t.Id,

        // A purchased ticket has no name of its own — the option it was bought from is what the user
        // recognises, so the card shows that rather than an empty line.
        TicketName = t.Option.Name,
        RouteDescription = t.Option.Description ?? t.Option.AdapterName,
        Status = t.Status.ToString(),
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        Price = t.Option.Price,
        Currency = t.Option.Currency,

        // TicketResponse carries no timestamp of its own — see TicketExpiryWorker, which changes
        // Status without touching one. ValidFrom is the closest thing to when it became relevant.
        SortDate = t.ValidFrom ?? DateTime.MinValue,
        Purchased = t
    };
}
