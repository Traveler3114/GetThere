using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GetThereAPI.Data;
using GetThereAPI.Models;
using GetThereShared.Models;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WalletController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;

        public WalletController(AppDbContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET /wallet/{userId}
        [HttpGet("{userId}")]
        public async Task<ActionResult<WalletDto>> GetWallet(string userId)
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
                return NotFound(OperationResult.Fail("Wallet not found."));

            var walletDto = new WalletDto
            {
                Id = wallet.Id,
                Balance = wallet.Balance,
                LastUpdated = wallet.LastUpdated
            };

            return Ok(walletDto);
        }

        // GET /wallet/{userId}/transactions
        [HttpGet("{userId}/transactions")]
        public async Task<ActionResult<IEnumerable<WalletTransactionDto>>> GetTransactions(string userId)
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null)
                return NotFound(OperationResult.Fail("Wallet not found."));

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

            return Ok(transactions);
        }

        // POST /wallet/topup
        //[HttpPost("topup")]
        //public async Task<ActionResult<WalletDto>> TopUp(TopUpDto request)
        //{
        //    var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == request.UserId);
        //    if (wallet == null)
        //        return NotFound(OperationResult.Fail("Wallet not found."));

        //    if (request.Amount <= 0)
        //        return BadRequest(OperationResult.Fail("Amount must be greater than zero."));

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

        //    return Ok(new WalletDto
        //    {
        //        Id = wallet.Id,
        //        Balance = wallet.Balance,
        //        LastUpdated = wallet.LastUpdated
        //    });
        //}
    }
}