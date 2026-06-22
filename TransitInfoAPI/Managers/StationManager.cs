using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Managers;

public class StationManager
{
    private readonly TransitDbContext _db;
    private readonly ScheduleManager _schedule;

    public StationManager(TransitDbContext db, ScheduleManager schedule)
    {
        _db = db;
        _schedule = schedule;
    }

    public async Task<List<StationDto>> GetAllAsync(
        double? lat, double? lon, double? radiusKm, int? countryId, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalStations
            .Include(cs => cs.Country)
            .Where(cs => cs.IsActive && cs.StationType == StationType.Stop)
            .AsQueryable();

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

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
            .Skip((page - 1) * perPage)
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
                PrimaryRouteType = cs.PrimaryRouteType.ToString(),
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
            .Where(cs => cs.OnestopId == onestopId && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                PrimaryRouteType = cs.PrimaryRouteType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<StationDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.CanonicalStations
            .Include(cs => cs.Country)
            .Include(cs => cs.City)
            .Where(cs => cs.Id == id && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                PrimaryRouteType = cs.PrimaryRouteType.ToString(),
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
            .Where(cs => cs.GlobalId == globalId && cs.IsActive && cs.StationType == StationType.Stop)
            .Select(cs => new StationDto
            {
                Id = cs.Id,
                GlobalId = cs.GlobalId,
                OnestopId = cs.OnestopId,
                Name = cs.Name,
                Latitude = cs.Latitude,
                Longitude = cs.Longitude,
                StationType = cs.StationType.ToString(),
                PrimaryRouteType = cs.PrimaryRouteType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<StationDto>> SearchAsync(string? q, RouteType? routeType, int? countryId, string? countryName, string? stationType, int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.CanonicalStations
            .Include(cs => cs.Country)
            .Where(cs => cs.IsActive && cs.StationType == StationType.Stop)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(cs => cs.Name.Contains(q));

        if (routeType.HasValue)
            query = query.Where(cs => cs.PrimaryRouteType == routeType.Value);

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(cs => cs.Country != null && cs.Country.Name == countryName);

        if (!string.IsNullOrWhiteSpace(stationType) && Enum.TryParse<StationType>(stationType, out var st))
            query = query.Where(cs => cs.StationType == st);

        return await query
            .OrderBy(cs => cs.Id)
            .Skip((page - 1) * perPage)
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
                PrimaryRouteType = cs.PrimaryRouteType.ToString(),
                CountryName = cs.Country.Name,
                CityName = cs.City != null ? cs.City.Name : null
            })
            .ToListAsync(ct);
    }

    public async Task<List<StationOperatorDto>> GetOperatorsAsync(string onestopId, CancellationToken ct)
    {
        var station = await _db.CanonicalStations
            .FirstOrDefaultAsync(cs => cs.OnestopId == onestopId && cs.IsActive && cs.StationType == StationType.Stop, ct);

        if (station is null) return [];

        return await _db.CanonicalStationOperators
            .Include(cso => cso.Operator)
            .Where(cso => cso.CanonicalStationId == station.Id)
            .Select(cso => new StationOperatorDto
            {
                GlobalId = cso.Operator.GlobalId,
                Name = cso.Operator.Name,
                OperatorType = cso.Operator.OperatorType.ToString()
            })
            .ToListAsync(ct);
    }

    public async Task<List<DepartureDto>> GetDeparturesAsync(int stationId, DateTime? from, int count, CancellationToken ct)
    {
        return await _schedule.GetDeparturesAsync(stationId, from ?? DateTime.UtcNow, count, ct);
    }

    public async Task<int> GetTotalCountAsync(double? lat, double? lon, double? radiusKm, int? countryId, string? countryName, CancellationToken ct)
    {
        var query = _db.CanonicalStations
            .Where(cs => cs.IsActive && cs.StationType == StationType.Stop);

        if (countryId.HasValue)
            query = query.Where(cs => cs.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(countryName))
            query = query.Where(cs => cs.Country != null && cs.Country.Name == countryName);

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

        return await query.CountAsync(ct);
    }
}
