using GetThereAPI.Data;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class TicketableCatalogueService
{
    private readonly AppDbContext _db;
    private readonly IBikeStationCache _mobility;

    private static readonly Dictionary<int, int> MobilityProviderIds = new()
    {
        [3] = 1,
    };

    private static readonly Dictionary<int, int> TicketableToDbTransitId = new()
    {
        [1] = 1,
        [2] = 2,
        [4] = 3,
    };

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

    public TicketableCatalogueService(AppDbContext db, IBikeStationCache mobility)
    {
        _db = db;
        _mobility = mobility;
    }

    public async Task<List<TicketableOperatorDto>> GetTicketableOperatorsAsync(int? countryId)
    {
        var dbOps = await _db.TransitOperators
            .Where(o => TicketableToDbTransitId.Values.Contains(o.Id))
            .Select(o => new { o.Id, o.LogoUrl })
            .ToListAsync();

        var logoMap = dbOps.ToDictionary(o => o.Id, o => o.LogoUrl);

        string? countryName = null;
        if (countryId.HasValue)
        {
            countryName = await _db.Countries
                .Where(c => c.Id == countryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            if (countryName is null)
                return [];
        }

        var result = new List<TicketableOperatorDto>();

        foreach (var t in TicketableList)
        {
            if (MobilityProviderIds.TryGetValue(t.Id, out var mobilityDbId))
            {
                if (countryName is not null
                    && !_mobility.HasStationsInCountry(mobilityDbId, countryName))
                {
                    continue;
                }

                result.Add(new TicketableOperatorDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Type = t.Type,
                    Color = t.Color,
                    Description = t.Description,
                    City = t.City,
                    Country = countryName ?? t.Country,
                    IsMock = t.IsMock,
                    LogoUrl = t.LogoUrl,
                });
                continue;
            }

            if (countryName is not null && t.Country != countryName)
                continue;

            TicketableToDbTransitId.TryGetValue(t.Id, out var dbId);
            result.Add(new TicketableOperatorDto
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type,
                Color = t.Color,
                Description = t.Description,
                City = t.City,
                Country = t.Country,
                IsMock = t.IsMock,
                LogoUrl = logoMap.TryGetValue(dbId, out var url) ? url : t.LogoUrl,
            });
        }

        return result;
    }
}
