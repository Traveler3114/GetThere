using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GetThereAPI.Data;
using GetThereAPI.Models;
using GetThereShared.Models;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PaymentController(AppDbContext context)
        {
            _context = context;
        }

        // POST /payment/topup
        [HttpPost("topup")]
        public async Task<ActionResult<OperationResult<WalletDto>>> TopUp(TopUpDto request)
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == request.UserId);
            if (wallet == null)
                return NotFound(OperationResult<WalletDto>.Fail("Wallet not found."));

            if (request.Amount <= 0)
                return BadRequest(OperationResult<WalletDto>.Fail("Amount must be greater than zero."));

            // Save payment record
            var payment = new Payment
            {
                Provider = "manual",
                ProviderTransactionId = Guid.NewGuid().ToString(),
                Amount = request.Amount,
                Status = "completed",
                CreatedAt = DateTime.UtcNow,
                WalletId = wallet.Id
            };

            _context.Payments.Add(payment);

            // Update wallet balance
            wallet.Balance += request.Amount;
            wallet.LastUpdated = DateTime.UtcNow;

            // Record wallet transaction
            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = "topup",
                Amount = request.Amount,
                Timestamp = DateTime.UtcNow,
                Description = "Manual top-up"
            });

            await _context.SaveChangesAsync();

            return Ok(OperationResult<WalletDto>.Ok(new WalletDto
            {
                Id = wallet.Id,
                Balance = wallet.Balance,
                LastUpdated = wallet.LastUpdated
            }, "Wallet topped up successfully."));
        }
    }
}