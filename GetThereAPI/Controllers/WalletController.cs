using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Dtos;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize] // <-- entire controller requires a valid token
    public class WalletController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;

        public WalletController(AppDbContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET /wallet  <-- no more {userId} in URL
        [HttpGet]
        public async Task<ActionResult<OperationResult<WalletDto>>> GetWallet()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
                return NotFound(OperationResult<WalletDto>.Fail("Wallet not found."));

            return Ok(OperationResult<WalletDto>.Ok(new WalletDto
            {
                Id = wallet.Id,
                Balance = wallet.Balance,
                LastUpdated = wallet.LastUpdated
            }));
        }

        // GET /wallet/transactions  <-- no more {userId} in URL
        [HttpGet("transactions")]
        public async Task<ActionResult<OperationResult<IEnumerable<WalletTransactionDto>>>> GetTransactions()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
                return NotFound(OperationResult<IEnumerable<WalletTransactionDto>>.Fail("Wallet not found."));

            var transactions = await _context.WalletTransactions
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.Timestamp)
                .Select(t => new WalletTransactionDto
                {
                    Id = t.Id,
                    Type = t.Type,
                    Amount = t.Amount,
                    Timestamp = t.Timestamp,
                    Description = t.Description,
                    WalletId = t.WalletId,
                    TicketId = t.TicketId
                })
                .ToListAsync();

            return Ok(OperationResult<IEnumerable<WalletTransactionDto>>.Ok(transactions));
        }


        // POST /wallet/topup
        //[HttpPost("topup")]
        //public async Task<ActionResult<OperationResult<WalletDto>>> TopUp(TopUpDto request)
        //{
        //    var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == request.UserId);
        //    if (wallet == null)
        //        return NotFound(OperationResult<WalletDto>.Fail("Wallet not found."));

        //    if (request.Amount <= 0)
        //        return BadRequest(OperationResult<WalletDto>.Fail("Amount must be greater than zero."));

        //    wallet.Balance += request.Amount;
        //    wallet.LastUpdated = DateTime.UtcNow;

        //    _context.WalletTransactions.Add(new WalletTransaction
        //    {
        //        WalletId = wallet.Id,
        //        Type = "topup",
        //        Amount = request.Amount,
        //        Timestamp = DateTime.UtcNow,
        //        Description = request.Description ?? "Wallet top-up"
        //    });

        //    await _context.SaveChangesAsync();

        //    return Ok(OperationResult<WalletDto>.Ok(new WalletDto
        //    {
        //        Id = wallet.Id,
        //        Balance = wallet.Balance,
        //        LastUpdated = wallet.LastUpdated
        //    }, "Wallet topped up successfully."));
        //}
    }
}