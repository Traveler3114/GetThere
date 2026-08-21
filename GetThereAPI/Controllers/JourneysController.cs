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
    private readonly JourneyQuoteManager _quote;
    private readonly JourneyBookingManager _booking;

    public JourneysController(JourneyManager manager, JourneyQuoteManager quote, JourneyBookingManager booking)
    {
        _manager = manager;
        _quote = quote;
        _booking = booking;
    }

    /// <summary>
    /// Prices a routed itinerary: one offer per operator segment plus a combined total, each offer
    /// marked purchasable-now or buy-on-board. Read-only — nothing is bought or reserved here.
    /// </summary>
    [HttpPost("quote")]
    [Authorize(Policy = PermissionKeys.JourneysView)]
    public async Task<ActionResult<JourneyQuoteResponse>> Quote(JourneyQuoteRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _quote.QuoteAsync(request, ct));
    }

    /// <summary>
    /// "Buy all" for a routed itinerary: creates a journey, purchases the operators it can, and
    /// reserves wallet funds for the buy-on-board ones.
    /// </summary>
    [HttpPost("book")]
    [Authorize(Policy = PermissionKeys.JourneysCreate)]
    public async Task<ActionResult<JourneyBookingResponse>> Book(BookJourneyRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var result = await _booking.BookAsync(userId, request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.JourneyId }, result);
    }

    /// <summary>Cancels a booked journey and releases its reserved funds back to spendable.</summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = PermissionKeys.JourneysManage)]
    public async Task<ActionResult> CancelBooking(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        await _booking.CancelAsync(id, userId, ct);
        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.JourneysView)]
    public async Task<ActionResult<PagedResult<JourneyResponse>>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] JourneyStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.ListAsync(userId, page, perPage, status, search, sort, ct));
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
