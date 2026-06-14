using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly WalletManager _walletManager;

    public WalletController(WalletManager walletManager) { _walletManager = walletManager; }

    [HttpGet]
    public async Task<ActionResult<OperationResult<WalletResponse>>> GetWallet(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _walletManager.GetWalletAsync(userId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("topup")]
    public async Task<ActionResult<OperationResult<WalletResponse>>> TopUp(TopUpRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _walletManager.TopUpAsync(userId, request.Amount, request.PaymentMethod, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("ensure")]
    public async Task<ActionResult<OperationResult<WalletResponse>>> EnsureWallet(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _walletManager.EnsureWalletAsync(userId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
