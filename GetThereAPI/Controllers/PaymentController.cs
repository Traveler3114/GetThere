using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Dtos;
using GetThereShared.Enums;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize] // <-- entire controller requires a valid token
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
            // Read userId from token instead of trusting request.UserId
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
                return NotFound(OperationResult<WalletDto>.Fail("Wallet not found."));

            if (request.Amount <= 0)
                return BadRequest(OperationResult<WalletDto>.Fail("Amount must be greater than zero."));

            var payment = new Payment
            {
                ProviderTransactionId = Guid.NewGuid().ToString(),
                Amount = request.Amount,
                Status = PaymentStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                WalletId = wallet.Id,
                PaymentProviderId = request.PaymentProviderId
            };

            _context.Payments.Add(payment);

            wallet.Balance += request.Amount;
            wallet.LastUpdated = DateTime.UtcNow;

            _context.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = WalletTransactionType.TopUp,
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