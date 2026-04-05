using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using GetThereShared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace GetThereAPI.Controllers;

/// <summary>
/// Provides mock ticket options and a mock purchase flow for the Shop feature.
/// No real payment or ticketing API is called — all tickets are clearly labelled as mock.
///
/// GET  /mock-tickets/{operatorId}/options   → available ticket types for the operator
/// POST /mock-tickets/{operatorId}/purchase  → purchase a mock ticket (no auth required)
///
/// Operator IDs used here match those returned by GET /operator/ticketable:
///   1 = ZET  (tram/bus, Zagreb)
///   2 = HZPP (train, Croatia)
///   3 = Bajs (city bike, Zagreb)
/// </summary>
[ApiController]
[Route("mock-tickets")]
public class MockTicketController : ControllerBase
{
    private readonly AppDbContext _db;

    // ── Hardcoded ticket catalogue ─────────────────────────────────────────

    private static readonly Dictionary<int, (string OperatorName, List<MockTicketOptionDto> Options)> Catalogue = new()
    {
        [1] = ("ZET",
        [
            new MockTicketOptionDto { OptionId = "zet-single",  Name = "Single Ride",  Description = "Valid for 90 minutes on any ZET tram or bus.",        Price = 0.80m,  Validity = "90 minutes" },
            new MockTicketOptionDto { OptionId = "zet-day",     Name = "Day Pass",     Description = "Unlimited rides all day on ZET tram and bus.",         Price = 4.00m,  Validity = "24 hours"   },
            new MockTicketOptionDto { OptionId = "zet-10ride",  Name = "10-Ride Card", Description = "10 single rides to use at any time on ZET network.",   Price = 6.50m,  Validity = "Per ride"   },
        ]),
        [2] = ("HZPP",
        [
            new MockTicketOptionDto { OptionId = "hzpp-zg-st",  Name = "Zagreb ↔ Split (one way)",  Description = "One-way train ticket between Zagreb and Split.",  Price = 25.00m, Validity = "Single journey" },
            new MockTicketOptionDto { OptionId = "hzpp-zg-ri",  Name = "Zagreb ↔ Rijeka (one way)", Description = "One-way train ticket between Zagreb and Rijeka.", Price = 18.00m, Validity = "Single journey" },
            new MockTicketOptionDto { OptionId = "hzpp-zg-os",  Name = "Zagreb ↔ Osijek (one way)", Description = "One-way train ticket between Zagreb and Osijek.", Price = 15.00m, Validity = "Single journey" },
        ]),
        [3] = ("Bajs",
        [
            new MockTicketOptionDto { OptionId = "bajs-1h",     Name = "1-Hour Pass",   Description = "Unlimited Nextbike rides in Zagreb for 1 hour.",   Price = 1.00m,  Validity = "1 hour"  },
            new MockTicketOptionDto { OptionId = "bajs-day",    Name = "Day Pass",       Description = "Unlimited Nextbike rides in Zagreb for the day.", Price = 5.00m,  Validity = "24 hours" },
            new MockTicketOptionDto { OptionId = "bajs-weekly", Name = "Weekly Pass",    Description = "Unlimited Nextbike rides in Zagreb for 7 days.",  Price = 15.00m, Validity = "7 days"   },
        ]),
    };

    // Validity minutes per option (used to compute ValidUntil)
    private static readonly Dictionary<string, int> ValidMinutes = new()
    {
        ["zet-single"]  = 90,
        ["zet-day"]     = 1440,
        ["zet-10ride"]  = 0,       // per-ride — no timed expiry; treat as 24h for mock
        ["hzpp-zg-st"]  = 0,       // single journey — treat as 24h for mock
        ["hzpp-zg-ri"]  = 0,
        ["hzpp-zg-os"]  = 0,
        ["bajs-1h"]     = 60,
        ["bajs-day"]    = 1440,
        ["bajs-weekly"] = 10080,
    };

    // TransitOperator DB ID for operators that can be saved in the Ticket table.
    // Bajs is a MobilityProvider (no TransitOperator row), so it is excluded.
    private static readonly Dictionary<int, int> DbTransitOperatorIds = new()
    {
        [1] = 1,   // ZET  → TransitOperator.Id = 1
        [2] = 2,   // HZPP → TransitOperator.Id = 2
    };

    public MockTicketController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns available ticket options for the specified operator.</summary>
    // GET /mock-tickets/{operatorId}/options
    [HttpGet("{operatorId:int}/options")]
    public ActionResult<OperationResult<List<MockTicketOptionDto>>> GetOptions(int operatorId)
    {
        if (!Catalogue.TryGetValue(operatorId, out var entry))
            return NotFound(OperationResult<List<MockTicketOptionDto>>.Fail(
                $"Operator {operatorId} not found in mock catalogue."));

        return Ok(OperationResult<List<MockTicketOptionDto>>.Ok(entry.Options));
    }

    /// <summary>
    /// Purchases a mock ticket.
    /// Requires authentication — deducts the ticket price from the user's wallet
    /// and records the transaction in the wallet history.
    /// Returns an error if the wallet balance is insufficient.
    /// If the operator maps to a TransitOperator row the ticket is also persisted to the database.
    /// </summary>
    // POST /mock-tickets/{operatorId}/purchase
    [Authorize]
    [HttpPost("{operatorId:int}/purchase")]
    public async Task<ActionResult<OperationResult<MockTicketResultDto>>> Purchase(
        int operatorId,
        [FromBody] MockTicketPurchaseRequest body)
    {
        if (!Catalogue.TryGetValue(operatorId, out var entry))
            return NotFound(OperationResult<MockTicketResultDto>.Fail(
                $"Operator {operatorId} not found in mock catalogue."));

        var option = entry.Options.FirstOrDefault(o => o.OptionId == body.OptionId);
        if (option is null)
            return BadRequest(OperationResult<MockTicketResultDto>.Fail(
                $"Option '{body.OptionId}' not found for operator {operatorId}."));

        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(OperationResult<MockTicketResultDto>.Fail("User not authenticated."));

        var quantity  = Math.Max(1, body.Quantity);
        var totalCost = option.Price * quantity;

        // Check and deduct from wallet
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet is null)
            return BadRequest(OperationResult<MockTicketResultDto>.Fail("Wallet not found."));

        if (wallet.Balance < totalCost)
            return BadRequest(OperationResult<MockTicketResultDto>.Fail(
                $"Insufficient balance. Required: €{totalCost:F2}, available: €{wallet.Balance:F2}."));

        wallet.Balance   -= totalCost;
        wallet.LastUpdated = DateTime.UtcNow;

        var validFrom  = DateTime.UtcNow;
        var mins       = ValidMinutes.TryGetValue(body.OptionId, out var m) && m > 0 ? m : 1440;
        var validUntil = validFrom.AddMinutes(mins * quantity);
        var ticketId   = Guid.NewGuid().ToString();

        var result = new MockTicketResultDto
        {
            TicketId     = ticketId,
            OperatorName = entry.OperatorName,
            TicketName   = option.Name,
            Price        = totalCost,
            ValidFrom    = validFrom.ToString("O"),
            ValidUntil   = validUntil.ToString("O"),
            QrCodeData   = ticketId,
            IsMock       = true,
        };

        // Persist ticket to DB if operator has a TransitOperator row
        Ticket? savedTicket = null;
        if (DbTransitOperatorIds.TryGetValue(operatorId, out var dbOpId))
        {
            savedTicket = new Ticket
            {
                TicketType           = option.Name,
                PurchasedAt          = validFrom,
                ValidFrom            = validFrom,
                ValidUntil           = validUntil,
                PricePaid            = totalCost,
                Format               = TicketFormat.QrCode,
                Payload              = ticketId,
                DisplayInstructions  = "MOCK TICKET — NOT VALID FOR TRAVEL",
                Status               = TicketStatus.Active,
                TicketDefinitionId   = body.OptionId,
                UserId               = userId,
                TransitOperatorId    = dbOpId,
            };
            _db.Tickets.Add(savedTicket);
        }

        // Record wallet transaction for history
        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId    = wallet.Id,
            Type        = WalletTransactionType.TicketPurchase,
            Amount      = totalCost,
            Timestamp   = DateTime.UtcNow,
            Description = $"{entry.OperatorName} — {option.Name}" + (quantity > 1 ? $" ×{quantity}" : ""),
            Ticket      = savedTicket,
        });

        await _db.SaveChangesAsync();

        return Ok(OperationResult<MockTicketResultDto>.Ok(result, "Mock ticket purchased."));
    }
}
