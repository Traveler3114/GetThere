using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GetThereAPI.Data;
using GetThereAPI.Models;
using GetThereShared.Models;
using GetThereShared.Enums;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TicketController : ControllerBase
    {
        private readonly AppDbContext _context;

        // Placeholder ticket types and prices - replace with operator data later
        private static readonly Dictionary<string, (decimal Price, int ValidMinutes)> TicketCatalogue = new()
        {
            { "single",  (1.50m,  90) },
            { "daily",   (5.00m,  1440) },
            { "weekly",  (15.00m, 10080) },
            { "monthly", (40.00m, 43200) }
        };

        public TicketController(AppDbContext context)
        {
            _context = context;
        }

        // GET /ticket/{userId}
        [HttpGet("{userId}")]
        public async Task<ActionResult<OperationResult<IEnumerable<TicketDto>>>> GetTickets(string userId)
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

            return Ok(OperationResult<IEnumerable<TicketDto>>.Ok(tickets));
        }

        // POST /ticket/purchase
        [HttpPost("purchase")]
        public async Task<ActionResult<OperationResult<TicketDto>>> Purchase(TicketDto request)
        {
            if (!TicketCatalogue.TryGetValue(request.TicketType.ToLower(), out var ticketInfo))
                return BadRequest(OperationResult<TicketDto>.Fail($"Unknown ticket type '{request.TicketType}'."));

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == request.UserId);
            if (wallet == null)
                return NotFound(OperationResult<TicketDto>.Fail("Wallet not found."));

            if (wallet.Balance < ticketInfo.Price)
                return BadRequest(OperationResult<TicketDto>.Fail("Insufficient wallet balance."));

            // Deduct wallet
            wallet.Balance -= ticketInfo.Price;
            wallet.LastUpdated = DateTime.UtcNow;

            // Create ticket
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
                UserId = request.UserId
            };

            _context.Tickets.Add(ticket);

            // Record wallet transaction
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = "ticket_purchase",
                Amount = ticketInfo.Price,
                Timestamp = DateTime.UtcNow,
                Description = $"{request.TicketType} ticket purchase"
            });

            await _context.SaveChangesAsync();

            // Link transaction to ticket after save
            var transaction = await _context.WalletTransactions
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync(t => t.WalletId == wallet.Id && t.Type == "ticket_purchase");

            if (transaction != null)
            {
                transaction.TicketId = ticket.Id;
                await _context.SaveChangesAsync();
            }

            return Ok(OperationResult<TicketDto>.Ok(new TicketDto
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
            }, "Ticket purchased successfully."));
        }
    }
}