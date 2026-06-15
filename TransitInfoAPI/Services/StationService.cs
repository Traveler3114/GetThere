using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Services;

public class StationService
{
    private readonly TransitDbContext _db;
    private readonly ScheduleService _schedule;

    public StationService(TransitDbContext db, ScheduleService schedule)
    {
        _db = db;
        _schedule = schedule;
    }

    public async Task<List<StationDto>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int? after, int perPage, CancellationToken ct)
    {
        var query = _db.CanonicalStations
            .Include(cs => cs.Country)
            .Where(cs => cs.IsActive)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (after.HasValue)
            query = query.Where(cs => cs.Id > after.Value);

        if (lat is not null && lon is not null && radiusKm is not null)
        {
            var latRange = radiusKm.Value / 111.0;
            var lonRange = radiusKm.Value / (111.0 * Math.Cos(lat.Value * Math.PI / 180));
            query = query.Where(cs =>
                cs.Latitude >= lat.Value - latRange &&
                cs.Latitude <= lat.Value + latRange &&
                cs.Longitude >= lon.Value - lonRange &&
                cs.Longitude <= lon.Value + lonRange);
        }

        return await query
            .OrderBy(cs => cs.Id)
            .Take(perPage)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .ToListAsync(ct);
    }

    public async Task<StationDto?> GetByOnestopIdAsync(string onestopId, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .Where(cs => cs.OnestopId == onestopId && cs.IsActive)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<StationDto?> GetByGlobalIdAsync(string globalId, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .Where(cs => cs.GlobalId == globalId && cs.IsActive)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<StationOperatorDto>> GetOperatorsAsync(string onestopId, CancellationToken ct)
    {
        var station = await _db.CanonicalStations
            .FirstOrDefaultAsync(cs => cs.OnestopId == onestopId && cs.IsActive, ct);

        if (station is null) return [];

        return await _db.CanonicalStationOperators
            .Include(cso => cso.Operator)
            .Where(cso => cso.CanonicalStationId == station.Id)
            .Select(cso => new StationOperatorDto
            {
                GlobalId = cso.Operator.GlobalId,
                Name = cso.Operator.Name,
                OperatorType = cso.Operator.OperatorType.ToString(),
                PlatformInfo = cso.PlatformInfo
            })
            .ToListAsync(ct);
    }

    public async Task<List<DepartureDto>> GetDeparturesAsync(int stationId, DateTime? from, int count, CancellationToken ct)
    {
        return await _schedule.GetDeparturesAsync(stationId, from ?? DateTime.Now, count, ct);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct)
    {
        return await _db.CanonicalStations.CountAsync(cs => cs.IsActive, ct);
    }
}
