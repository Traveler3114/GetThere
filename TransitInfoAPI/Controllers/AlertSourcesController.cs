using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("alert-sources")]
[Authorize]
public class AlertSourcesController : ControllerBase
{
    private readonly AlertSourceManager _manager;

    public AlertSourcesController(AlertSourceManager manager) { _manager = manager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.AlertSourcesView)]
    public async Task<ActionResult<Paginated<AlertSourceResponse>>> GetAll(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _manager.GetAllAsync(page, perPage, ct);
        return Ok(new Paginated<AlertSourceResponse>(items, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionKeys.AlertSourcesView)]
    public async Task<ActionResult<AlertSourceResponse>> GetById(int id, CancellationToken ct = default)
    {
        var source = await _manager.GetByIdAsync(id, ct);
        return source is null ? NotFound() : Ok(source);
    }

    [HttpPost]
    [Authorize(Policy = PermissionKeys.AlertSourcesManage)]
    public async Task<ActionResult<AlertSourceResponse>> Create(
        [FromBody] CreateAlertSourceRequest request,
        CancellationToken ct = default)
    {
        var created = await _manager.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionKeys.AlertSourcesManage)]
    public async Task<ActionResult<AlertSourceResponse>> Update(
        int id,
        [FromBody] UpdateAlertSourceRequest request,
        CancellationToken ct = default)
    {
        var updated = await _manager.UpdateAsync(id, request, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionKeys.AlertSourcesManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
        => await _manager.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpPost("{id:int}/preview")]
    [Authorize(Policy = PermissionKeys.AlertSourcesManage)]
    public async Task<ActionResult<AlertSourcePreviewResponse>> Preview(int id, CancellationToken ct = default)
        => Ok(await _manager.PreviewAsync(id, ct));
}
