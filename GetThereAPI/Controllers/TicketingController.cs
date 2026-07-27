using System.ComponentModel.DataAnnotations;

using GetThereAPI.Common;
using GetThereAPI.Managers;

using GetThereShared.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("tickets")]
[Authorize]
public class TicketingController : ControllerBase
{
    private readonly TicketingManager _ticketingManager;

    public TicketingController(TicketingManager ticketingManager) { _ticketingManager = ticketingManager; }

    [HttpGet("options")]
    [Authorize(Policy = PermissionKeys.TicketsView)]
    public async Task<ActionResult<List<TicketOptionResponse>>> GetOptions(CancellationToken ct = default)
    {
        var result = await _ticketingManager.GetTicketOptionsAsync(ct);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.TicketsView)]
    public async Task<ActionResult<List<TicketResponse>>> GetMyTickets(CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _ticketingManager.GetUserTicketsAsync(userId, ct);
        return Ok(result);
    }

    [HttpPost("purchase")]
    [Authorize(Policy = PermissionKeys.TicketsCreate)]
    /// <param name="idempotencyKey">
    /// Optional client-generated key. Sending the same key twice returns the original ticket instead
    /// of charging the wallet again — mobile clients retry, and a retry must not double-charge.
    /// </param>
    public async Task<ActionResult<TicketResponse>> Purchase(
        PurchaseTicketRequest request,
        [FromHeader(Name = "Idempotency-Key"), StringLength(64, MinimumLength = 8)] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _ticketingManager.PurchaseTicketAsync(userId, request.AdapterId, request.OptionId, idempotencyKey, ct);
        return CreatedAtAction(nameof(GetMyTickets), new { }, result);
    }
}
