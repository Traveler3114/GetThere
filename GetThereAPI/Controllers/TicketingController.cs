using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("tickets")]
[Authorize]
public class TicketingController : ControllerBase
{
    private readonly TicketingManager _ticketingManager;

public TicketingController(TicketingManager ticketingManager) { _ticketingManager = ticketingManager; }

    [HttpGet("options")]
    public async Task<ActionResult<List<TicketOptionResponse>>> GetOptions(CancellationToken ct = default)
    {
        var result = await _ticketingManager.GetTicketOptionsAsync(ct);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<List<TicketResponse>>> GetMyTickets(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _ticketingManager.GetUserTicketsAsync(userId, ct);
        return Ok(result);
    }

    [HttpPost("purchase")]
    public async Task<ActionResult<TicketResponse>> Purchase(
        PurchaseTicketRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _ticketingManager.PurchaseTicketAsync(userId, request.AdapterId, request.OptionId, ct);
        return CreatedAtAction(nameof(GetMyTickets), new { }, result);
    }
}
