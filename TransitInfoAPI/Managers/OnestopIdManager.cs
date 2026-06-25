using System.Text;
using System.Text.RegularExpressions;

using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Managers;

public class OnestopIdManager
{
    private const string Base32 = "0123456789bcdefghjkmnpqrstuvwxyz";

    // Two stops 6m apart at a geohash boundary produce different geohashes and thus different OnestopIds.
    // The 20m proximity fallback in reconciliation catches most cases. Acceptable known limitation.
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
        normalized = NormalizeAbbreviations(normalized);

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

    private static string NormalizeAbbreviations(string lower)
    {
        lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\bkol\b", "kolodvor");
        lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\bul\b", "ulica");
        lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\bst\b", "sveti");
        lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\bsv\b", "sveti");
        return lower;
    }

    public string GenerateStopOnestopId(double lat, double lon, string name, RouteType routeType)
    {
        var geohash = EncodeGeohash(lat, lon, 9);
        var slug = ToNameSlug(name);
        var rtSuffix = RouteTypeToOnestopSuffix(routeType);
        return $"s-{geohash}-{slug}~{rtSuffix}";
    }

    public string GenerateOperatorOnestopId(string name)
    {
        var slug = ToNameSlug(name);
        return $"o-{slug}";
    }

    public string GenerateFeedOnestopId(double lat, double lon, string feedId)
    {
        if (lat == 0.0 && lon == 0.0)
            return $"f-{ToNameSlug(feedId)}";
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
        RouteType.Subway => "subway",
        RouteType.Train => "train",
        RouteType.Bus => "bus",
        RouteType.Ferry => "ferry",
        RouteType.CableTram => "cabletram",
        RouteType.CableCar => "cablecar",
        RouteType.Funicular => "funicular",
        RouteType.Trolleybus => "trolleybus",
        RouteType.Monorail => "monorail",
        RouteType.Bicycle => "bicycle",
        RouteType.Scooter => "scooter",
        RouteType.Airplane => "airplane",
        _ => "unknown"
    };
}
