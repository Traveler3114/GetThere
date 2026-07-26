using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Contracts;
using GetThereShared.Enums;
using GetThereAPI.Common;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ImportedTicketsController : ControllerBase
{
    private readonly ImportedTicketManager _manager;

    public ImportedTicketsController(ImportedTicketManager manager) { _manager = manager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.ImportedTicketsView)]
    public async Task<ActionResult<List<ImportedTicketResponse>>> List(
        [FromQuery] ImportedTicketStatus? status = null,
        [FromQuery] ImportSource? source = null,
        CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();
        return Ok(await _manager.ListAsync(userId, status, source, ct));
    }

    [HttpGet("{id}")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsView)]
    public async Task<ActionResult<ImportedTicketResponse>> GetById(int id, CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();
        var result = await _manager.GetByIdAsync(id, userId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionKeys.ImportedTicketsCreate)]
    public async Task<ActionResult<ImportedTicketResponse>> Create(CreateImportedTicketRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();
        var result = await _manager.CreateAsync(userId, request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsManage)]
    public async Task<ActionResult<ImportedTicketResponse>> UpdateStatus(int id, UpdateImportedTicketStatusRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();
        return Ok(await _manager.UpdateStatusAsync(id, userId, request.Status, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsManage)]
    public async Task<ActionResult> Cancel(int id, CancellationToken ct = default)
    {
        var userId = User.FindFirst(JwtClaimTypes.UserId)?.Value;
        if (userId is null) return Unauthorized();
        await _manager.CancelAsync(id, userId, ct);
        return NoContent();
    }
}
