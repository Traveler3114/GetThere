using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class TicketManager
{
    private readonly AppDbContext _context;

    public TicketManager(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OperationResult<IEnumerable<TicketResponse>>> GetTicketsAsync(string userId, CancellationToken ct = default)
    {
        var tickets = await _context.Tickets
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.PurchasedAt)
            .Select(t => new TicketResponse
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
            .ToListAsync(ct);

        return OperationResult<IEnumerable<TicketResponse>>.Ok(tickets);
    }
}