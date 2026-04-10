using GetThereAPI.Data;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class OperatorManager
{
    private readonly AppDbContext _db;
    private readonly TransitlandManager _transitland;

    public OperatorManager(
        AppDbContext db,
        TransitlandManager transitland)
    {
        _db = db;
        _transitland = transitland;
    }

    private static readonly List<TicketableOperatorDto> TicketableList =
    [
        new TicketableOperatorDto
        {
            Id = 1, Name = "ZET", Type = "TRANSIT", Color = "#1264AB",
            Description = "Zagreb's tram and bus network.",
            City = "Zagreb", Country = "Croatia", IsMock = true,
        },
        new TicketableOperatorDto
        {
            Id = 2, Name = "HZPP", Type = "TRAIN", Color = "#6a1b9a",
            Description = "Croatian national railway — trains across Croatia.",
            City = "Zagreb", Country = "Croatia", IsMock = true,
        },
        new TicketableOperatorDto
        {
            Id = 3, Name = "Bajs", Type = "BIKE", Color = "#FF6B00",
            Description = "Nextbike city bike sharing service.",
            City = "", Country = "", IsMock = true,
        },
        new TicketableOperatorDto
        {
            Id = 4, Name = "LPP", Type = "TRANSIT", Color = "#E30613",
            Description = "Ljubljana's city bus network.",
            City = "Ljubljana", Country = "Slovenia", IsMock = true,
        },
    ];

    public async Task<List<TicketableOperatorDto>> GetTicketableOperatorsAsync(int? countryId)
    {
        var countryName = await ResolveCountryNameAsync(countryId);
        if (countryId.HasValue && countryName is null)
            return [];

        if (countryName is null)
            return [.. TicketableList];

        return TicketableList
            .Where(t => string.IsNullOrWhiteSpace(t.Country)
                        || string.Equals(t.Country, countryName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<List<TransportTypeDto>> GetTransportTypesAsync(IWebHostEnvironment env)
    {
        var imagesPath = Path.Combine(env.WebRootPath, "images");

        var all = await _db.TransportTypes
            .Select(t => new TransportTypeDto
            {
                GtfsRouteType = t.GtfsRouteType,
                Name = t.Name,
                IconFile = t.IconFile,
                Color = t.Color,
            })
            .ToListAsync();

        return all.Where(t => File.Exists(Path.Combine(imagesPath, t.IconFile))).ToList();
    }

    public async Task<List<OperatorDto>> GetAllOperatorsAsync(int? countryId = null)
    {
        var query = _db.TransitOperators
            .Include(o => o.Country)
            .Include(o => o.City)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(o => o.CountryId == countryId.Value);

        return await query
            .OrderBy(o => o.Name)
            .Select(o => new OperatorDto
            {
                Id = o.Id,
                Name = o.Name,
                LogoUrl = o.LogoUrl,
                City = o.City != null ? o.City.Name : null,
                Country = o.Country.Name,
            })
            .ToListAsync();
    }

    public async Task<List<StopDto>> GetAllStopsAsync(int? countryId = null, CancellationToken cancellationToken = default)
    {
        var countryName = await ResolveCountryNameAsync(countryId, cancellationToken);
        if (countryId.HasValue && countryName is null)
            return [];
        return await _transitland.GetStopsAsync(countryName, cancellationToken);
    }

    public List<RouteDto> GetAllRoutes(int? countryId = null) => [];

    public List<VehicleDto> GetAllVehicles(int? countryId = null) => [];

    public async Task<StopScheduleDto?> GetStopScheduleAsync(string stopId, CancellationToken cancellationToken = default)
    {
        // Fetch stop name and departures in parallel
        var detailsTask    = _transitland.GetStopDetailsAsync(stopId, cancellationToken);
        var departuresTask = _transitland.GetStopDeparturesAsync(stopId, limit: 10, cancellationToken);

        await Task.WhenAll(detailsTask, departuresTask);

        var details    = detailsTask.Result;
        var stopTimes  = departuresTask.Result;

        // Group by route + headsign to match the StopScheduleDto shape
        var groups = stopTimes
            .GroupBy(st => (st.RouteId, st.Headsign))
            .Select(g =>
            {
                var first = g.First();
                return new DepartureGroupDto
                {
                    RouteId   = g.Key.RouteId,
                    ShortName = first.RouteShortName,
                    Headsign  = g.Key.Headsign,
                    Departures = g.Select(st => new DepartureDto
                    {
                        TripId        = st.TripId,
                        ScheduledTime = st.ScheduledTime,
                        EstimatedTime = st.EstimatedTime,
                        DelayMinutes  = st.DelayMinutes,
                        IsRealtime    = st.IsRealtime,
                    }).ToList(),
                };
            })
            .ToList();

        return new StopScheduleDto
        {
            StopId   = stopId,
            StopName = details?.Name ?? stopId,
            Groups   = groups,
        };
    }

    public Task<TripDetailDto?> GetTripDetailAsync(string tripId)
        => Task.FromResult<TripDetailDto?>(null);

    private async Task<string?> ResolveCountryNameAsync(int? countryId, CancellationToken cancellationToken = default)
    {
        if (!countryId.HasValue)
            return null;

        return await _db.Countries
            .Where(c => c.Id == countryId.Value)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
