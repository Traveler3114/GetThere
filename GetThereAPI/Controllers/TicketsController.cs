using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class TicketsController : ControllerBase
{
    private readonly TicketManager _ticketManager;

    public TicketsController(TicketManager ticketManager) { _ticketManager = ticketManager; }

    [HttpGet("types")]
    public async Task<ActionResult<OperationResult<List<TicketTypeResponse>>>> GetTypes(CancellationToken ct = default)
    {
        var result = await _ticketManager.GetTicketTypesAsync(ct);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("purchase")]
    public async Task<ActionResult<OperationResult<TicketInstanceResponse>>> Purchase(PurchaseTicketRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _ticketManager.PurchaseTicketAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<OperationResult<List<TicketInstanceResponse>>>> GetMyTickets(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _ticketManager.GetUserTicketsAsync(userId, ct);
        return Ok(result);
    }

    [HttpPost("{ticketId}/validate")]
    public async Task<ActionResult<OperationResult<TicketValidationResponse>>> Validate(int ticketId,
        [FromQuery] double? lat, [FromQuery] double? lon, CancellationToken ct = default)
    {
        var inspectorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var result = await _ticketManager.ValidateTicketAsync(ticketId, inspectorId, lat, lon, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
