using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class OperatorsController : ControllerBase
{
    private readonly OperatorService _operatorService;
    private readonly TransitDbContext _db;

    public OperatorsController(OperatorService operatorService, TransitDbContext db)
    {
        _operatorService = operatorService;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<List<OperatorDto>>>> GetAll(
        [FromQuery] int? countryId = null,
        [FromQuery] OperatorType? type = null,
        CancellationToken ct = default)
    {
        var result = await _operatorService.GetAllAsync(countryId, type, ct);
        return Ok(OperationResult<List<OperatorDto>>.Ok(result));
    }

    [HttpGet("{globalId}")]
    public async Task<ActionResult<OperationResult<OperatorDto>>> GetByGlobalId(string globalId, CancellationToken ct = default)
    {
        var op = await _operatorService.GetByGlobalIdAsync(globalId, ct);
        if (op is null) return NotFound(OperationResult<OperatorDto>.Fail("Operator not found."));
        return Ok(OperationResult<OperatorDto>.Ok(op));
    }

    [HttpGet("{globalId}/stations")]
    public async Task<ActionResult<OperationResult<List<StationDto>>>> GetStations(string globalId, CancellationToken ct = default)
    {
        var stations = await _operatorService.GetStationsAsync(globalId, ct);
        return Ok(OperationResult<List<StationDto>>.Ok(stations));
    }

    [HttpGet("{globalId}/routes")]
    public async Task<ActionResult<OperationResult<List<RouteDto>>>> GetRoutes(string globalId, CancellationToken ct = default)
    {
        var routes = await _operatorService.GetRoutesAsync(globalId, ct);
        return Ok(OperationResult<List<RouteDto>>.Ok(routes));
    }

    [HttpGet("{globalId}/feeds")]
    public async Task<ActionResult<OperationResult<List<FeedDto>>>> GetFeeds(string globalId, CancellationToken ct = default)
    {
        var feeds = await _operatorService.GetFeedsAsync(globalId, ct);
        return Ok(OperationResult<List<FeedDto>>.Ok(feeds));
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<OperatorDto>>> Create([FromBody] CreateOperatorRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(OperationResult<OperatorDto>.Fail("Operator name is required."));

        if (string.IsNullOrWhiteSpace(request.ShortName))
            return BadRequest(OperationResult<OperatorDto>.Fail("Short name is required."));

        var country = await _db.Countries.FindAsync(new object[] { request.CountryId }, ct);
        if (country is null)
            return BadRequest(OperationResult<OperatorDto>.Fail("Country not found."));

        var globalId = request.GlobalId;
        if (string.IsNullOrWhiteSpace(globalId))
            globalId = $"gt-{country.IsoCode.ToLowerInvariant()}-{request.ShortName.ToLowerInvariant()}";

        var exists = await _db.Operators.AnyAsync(o => o.GlobalId == globalId, ct);
        if (exists)
            return Conflict(OperationResult<OperatorDto>.Fail($"Operator with GlobalId '{globalId}' already exists."));

        if (!Enum.TryParse<OperatorType>(request.OperatorType, true, out var operatorType))
            return BadRequest(OperationResult<OperatorDto>.Fail($"Invalid operator type '{request.OperatorType}'."));

        var op = new Operator
        {
            GlobalId = globalId,
            Name = request.Name,
            ShortName = request.ShortName,
            Website = request.Website,
            OperatorType = operatorType,
            IsVerified = false,
            CountryId = request.CountryId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Operators.Add(op);
        await _db.SaveChangesAsync(ct);

        var dto = new OperatorDto
        {
            Id = op.Id,
            GlobalId = op.GlobalId,
            Name = op.Name,
            ShortName = op.ShortName,
            Website = op.Website,
            OperatorType = op.OperatorType.ToString(),
            IsVerified = op.IsVerified,
            CountryName = country.Name
        };

        return CreatedAtAction(nameof(GetAll), null, OperationResult<OperatorDto>.Ok(dto, "Operator created."));
    }
}
