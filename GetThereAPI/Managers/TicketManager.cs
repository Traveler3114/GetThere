using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Mapping;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

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
            .ToListAsync(ct);

        return OperationResult<IEnumerable<TicketResponse>>.Ok(tickets.Select(TicketMapper.ToResponse).ToList());
    }
}
