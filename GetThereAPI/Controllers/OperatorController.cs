using GetThereAPI.Managers;
using GetThereShared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GetThereAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorController : ControllerBase
{
    private readonly OperatorManager _manager;

    public OperatorController(OperatorManager manager)
    {
        _manager = manager;
    }

    // GET /operator
    [HttpGet]
    public async Task<ActionResult<OperationResult<IEnumerable<TransitOperatorDto>>>> GetAll()
    {
        var operators = await _manager.GetAllAsync();
        return Ok(OperationResult<IEnumerable<TransitOperatorDto>>.Ok(operators));
    }
}