using System.ComponentModel.DataAnnotations;

using GetThereAPI.Common;
using GetThereAPI.Managers;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

/// <summary>
/// Journeys group tickets — imported and purchased alike — into a trip. Every action is scoped to
/// the caller from the JWT claim, as with imported tickets.
/// </summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public class JourneysController : ControllerBase
{
    private readonly JourneyManager _manager;

    public JourneysController(JourneyManager manager) { _manager = manager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.JourneysView)]
    public async Task<ActionResult<PagedResult<JourneyResponse>>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] JourneyStatus? status = null,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.ListAsync(userId, page, perPage, status, ct));
    }

    [HttpGet("{id}")]
    [Authorize(Policy = PermissionKeys.JourneysView)]
    public async Task<ActionResult<JourneyResponse>> GetById(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var result = await _manager.GetByIdAsync(id, userId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Proposed groupings over tickets not yet in a journey. Returned for the user to accept —
    /// accepting one is a <see cref="Create"/> with the suggested ticket ids.
    /// </summary>
    [HttpGet("suggestions")]
    [Authorize(Policy = PermissionKeys.JourneysView)]
    public async Task<ActionResult<List<JourneySuggestionResponse>>> Suggestions(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.SuggestAsync(userId, ct));
    }

    [HttpPost]
    [Authorize(Policy = PermissionKeys.JourneysCreate)]
    public async Task<ActionResult<JourneyResponse>> Create(CreateJourneyRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var result = await _manager.CreateAsync(userId, request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPatch("{id}")]
    [Authorize(Policy = PermissionKeys.JourneysManage)]
    public async Task<ActionResult<JourneyResponse>> Update(int id, UpdateJourneyRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.UpdateAsync(id, userId, request, ct));
    }

    [HttpPost("{id}/tickets")]
    [Authorize(Policy = PermissionKeys.JourneysManage)]
    public async Task<ActionResult<JourneyResponse>> AddTickets(int id, JourneyMembershipRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.AddTicketsAsync(id, userId, request, ct));
    }

    [HttpDelete("{id}/tickets")]
    [Authorize(Policy = PermissionKeys.JourneysManage)]
    public async Task<ActionResult<JourneyResponse>> RemoveTickets(int id, JourneyMembershipRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.RemoveTicketsAsync(id, userId, request, ct));
    }

    /// <summary>Deletes the journey and releases its tickets — it never deletes them.</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = PermissionKeys.JourneysManage)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        await _manager.DeleteAsync(id, userId, ct);
        return NoContent();
    }
}
