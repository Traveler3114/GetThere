using GetThereAPI.Managers;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GetThereAPI.Controllers
{
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
        public async Task<ActionResult<OperationResult<IEnumerable<TicketDto>>>> GetTickets()
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var result = await _ticketManager.GetTicketsAsync(userId);
            return Ok(result);
        }

        [HttpPost("purchase")]
        public async Task<ActionResult<OperationResult<TicketDto>>> Purchase(TicketDto request)
        {
            var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var result = await _ticketManager.PurchaseTicketAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}