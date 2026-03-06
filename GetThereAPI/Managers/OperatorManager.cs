using GetThereAPI.Data;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class OperatorManager
{
    private readonly AppDbContext _context;

    public OperatorManager(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<TransitOperatorDto>> GetAllAsync()
    {
        return await _context.TransitOperators
            .Where(o => o.IsActive)
            .Include(o => o.Country)
            .Include(o => o.City)
            .OrderBy(o => o.Name)
            .Select(o => new TransitOperatorDto
            {
                Id                 = o.Id,
                Name               = o.Name,
                LogoUrl            = o.LogoUrl,
                City               = o.City != null ? o.City.Name : null,
                Country            = o.Country.Name,
                GtfsFeedUrl        = o.GtfsFeedUrl,
                GtfsRealtimeFeedUrl= o.GtfsRealtimeFeedUrl,
                IsTicketingEnabled = o.IsTicketingEnabled,
                IsScheduleEnabled  = o.IsScheduleEnabled,
                IsRealtimeEnabled  = o.IsRealtimeEnabled,
            }).ToListAsync();
    }
}