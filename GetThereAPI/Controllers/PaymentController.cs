using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GetThereAPI.Controllers;

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

    // GET /payment/providers
    [HttpGet("providers")]
    public async Task<ActionResult<OperationResult<IEnumerable<PaymentProviderDto>>>> GetProviders()
    {
        var providers = await _paymentManager.GetActiveProvidersAsync();
        return Ok(OperationResult<IEnumerable<PaymentProviderDto>>.Ok(providers));
    }

    // POST /payment/topup
    [HttpPost("topup")]
    public async Task<ActionResult<OperationResult<WalletDto>>> TopUp(TopUpDto request)
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId == null)
            return Unauthorized(OperationResult<WalletDto>.Fail("User ID claim missing or not authenticated."));

        var result = await _paymentManager.TopUpWalletAsync(userId, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
