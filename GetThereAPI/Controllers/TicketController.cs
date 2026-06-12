using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

using GetThereAPI.Managers;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Controllers;
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class TicketController : ControllerBase
    {
        private readonly TicketManager _ticketManager;

        public TicketController(TicketManager ticketManager)
        {
            _ticketManager = ticketManager;
        }

        [HttpGet]
        public async Task<ActionResult<OperationResult<IEnumerable<TicketResponse>>>> GetTickets(CancellationToken ct = default)
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (userId is null)
                return Unauthorized(OperationResult<IEnumerable<TicketResponse>>.Fail("USER_NOT_AUTHENTICATED", "User ID claim missing or not authenticated."));

            var result = await _ticketManager.GetTicketsAsync(userId, ct);
            return Ok(result);
        }
    }
