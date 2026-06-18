using System.Text;

using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Managers;

public class OnestopIdManager
{
    private const string Base32 = "0123456789bcdefghjkmnpqrstuvwxyz";

    public string EncodeGeohash(double lat, double lon, int precision)
    {
        var latMin = -90.0;
        var latMax = 90.0;
        var lonMin = -180.0;
        var lonMax = 180.0;

        var bits = new bool[precision * 5];
        for (var i = 0; i < bits.Length; i++)
        {
            if (i % 2 == 0)
            {
                var mid = (lonMin + lonMax) / 2;
                if (lon >= mid) { bits[i] = true; lonMin = mid; }
                else { lonMax = mid; }
            }
            else
            {
                var mid = (latMin + latMax) / 2;
                if (lat >= mid) { bits[i] = true; latMin = mid; }
                else { latMax = mid; }
            }
        }

        var chars = new char[precision];
        for (var i = 0; i < precision; i++)
        {
            var value = 0;
            for (var j = 0; j < 5; j++)
            {
                if (i * 5 + j < bits.Length && bits[i * 5 + j])
                    value |= 1 << (4 - j);
            }
            chars[i] = Base32[value];
        }

        return new string(chars);
    }

    public string ToNameSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";

        var normalized = name.ToLowerInvariant().Trim();

        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var mapped = c switch
            {
                'č' or 'ć' => 'c',
                'š' => 's',
                'ž' => 'z',
                'đ' => 'd',
                'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => 'a',
                'è' or 'é' or 'ê' or 'ë' => 'e',
                'ì' or 'í' or 'î' or 'ï' => 'i',
                'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' => 'o',
                'ù' or 'ú' or 'û' or 'ü' => 'u',
                'ñ' => 'n',
                'ý' or 'ÿ' => 'y',
                _ => c
            };

            if (char.IsLetterOrDigit(mapped))
                sb.Append(mapped);
            else if (mapped == '-' || mapped == '.')
                sb.Append(mapped);
        }

        var slug = sb.ToString();
        slug = slug.Trim('-', '.');

        if (string.IsNullOrEmpty(slug)) return "unknown";
        if (slug.Length > 64) slug = slug[..64];

        return slug;
    }

    public string GenerateStopOnestopId(double lat, double lon, string name, RouteType routeType)
    {
        var geohash = EncodeGeohash(lat, lon, 9);
        var slug = ToNameSlug(name);
        var rtSuffix = RouteTypeToOnestopSuffix(routeType);
        return $"s-{geohash}-{slug}~{rtSuffix}";
    }

    public string GenerateOperatorOnestopId(double lat, double lon, string name)
    {
        var geohash = EncodeGeohash(lat, lon, 6);
        var slug = ToNameSlug(name);
        return $"o-{geohash}-{slug}";
    }

    public string GenerateOperatorOnestopId(string isoCode, string name)
    {
        var slug = ToNameSlug(name);
        return $"o-{isoCode.ToLowerInvariant()}-{slug}";
    }

    public string GenerateFeedOnestopId(double lat, double lon, string feedId)
    {
        var geohash = EncodeGeohash(lat, lon, 6);
        var slug = ToNameSlug(feedId);
        return $"f-{geohash}-{slug}";
    }

    public string GenerateRouteOnestopId(double centerLat, double centerLon, string shortName)
    {
        var geohash = EncodeGeohash(centerLat, centerLon, 6);
        var slug = ToNameSlug(shortName);
        return $"r-{geohash}-{slug}";
    }

    private static string RouteTypeToOnestopSuffix(RouteType rt) => rt switch
    {
        RouteType.Tram => "tram",
        RouteType.Bus => "bus",
        RouteType.Trolleybus => "trolleybus",
        RouteType.Metro => "metro",
        RouteType.Rail => "rail",
        RouteType.Ferry => "ferry",
        RouteType.Flight => "flight",
        RouteType.CableCar => "cablecar",
        RouteType.Funicular => "funicular",
        RouteType.Coach => "coach",
        RouteType.BikeShare => "bikeshare",
        RouteType.ScooterShare => "scootershare",
        _ => "unknown"
    };
}
