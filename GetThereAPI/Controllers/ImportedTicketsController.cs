using System.ComponentModel.DataAnnotations;

using GetThereAPI.Common;
using GetThereAPI.Managers;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ImportedTicketsController : ControllerBase
{
    private readonly ImportedTicketManager _manager;
    private readonly TicketUploadManager _uploads;

    public ImportedTicketsController(ImportedTicketManager manager, TicketUploadManager uploads)
    {
        _manager = manager;
        _uploads = uploads;
    }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.ImportedTicketsView)]
    public async Task<ActionResult<PagedResult<ImportedTicketResponse>>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        [FromQuery] ImportedTicketStatus? status = null,
        [FromQuery] ImportSource? source = null,
        [FromQuery] string? operatorId = null,
        [FromQuery] DateTime? validFrom = null,
        [FromQuery] DateTime? validTo = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? search = null,
        [FromQuery] bool? ungrouped = null,
        [FromQuery] bool? hasJourney = null,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.ListAsync(userId, page, perPage, status, source, operatorId, validFrom, validTo, sort, search, ungrouped, hasJourney, ct));
    }

    [HttpGet("{id}")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsView)]
    public async Task<ActionResult<ImportedTicketResponse>> GetById(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var result = await _manager.GetByIdAsync(id, userId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionKeys.ImportedTicketsCreate)]
    public async Task<ActionResult<ImportedTicketResponse>> Create(CreateImportedTicketRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var result = await _manager.CreateAsync(userId, request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Accepts a ticket file and returns what could be read from it, plus a single-use blob key to
    /// pass to <see cref="Create"/>. Nothing is created here — the user confirms the extracted
    /// fields first, because what a file yields varies from near-complete (a wallet pass) to
    /// nothing at all (a photo with no barcode).
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsCreate)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TicketUploadManager.MaxFileBytes)]
    // The global limiter allows 100 requests a minute, which is reasonable for JSON and far too
    // generous for 10 MB uploads that each run barcode and PDF decoding.
    [EnableRateLimiting("Upload")]
    public async Task<ActionResult<TicketUploadResponse>> Upload(IFormFile file, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest(new { title = "No file was uploaded." });

        await using var stream = file.OpenReadStream();
        return Ok(await _uploads.UploadAsync(userId, stream, file.Length, ct));
    }

    /// <summary>Scrapes pasted confirmation text. No file, so no storage and no blob key.</summary>
    [HttpPost("extract-text")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsCreate)]
    [EnableRateLimiting("Upload")]
    public ActionResult<TicketExtractionResult> ExtractText(ExtractTicketTextRequest request)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(TicketUploadManager.ExtractText(request.Text));
    }

    /// <summary>
    /// Streams the file a ticket was imported from. Served through here rather than from static
    /// hosting so ownership is checked on every read and the storage root stays unexposed.
    /// </summary>
    [HttpGet("{id}/file")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsView)]
    public async Task<ActionResult> GetFile(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var file = await _uploads.GetFileAsync(userId, id, ct);
        if (file is null) return NotFound();

        return File(file.Value.Content, file.Value.ContentType);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsManage)]
    public async Task<ActionResult<ImportedTicketResponse>> UpdateStatus(int id, UpdateImportedTicketStatusRequest request, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _manager.UpdateStatusAsync(id, userId, request.Status, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = PermissionKeys.ImportedTicketsManage)]
    public async Task<ActionResult> Cancel(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        await _manager.CancelAsync(id, userId, ct);
        return NoContent();
    }
}
