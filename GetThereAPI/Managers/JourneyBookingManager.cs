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

        var items = new List<BookedOfferDto>();
        var placedHolds = new List<(decimal Amount, string Reference)>();
        var charged = 0m;
        var reserved = 0m;

        try
        {
            foreach (var offer in quoted.Offers)
            {
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

                if (offer.FulfillmentMode == FulfillmentModes.PurchasableNow && offer.TicketingAdapterId is int adapterId)
                {
                    var idem = $"jrn-{journey.Id}-{offer.OperatorGlobalId}";
                    var ticketResp = await ticketing.PurchaseTicketAsync(userId, adapterId, offer.TicketOptionId.Value, idem, ct);

                    var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketResp.Id, ct);
                    if (ticket is not null) ticket.JourneyId = journey.Id;

                    charged += offer.Price.Value;
                    items.Add(new BookedOfferDto(offer.OperatorGlobalId, offer.OperatorName, offer.ProductName, offer.Price, BookingOutcomes.Purchased));
                }
                else
                {
                    // Buy-on-board: hold the funds.
                    var reference = $"jrn-{journey.Id}-{offer.OperatorGlobalId}";
                    await wallet.ReserveAsync(walletEntity.Id, offer.Price.Value, $"Hold: {offer.OperatorName}", reference, ct);
                    placedHolds.Add((offer.Price.Value, reference));

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Booking journey {JourneyId} failed; compensating", journey.Id);
            foreach (var (amount, reference) in placedHolds)
                await wallet.ReleaseAsync(walletEntity.Id, amount, "Release: booking failed", reference, ct);
            db.Journeys.Remove(journey); // cascades reservations; releases any attached tickets
            await db.SaveChangesAsync(ct);
            throw;
        }

        return new JourneyBookingResponse(journey.Id, journey.Name, items, quoted.Total, charged, reserved);
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
