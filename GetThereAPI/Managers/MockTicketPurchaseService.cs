using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using GetThereShared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class MockTicketPurchaseService
{
    private const int DefaultValidityMinutes = 1440;
    private readonly AppDbContext _db;

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
            new MockTicketOptionDto { OptionId = "bajs-1h",     Name = "1-Hour Pass",   Description = "Unlimited Nextbike rides in your city for 1 hour.",   Price = 1.00m,  Validity = "1 hour"  },
            new MockTicketOptionDto { OptionId = "bajs-day",    Name = "Day Pass",       Description = "Unlimited Nextbike rides in your city for the day.", Price = 5.00m,  Validity = "24 hours" },
            new MockTicketOptionDto { OptionId = "bajs-weekly", Name = "Weekly Pass",    Description = "Unlimited Nextbike rides in your city for 7 days.",  Price = 15.00m, Validity = "7 days"   },
        ]),
        [4] = ("LPP",
        [
            new MockTicketOptionDto { OptionId = "lpp-single",  Name = "Single Ride",   Description = "Valid for 90 minutes on any LPP bus in Ljubljana.",      Price = 1.30m,  Validity = "90 minutes" },
            new MockTicketOptionDto { OptionId = "lpp-day",     Name = "Day Pass",       Description = "Unlimited rides all day on LPP buses in Ljubljana.",     Price = 5.00m,  Validity = "24 hours"   },
            new MockTicketOptionDto { OptionId = "lpp-10ride",  Name = "10-Ride Card",   Description = "10 single rides to use at any time on the LPP network.", Price = 11.00m, Validity = "Per ride"   },
        ]),
    };

    private static readonly Dictionary<string, int> ValidMinutes = new()
    {
        ["zet-single"]  = 90,
        ["zet-day"]     = 1440,
        ["zet-10ride"]  = 0,
        ["hzpp-zg-st"]  = 0,
        ["hzpp-zg-ri"]  = 0,
        ["hzpp-zg-os"]  = 0,
        ["bajs-1h"]     = 60,
        ["bajs-day"]    = 1440,
        ["bajs-weekly"] = 10080,
        ["lpp-single"]  = 90,
        ["lpp-day"]     = 1440,
        ["lpp-10ride"]  = 1440,
    };

    private static readonly Dictionary<int, int> DbTransitOperatorIds = new()
    {
        [1] = 1,
        [2] = 2,
        [4] = 3,
    };

    public MockTicketPurchaseService(AppDbContext db)
    {
        _db = db;
    }

    public OperationResult<List<MockTicketOptionDto>> GetOptions(int operatorId)
    {
        if (!Catalogue.TryGetValue(operatorId, out var entry))
            return OperationResult<List<MockTicketOptionDto>>.Fail($"Operator {operatorId} not found in mock catalogue.");

        return OperationResult<List<MockTicketOptionDto>>.Ok(entry.Options);
    }

    public async Task<OperationResult<MockTicketResultDto>> PurchaseAsync(
        string userId,
        int operatorId,
        MockTicketPurchaseRequest body)
    {
        if (!Catalogue.TryGetValue(operatorId, out var entry))
            return OperationResult<MockTicketResultDto>.Fail($"Operator {operatorId} not found in mock catalogue.");

        var option = entry.Options.FirstOrDefault(o => o.OptionId == body.OptionId);
        if (option is null)
            return OperationResult<MockTicketResultDto>.Fail($"Option '{body.OptionId}' not found for operator {operatorId}.");

        var quantity = Math.Max(1, body.Quantity);
        var totalCost = option.Price * quantity;

        await using var dbTx = await _db.Database.BeginTransactionAsync();

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet is null)
        {
            await dbTx.RollbackAsync();
            return OperationResult<MockTicketResultDto>.Fail("Wallet not found.");
        }

        if (wallet.Balance < totalCost)
        {
            await dbTx.RollbackAsync();
            return OperationResult<MockTicketResultDto>.Fail(
                $"Insufficient balance. Required: €{totalCost:F2}, available: €{wallet.Balance:F2}.");
        }

        wallet.Balance -= totalCost;
        wallet.LastUpdated = DateTime.UtcNow;

        var validFrom = DateTime.UtcNow;
        var mins = ValidMinutes.TryGetValue(body.OptionId, out var m) && m > 0 ? m : DefaultValidityMinutes;
        var validUntil = validFrom.AddMinutes(mins * quantity);
        var ticketId = Guid.NewGuid().ToString();

        var result = new MockTicketResultDto
        {
            TicketId = ticketId,
            OperatorName = entry.OperatorName,
            TicketName = option.Name,
            Price = totalCost,
            ValidFrom = validFrom.ToString("O"),
            ValidUntil = validUntil.ToString("O"),
            QrCodeData = ticketId,
            IsMock = true,
        };

        Ticket? savedTicket = null;
        if (DbTransitOperatorIds.TryGetValue(operatorId, out var dbOpId))
        {
            savedTicket = new Ticket
            {
                TicketType = option.Name,
                PurchasedAt = validFrom,
                ValidFrom = validFrom,
                ValidUntil = validUntil,
                PricePaid = totalCost,
                Format = TicketFormat.QrCode,
                Payload = ticketId,
                DisplayInstructions = "MOCK TICKET — NOT VALID FOR TRAVEL",
                Status = TicketStatus.Active,
                TicketDefinitionId = body.OptionId,
                UserId = userId,
                TransitOperatorId = dbOpId,
            };
            _db.Tickets.Add(savedTicket);
        }

        var tx = new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.TicketPurchase,
            Amount = totalCost,
            Timestamp = DateTime.UtcNow,
            Description = $"{entry.OperatorName} — {option.Name}" + (quantity > 1 ? $" ×{quantity}" : ""),
        };
        if (savedTicket is not null)
            tx.Ticket = savedTicket;

        _db.WalletTransactions.Add(tx);

        await _db.SaveChangesAsync();
        await dbTx.CommitAsync();

        return OperationResult<MockTicketResultDto>.Ok(result, "Mock ticket purchased.");
    }
}
