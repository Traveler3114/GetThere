using GetThereAPI.Data;
using GetThereShared.Common;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class OperatorManager
{
    private readonly AppDbContext _context;

    public OperatorManager(AppDbContext context) => _context = context;

    //public async Task<List<OperatorDto>> GetAllAsync()
    //{
    //    return await _context.TransitOperators
    //        .Include(o => o.Country)
    //        .Include(o => o.City)
    //        .OrderBy(o => o.Name)
    //        .Select(o => new OperatorDto
    //        {
    //            Id                    = o.Id,
    //            Name                  = o.Name,
    //            LogoUrl               = o.LogoUrl,
    //            City                  = o.City != null ? o.City.Name : null,
    //            Country               = o.Country.Name,
    //            GtfsFeedUrl           = o.GtfsFeedUrl,
    //            GtfsRealtimeFeedUrl   = o.GtfsRealtimeFeedUrl,
    //            RealtimeFeedFormat    = o.RealtimeFeedFormat,
    //            RealtimeAuthType      = o.RealtimeAuthType,
    //            RealtimeAuthConfig    = o.RealtimeAuthConfig,
    //            RealtimeAdapterConfig = o.RealtimeAdapterConfig,
    //        }).ToListAsync();
    //}
}
