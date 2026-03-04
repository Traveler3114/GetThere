using GetThereAPI.Data;
using GetThereAPI.Managers;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly PaymentManager _paymentManager;
    private readonly AppDbContext _context;

    public PaymentController(PaymentManager paymentManager, AppDbContext context)
    {
        _paymentManager = paymentManager;
        _context        = context;
    }

    // GET /payment/providers
    [HttpGet("providers")]
    public async Task<ActionResult<OperationResult<IEnumerable<PaymentProviderDto>>>> GetProviders()
    {
        var providers = await _context.PaymentProviders
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .Select(p => new PaymentProviderDto
            {
                Id      = p.Id,
                Name    = p.Name,
            })
            .ToListAsync();

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