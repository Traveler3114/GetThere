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

        var configuredInstanceKeys = await _db.TransitOperators
            .Where(o => o.CountryId == countryId.Value
                        && !string.IsNullOrWhiteSpace(o.GtfsFeedUrl)
                        && !string.IsNullOrWhiteSpace(o.OtpInstanceKey))
            .Select(o => o.OtpInstanceKey.Trim())
            .Distinct()
            .ToListAsync(ct);

        foreach (var key in configuredInstanceKeys)
        {
            if (_otp.Instances.ContainsKey(key))
                return key;
        }

        return _otp.DefaultInstance;
    }
}
