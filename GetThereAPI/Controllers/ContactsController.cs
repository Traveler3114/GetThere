using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ContactsController : ControllerBase
{
    private readonly ContactManager _contactManager;

    public ContactsController(ContactManager contactManager) { _contactManager = contactManager; }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<ContactResponse>>>> GetAll(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _contactManager.GetContactsAsync(userId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<ContactResponse>>> Create(SaveContactRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _contactManager.SaveContactAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<ContactResponse>>> Update(int id, SaveContactRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _contactManager.UpdateContactAsync(id, userId, request, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(int id, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _contactManager.DeleteContactAsync(id, userId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("{id}/favorite")]
    public async Task<ActionResult<OperationResult<ContactResponse>>> ToggleFavorite(int id, CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var result = await _contactManager.ToggleFavoriteAsync(id, userId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }
}
