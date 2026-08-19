using GetThereAPI.Models;

namespace GetThereAPI.Sdk;

public interface ITicketingAdapter
{
    string Name { get; }
    string AdapterType { get; }
    List<RequiredInput> RequiredInputs { get; }

    /// <summary>
    /// Whether this operator's ticket can be bought through the API here and now. Buy-on-board
    /// operators (board and scan a QR, unlock at a dock) return false: they can be priced and reserved
    /// but not purchased remotely. Default true.
    /// </summary>
    bool CanPurchase => true;

    /// <summary>
    /// Prices one segment of a journey for this operator, applying the operator's own fare rules.
    /// The default returns null, meaning "no custom logic — let the caller quote from the catalogue"
    /// (cheapest option that covers the segment). An operator with a real tariff overrides this.
    /// </summary>
    Task<QuoteOffer?> QuoteAsync(QuoteContext context, CancellationToken ct = default)
        => Task.FromResult<QuoteOffer?>(null);

    Task<PurchaseResult> PurchaseAsync(PurchaseRequest request, CancellationToken ct = default);
    Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default);

    /// <summary>
    /// Looks up a purchase the operator may already have issued, by the reference we sent on
    /// <see cref="PurchaseRequest.PaymentReference"/>.
    /// <para>
    /// This exists for recovery. The wallet is debited and committed before the operator is called,
    /// so a process that dies mid-call leaves a purchase whose outcome nobody knows — and the debit
    /// stands until someone finds out. <see cref="ValidateAsync"/> cannot answer that question: it
    /// is keyed on an external ticket id, which is exactly what was never received.
    /// </para>
    /// <para>
    /// Return the original outcome if the operator has a record of the reference, and <c>null</c> if
    /// it has none — <c>null</c> is read as "no ticket was issued" and the debit is reversed. Throw
    /// if the answer cannot be obtained right now: the purchase is then left alone and retried on
    /// the next sweep, which is always safer than guessing. The default implementation returns
    /// <c>null</c>, so an adapter that never implements lookup strands nothing; it just cannot
    /// rescue a ticket the operator did issue.
    /// </para>
    /// </summary>
    Task<PurchaseResult?> FindPurchaseAsync(string purchaseReference, CancellationToken ct = default)
        => Task.FromResult<PurchaseResult?>(null);
}
