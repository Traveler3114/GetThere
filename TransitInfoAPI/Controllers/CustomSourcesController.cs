using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using TransitInfoAPI.Common;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("custom-sources")]
[Authorize]
public class CustomSourcesController : ControllerBase
{
    private readonly CustomSourceManager _manager;

    public CustomSourcesController(CustomSourceManager manager) { _manager = manager; }

    [HttpGet]
    [Authorize(Policy = PermissionKeys.CustomSourcesView)]
    public async Task<ActionResult<Paginated<CustomSourceResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery, Range(1, 500)] int perPage = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _manager.GetAllAsync(page, perPage, ct);
        return Ok(new Paginated<CustomSourceResponse>(items, total, page, perPage));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionKeys.CustomSourcesView)]
    public async Task<ActionResult<CustomSourceResponse>> GetById(int id, CancellationToken ct = default)
    {
        var source = await _manager.GetByIdAsync(id, ct);
        return source is null ? NotFound() : Ok(source);
    }

    [HttpPost]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    public async Task<ActionResult<CustomSourceResponse>> Create(
        [FromBody] CreateCustomSourceRequest request,
        CancellationToken ct = default)
    {
        var created = await _manager.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    public async Task<ActionResult<CustomSourceResponse>> Update(
        int id,
        [FromBody] UpdateCustomSourceRequest request,
        CancellationToken ct = default)
    {
        var updated = await _manager.UpdateAsync(id, request, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
        => await _manager.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Fetches a sample and reports the response's shape, to fill in a data path.</summary>
    [HttpPost("{id:int}/discover")]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    public async Task<ActionResult<CustomSourceDiscoverResponse>> Discover(
        int id,
        [FromQuery] int requestIndex = 0,
        CancellationToken ct = default)
        => Ok(await _manager.DiscoverAsync(id, requestIndex, ct));

    /// <summary>Runs the extraction and returns what it produced, without importing anything.</summary>
    [HttpPost("{id:int}/preview")]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    public async Task<ActionResult<CustomSourcePreviewResponse>> Preview(int id, CancellationToken ct = default)
        => Ok(await _manager.PreviewAsync(id, ct));

    /// <summary>
    /// Stores a spreadsheet, CSV or PDF for this source and returns the URL to put on a request.
    /// <para>
    /// Uploads are how an operator who emails a file once a quarter gets imported at all. The
    /// returned <c>upload://name</c> goes in a request's URL, and everything past that — mappings,
    /// completion, versioning — is identical to a polled endpoint.
    /// </para>
    /// </summary>
    [HttpPost("{id:int}/upload")]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<ActionResult<UploadedFileResponse>> Upload(int id, IFormFile file, CancellationToken ct = default)
        => Ok(await _manager.StoreUploadAsync(id, file, ct));

    /// <summary>Lists the files uploaded for this source.</summary>
    [HttpGet("{id:int}/uploads")]
    [Authorize(Policy = PermissionKeys.CustomSourcesView)]
    public ActionResult<List<UploadedFileResponse>> Uploads(int id)
        => Ok(_manager.ListUploads(id));

    /// <summary>The C# extractors available to take over a source that configuration cannot describe.</summary>
    [HttpGet("extractors")]
    [Authorize(Policy = PermissionKeys.CustomSourcesView)]
    public ActionResult<List<ExtractorResponse>> Extractors() => Ok(_manager.ListExtractors());

    /// <summary>Fetches, versions and imports through the same path the poller uses.</summary>
    [HttpPost("{id:int}/run")]
    [Authorize(Policy = PermissionKeys.CustomSourcesManage)]
    public async Task<ActionResult<CustomSourceRunResponse>> Run(int id, CancellationToken ct = default)
        => Ok(await _manager.RunAsync(id, ct));

    [HttpGet("{id:int}/runs")]
    [Authorize(Policy = PermissionKeys.CustomSourcesView)]
    public async Task<ActionResult<List<CustomSourceRunResponse>>> GetRuns(
        int id,
        [FromQuery, Range(1, 200)] int limit = 50,
        CancellationToken ct = default)
        => Ok(await _manager.GetRunsAsync(id, limit, ct));
}
