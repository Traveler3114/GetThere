using GetThereAPI.Common;
using GetThereAPI.Managers;

using GetThereShared.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly WalletManager _walletManager;

    public WalletController(WalletManager walletManager) { _walletManager = walletManager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.WalletsView)]
    public async Task<ActionResult<WalletResponse>> GetWallet(CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _walletManager.GetWalletAsync(userId, ct);
        if (result is null) return Problem(statusCode: 404, title: "Wallet not found");

        return Ok(result);
    }

    // WalletsTopUp, not WalletsManage: the User role holds WalletsManage, and this endpoint credits
    // balance with no payment provider behind it.
    [HttpPost("topup")]
    [Authorize(Policy = PermissionKeys.WalletsTopUp)]
    public async Task<ActionResult<WalletResponse>> TopUp(TopUpRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _walletManager.TopUpAsync(userId, request.Amount, request.PaymentMethod, ct);
        return CreatedAtAction(nameof(GetWallet), new { }, result);
    }

    [HttpPost("ensure")]
    [Authorize(Policy = PermissionKeys.WalletsView)]
    public async Task<ActionResult<WalletResponse>> EnsureWallet(CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();

        await _walletManager.EnsureWalletAsync(userId, ct);
        var response = await _walletManager.GetWalletAsync(userId, ct);
        return CreatedAtAction(nameof(GetWallet), new { }, response);
    }
}
