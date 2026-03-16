using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly WalletManager _walletManager;

        public WalletController(WalletManager walletManager)
        {
            _walletManager = walletManager;
        }

        [HttpGet]
        public async Task<ActionResult<OperationResult<WalletDto>>> GetWallet()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId == null)
                return Unauthorized(OperationResult<WalletDto>.Fail("User ID claim missing or not authenticated."));

            var result = await _walletManager.GetWalletAsync(userId);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpGet("transactions")]
        public async Task<ActionResult<OperationResult<IEnumerable<WalletTransactionDto>>>> GetTransactions()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId == null)
                return Unauthorized(OperationResult<IEnumerable<WalletTransactionDto>>.Fail("User ID claim missing or not authenticated."));

            var result = await _walletManager.GetTransactionsAsync(userId);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}