using GetThereAPI.Managers;
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
    public class PaymentController : ControllerBase
    {
        private readonly PaymentManager _paymentManager;

        public PaymentController(PaymentManager paymentManager)
        {
            _paymentManager = paymentManager;
        }

        [HttpPost("topup")]
        public async Task<ActionResult<OperationResult<WalletDto>>> TopUp(TopUpDto request)
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var result = await _paymentManager.TopUpWalletAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}