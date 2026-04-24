using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace GetThereAPI.Controllers;

/// <summary>
/// Provides mock ticket options and a mock purchase flow for the Shop feature.
/// No real payment or ticketing API is called — all tickets are clearly labelled as mock.
///
/// GET  /mock-tickets/{operatorId}/options   → available ticket types for the operator
/// POST /mock-tickets/{operatorId}/purchase  → purchase a mock ticket
///
/// Operator IDs used here match those returned by GET /operator/ticketable:
///   1 = ZET  (tram/bus, Zagreb, Croatia)
///   2 = HZPP (train, Croatia)
///   3 = Bajs (city bike, Nextbike — multiple countries)
///   4 = LPP  (bus, Ljubljana, Slovenia)
/// </summary>
[ApiController]
[Route("mock-tickets")]
public class MockTicketController : ControllerBase
{
    private readonly MockTicketPurchaseService _purchaseService;

    public MockTicketController(MockTicketPurchaseService purchaseService)
    {
        _purchaseService = purchaseService;
    }

    [HttpGet("{operatorId:int}/options")]
    public ActionResult<OperationResult<List<MockTicketOptionDto>>> GetOptions(int operatorId)
    {
        var result = _purchaseService.GetOptions(operatorId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [Authorize]
    [HttpPost("{operatorId:int}/purchase")]
    public async Task<ActionResult<OperationResult<MockTicketResultDto>>> Purchase(
        int operatorId,
        [FromBody] MockTicketPurchaseRequest body)
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(OperationResult<MockTicketResultDto>.Fail("User not authenticated."));

        var result = await _purchaseService.PurchaseAsync(userId, operatorId, body);
        return result.Success ? Ok(result) : MapPurchaseFailure(result);
    }

    private ActionResult<OperationResult<MockTicketResultDto>> MapPurchaseFailure(OperationResult<MockTicketResultDto> result)
        => result.Message.Contains("not found in mock catalogue", StringComparison.OrdinalIgnoreCase)
            ? NotFound(result)
            : BadRequest(result);
}
