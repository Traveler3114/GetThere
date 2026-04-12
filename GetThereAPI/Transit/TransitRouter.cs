using GetThereAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GetThereAPI.Transit;

public class TransitRouter : ITransitRouter
{
    private readonly AppDbContext _db;
    private readonly OtpOptions _otp;

    public TransitRouter(AppDbContext db, IOptions<OtpOptions> otp)
    {
        _db = db;
        _otp = otp.Value;
    }

    public async Task<string> ResolveInstanceKeyAsync(int? countryId, CancellationToken ct = default)
    {
        if (!countryId.HasValue)
            return _otp.DefaultInstance;

        var exists = await _db.Countries.AnyAsync(c => c.Id == countryId.Value, ct);
        if (!exists)
            return _otp.DefaultInstance;

        if (_otp.CountryInstanceMap.TryGetValue(countryId.Value, out var mapped)
            && _otp.Instances.ContainsKey(mapped))
        {
            return mapped;
        }

        return _otp.DefaultInstance;
    }
}
