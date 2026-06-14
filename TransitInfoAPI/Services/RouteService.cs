using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Services;

public class RouteService
{
    private readonly TransitDbContext _db;

    public RouteService(TransitDbContext db)
    {
        _db = db;
    }

    public async Task<List<RouteDto>> GetAllAsync(int? operatorId, RouteType? routeType, CancellationToken ct)
    {
        var query = _db.CanonicalRoutes.Where(r => r.IsActive).AsQueryable();

        if (operatorId.HasValue)
            query = query.Where(r => r.OperatorId == operatorId.Value);
        if (routeType.HasValue)
            query = query.Where(r => r.RouteType == routeType.Value);

        return await query.Select(r => new RouteDto
        {
            Id = r.Id,
            GlobalId = r.GlobalId,
            Name = r.LongName,
            ShortName = r.ShortName,
            RouteType = r.RouteType.ToString(),
            OperatorId = r.OperatorId,
            OperatorName = r.Operator.Name
        }).ToListAsync(ct);
    }

    public async Task<RouteDto?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.CanonicalRoutes
            .Where(r => r.GlobalId == globalId && r.IsActive)
            .Select(r => new RouteDto
            {
                Id = r.Id,
                GlobalId = r.GlobalId,
                Name = r.LongName,
                ShortName = r.ShortName,
                RouteType = r.RouteType.ToString(),
                OperatorId = r.OperatorId,
                OperatorName = r.Operator.Name
            })
            .FirstOrDefaultAsync(ct);
    }
}
