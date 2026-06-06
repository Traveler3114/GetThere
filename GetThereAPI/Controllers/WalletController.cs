using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GetThereAPI.Controllers;
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
        public async Task<ActionResult<OperationResult<WalletResponse>>> GetWallet(CancellationToken ct = default)
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId == null)
                return Unauthorized(OperationResult<WalletResponse>.Fail("User ID claim missing or not authenticated."));

            var result = await _walletManager.GetWalletAsync(userId, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpGet("transactions")]
        public async Task<ActionResult<OperationResult<IEnumerable<WalletTransactionResponse>>>> GetTransactions(CancellationToken ct = default)
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId == null)
                return Unauthorized(OperationResult<IEnumerable<WalletTransactionResponse>>.Fail("User ID claim missing or not authenticated."));

            var result = await _walletManager.GetTransactionsAsync(userId, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }