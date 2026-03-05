using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Dtos;
using GetThereShared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class TicketManager
{
    private readonly AppDbContext _context;

    private static readonly Dictionary<string, (decimal Price, int ValidMinutes)> TicketCatalogue = new()
    {
        { "single",  (1.50m,  90) },
        { "daily",   (5.00m,  1440) },
        { "weekly",  (15.00m, 10080) },
        { "monthly", (40.00m, 43200) }
    };

    public TicketManager(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OperationResult<IEnumerable<TicketDto>>> GetTicketsAsync(string userId)
    {
        var tickets = await _context.Tickets
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.PurchasedAt)
            .Select(t => new TicketDto
            {
                Id = t.Id,
                UserId = t.UserId,
                TicketType = t.TicketType,
                PurchasedAt = t.PurchasedAt,
                ValidFrom = t.ValidFrom,
                ValidUntil = t.ValidUntil,
                Format = t.Format,
                Payload = t.Payload,
                DisplayInstructions = t.DisplayInstructions,
                Status = t.Status
            })
            .ToListAsync();

        return OperationResult<IEnumerable<TicketDto>>.Ok(tickets);
    }

    public async Task<OperationResult<TicketDto>> PurchaseTicketAsync(string userId, TicketDto request)
    {
        if (!TicketCatalogue.TryGetValue(request.TicketType.ToLower(), out var ticketInfo))
            return OperationResult<TicketDto>.Fail($"Unknown ticket type '{request.TicketType}'.");

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
            return OperationResult<TicketDto>.Fail("Wallet not found.");

        if (wallet.Balance < ticketInfo.Price)
            return OperationResult<TicketDto>.Fail("Insufficient wallet balance.");

        wallet.Balance -= ticketInfo.Price;
        wallet.LastUpdated = DateTime.UtcNow;

        var validFrom = DateTime.UtcNow;
        var validUntil = validFrom.AddMinutes(ticketInfo.ValidMinutes);

        var ticket = new Ticket
        {
            TicketType = request.TicketType.ToLower(),
            PurchasedAt = DateTime.UtcNow,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            Format = TicketFormat.QrCode,
            Payload = Guid.NewGuid().ToString(),
            DisplayInstructions = "Show QR code to the driver or validator.",
            Status = TicketStatus.Active,
            UserId = userId
        };

        _context.Tickets.Add(ticket);

        _context.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.TicketPurchase,
            Amount = ticketInfo.Price,
            Timestamp = DateTime.UtcNow,
            Description = $"{request.TicketType} ticket purchase",
            Ticket = ticket
        });

        await _context.SaveChangesAsync();

        var dto = new TicketDto
        {
            Id = ticket.Id,
            UserId = ticket.UserId,
            TicketType = ticket.TicketType,
            PurchasedAt = ticket.PurchasedAt,
            ValidFrom = ticket.ValidFrom,
            ValidUntil = ticket.ValidUntil,
            Format = ticket.Format,
            Payload = ticket.Payload,
            DisplayInstructions = ticket.DisplayInstructions,
            Status = ticket.Status
        };
        return OperationResult<TicketDto>.Ok(dto, "Ticket purchased successfully.");
    }
}