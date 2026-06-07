using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

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
    public async Task<ActionResult<OperationResult<IEnumerable<PaymentProviderResponse>>>> GetProviders(CancellationToken ct = default)
    {
        var providers = await _paymentManager.GetActiveProvidersAsync(ct);
        return Ok(OperationResult<IEnumerable<PaymentProviderResponse>>.Ok(providers));
    }

    // POST /payment/topup
    [HttpPost("topup")]
    public async Task<ActionResult<OperationResult<WalletResponse>>> TopUp(TopUpRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
            return Unauthorized(OperationResult<WalletResponse>.Fail("User ID claim missing or not authenticated."));

        var result = await _paymentManager.TopUpWalletAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
