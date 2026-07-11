using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Exceptions;

namespace TransitInfoAPI.Managers;

public class CountryManager
{
    private readonly TransitDbContext _db;

public CountryManager(TransitDbContext db) { _db = db; }

    public async Task<List<CountryResponse>> GetAllAsync(int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        return await _db.Countries
            .OrderBy(c => c.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(CountryMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await _db.Countries.CountAsync(ct);
    }

    public async Task<CountryResponse> CreateAsync(CreateCountryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new AppException("Country name is required.", 400);

        if (string.IsNullOrWhiteSpace(request.IsoCode))
            throw new AppException("ISO code is required.", 400);

        request.IsoCode = request.IsoCode.ToUpperInvariant();

        var exists = await _db.Countries.AnyAsync(c => c.IsoCode == request.IsoCode, ct);
        if (exists)
            throw new AppException($"Country with ISO code '{request.IsoCode}' already exists.", 409);

        var country = new Country
        {
            Name = request.Name,
            IsoCode = request.IsoCode,
            Continent = request.Continent
        };
        _db.Countries.Add(country);
        await _db.SaveChangesAsync(ct);

        return CountryMapper.ToResponse(country);
    }
}