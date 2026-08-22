using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// "Buy all" for a routed itinerary. Prices the journey per operator, then for each segment either
/// purchases it (operators that sell via the API — mocked here) or <b>reserves</b> wallet funds for it
/// (buy-on-board operators). A reservation is a budget hold: the money stays in the wallet but is not
/// spendable, and it is released on cancel or when the ticket is obtained on board. The app never pays
/// an operator for a buy-on-board leg.
/// </summary>
public class JourneyBookingManager(
    AppDbContext db,
    JourneyQuoteManager quote,
    TicketingManager ticketing,
    WalletManager wallet,
    ILogger<JourneyBookingManager> logger)
{
    public async Task<JourneyBookingResponse> BookAsync(string userId, BookJourneyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new AppException("A journey name is required.", 400, "INVALID_NAME");

        var quoted = await quote.QuoteAsync(new JourneyQuoteRequest(request.Legs), ct);
        var walletEntity = await wallet.EnsureWalletAsync(userId, ct);

        // Everything with a price consumes available balance: a purchase debits it, a hold reserves it.
        var needed = quoted.Offers.Where(o => o.Price is > 0).Sum(o => o.Price!.Value);
        var available = walletEntity.Balance - walletEntity.Reserved;
        if (needed > available)
            throw new AppException($"This journey costs {needed:N2} but only {available:N2} is available.", 400, "INSUFFICIENT_BALANCE");

        var journey = new Journey { UserId = userId, Name = request.Name, Status = JourneyStatus.Planned };
        if (request.Legs.Count > 0)
        {
            journey.StartsAt = request.Legs.Min(l => l.StartTime);
            journey.EndsAt = request.Legs.Max(l => l.EndTime);
        }
        db.Journeys.Add(journey);
        await db.SaveChangesAsync(ct);

        var offers = quoted.Offers.ToList();
        var items = new List<BookedOfferDto>();
        var purchasedTicketIds = new List<int>();
        var ticketIdByOffer = new Dictionary<int, int>();
        var charged = 0m;
        var reserved = 0m;

        try
        {
            // ---- Phase 1: the operator calls, deliberately outside the transaction below --------
            //
            // PurchaseTicketAsync commits the wallet debit before it calls the operator, so that a
            // crash in that window leaves a Pending purchase for PurchaseReconciliationWorker to
            // settle against the operator. Enlisting it in the booking transaction would roll that
            // debit back and leave an operator-issued ticket with no local trace of it at all. So
            // purchases stay outside and are put back by refund in the catch instead.
            for (var i = 0; i < offers.Count; i++)
            {
                var offer = offers[i];
                if (offer.Price is not > 0 || offer.TicketOptionId is null) continue;
                if (offer.FulfillmentMode != FulfillmentModes.PurchasableNow || offer.TicketingAdapterId is not int adapterId) continue;

                var idem = LegReference(journey.Id, offer.OperatorGlobalId);
                var ticketResp = await ticketing.PurchaseTicketAsync(userId, adapterId, offer.TicketOptionId.Value, idem, ct);

                ticketIdByOffer[i] = ticketResp.Id;
                purchasedTicketIds.Add(ticketResp.Id);
            }

            // ---- Phase 2: everything local, in one transaction ---------------------------------
            //
            // Holds join this transaction rather than opening their own — see
            // WalletManager.BeginIfNoneAsync — so a failure here undoes them along with the journey's
            // reservations, and no compensating release is needed.
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            for (var i = 0; i < offers.Count; i++)
            {
                var offer = offers[i];

                if (offer.Price is not > 0 || offer.TicketOptionId is null)
                {
                    // Nothing to price/hold — a buy-on-board leg with no product configured.
                    db.JourneyReservations.Add(new JourneyReservation
                    {
                        JourneyId = journey.Id, OperatorGlobalId = offer.OperatorGlobalId, OperatorName = offer.OperatorName,
                        ProductName = offer.ProductName, Amount = 0m, Currency = offer.Currency, Status = ReservationStatus.Reserved,
                    });
                    items.Add(new BookedOfferDto(offer.OperatorGlobalId, offer.OperatorName, offer.ProductName, offer.Price, BookingOutcomes.BuyOnBoardUnpriced));
                    continue;
                }

                if (ticketIdByOffer.TryGetValue(i, out var ticketId))
                {
                    var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
                    if (ticket is not null) ticket.JourneyId = journey.Id;

                    charged += offer.Price.Value;
                    items.Add(new BookedOfferDto(offer.OperatorGlobalId, offer.OperatorName, offer.ProductName, offer.Price, BookingOutcomes.Purchased));
                }
                else
                {
                    // Buy-on-board: hold the funds.
                    var reference = LegReference(journey.Id, offer.OperatorGlobalId);
                    await wallet.ReserveAsync(walletEntity.Id, offer.Price.Value, $"Hold: {offer.OperatorName}", reference, ct);

                    db.JourneyReservations.Add(new JourneyReservation
                    {
                        JourneyId = journey.Id, OperatorGlobalId = offer.OperatorGlobalId, OperatorName = offer.OperatorName,
                        ProductName = offer.ProductName, Amount = offer.Price.Value, Currency = offer.Currency,
                        Status = ReservationStatus.Reserved, WalletHoldReference = reference,
                    });
                    reserved += offer.Price.Value;
                    items.Add(new BookedOfferDto(offer.OperatorGlobalId, offer.OperatorName, offer.ProductName, offer.Price, BookingOutcomes.Reserved));
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Booking journey {JourneyId} failed; compensating", journey.Id);

            // The phase-2 transaction has already rolled back by the time we get here — leaving the
            // scope disposes it — so the reservations and any holds are gone with it. What survives
            // is the journey row from before phase 1 and the purchases, which committed on purpose.
            db.ChangeTracker.Clear();

            foreach (var ticketId in purchasedTicketIds)
                await ticketing.RefundByTicketIdAsync(ticketId, "Journey booking failed", ct);

            var orphan = await db.Journeys.FirstOrDefaultAsync(j => j.Id == journey.Id, ct);
            if (orphan is not null)
            {
                db.Journeys.Remove(orphan);
                await db.SaveChangesAsync(ct);
            }

            throw;
        }

        return new JourneyBookingResponse(journey.Id, journey.Name, items, quoted.Total, charged, reserved);
    }

    /// <summary>
    /// Builds the wallet reference for a leg, capped to the 64 characters
    /// <c>JourneyReservation.WalletHoldReference</c> and <c>Purchase.IdempotencyKey</c> allow.
    /// <c>OperatorGlobalId</c> is itself 128, so a long slug would otherwise overflow both columns
    /// and fail at SaveChanges — after the wallet had already been touched.
    /// </summary>
    private static string LegReference(int journeyId, string operatorGlobalId)
    {
        var prefix = $"jrn-{journeyId}-";
        var room = 64 - prefix.Length;
        return prefix + (operatorGlobalId.Length <= room ? operatorGlobalId : operatorGlobalId[..room]);
    }

    /// <summary>Cancels a booked journey: releases every hold back to spendable and marks it cancelled.</summary>
    public async Task CancelAsync(int journeyId, string userId, CancellationToken ct = default)
    {
        var journey = await db.Journeys
            .Include(j => j.Reservations)
            .FirstOrDefaultAsync(j => j.Id == journeyId && j.UserId == userId, ct)
            ?? throw new AppException("Journey not found.", 404);

        var walletEntity = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);

        foreach (var reservation in journey.Reservations.Where(r => r.Status == ReservationStatus.Reserved))
        {
            if (walletEntity is not null && reservation.Amount > 0 && reservation.WalletHoldReference is not null)
                await wallet.ReleaseAsync(walletEntity.Id, reservation.Amount, $"Release: {journey.Name} cancelled", reservation.WalletHoldReference, ct);
            reservation.Status = ReservationStatus.Released;
        }

        journey.Status = JourneyStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }
}
