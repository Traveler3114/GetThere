using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransitInfoAPI.Services;

public static class AlertSourceMapper
{
    public static string BuildSourceKey(string sourceId, ExtractedRow row, int index)
    {
        // Prefer stable identifiers present in the row.
        string? raw = null;
        // Common keys for stable id (ordered by preference)
        var candidates = new[] { "Link", "href", "SourceUrl", "url", "properties.id", "id", "_key" };
        foreach (var key in candidates)
        {
            if (row.TryGetValue(key, out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()))
            {
                raw = v.ToString();
                break;
            }
        }
        // Search any key containing "id" for GeoJSON features
        if (raw is null)
        {
            foreach (var kv in row)
            {
                if (kv.Key.EndsWith(".id", StringComparison.OrdinalIgnoreCase) && kv.Value is not null)
                {
                    raw = kv.Value.ToString();
                    break;
                }
            }
        }
        if (raw is null)
        {
            // Fallback: hash of title+description+geometry
            if (row.TryGetValue("Title", out var t) || row.TryGetValue("title", out t) || row.TryGetValue("properties.title", out t))
                raw = t?.ToString();
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Last fallback: geometry coordinates or whole row hash
            if (row.TryGetValue("geometry.coordinates", out var g) && g is not null)
                raw = g.ToString();
            else
                raw = JsonSerializer.Serialize(row);
        }

        // Normalise raw into a short stable token. If it looks like a URL, take last path segment.
        var stable = NormaliseStableId(raw!, index);
        return $"{sourceId}:{stable}";
    }

    private static string NormaliseStableId(string raw, int index)
    {
        raw = raw.Trim();
        // If it's a URL, extract last non-empty segment
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            var segment = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s));
            if (!string.IsNullOrEmpty(segment))
            {
                // Keep segment plus query hash if needed
                var cleaned = Uri.UnescapeDataString(segment);
                cleaned = new string(cleaned.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
                if (cleaned.Length is >= 3 and <= 80)
                    return cleaned;
            }
        }
        // If raw is short and slug-like, use it
        if (raw.Length <= 80 && raw.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
            return raw.Replace(" ", "-");
        // Otherwise hash
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
        // Include index to avoid collision when multiple rows hash similarly (unlikely)
        return $"{hash}-{index}";
    }

    public static string? ResolveField(ExtractedRow row, string? primaryKey, params string[] alternates)
    {
        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            // Try exact key first
            if (row.TryGetValue(primaryKey, out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()))
                return v.ToString()!.Trim();
            // Try dotted suffix match case-insensitive
            foreach (var kv in row)
            {
                if (kv.Key.Equals(primaryKey, StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.EndsWith("." + primaryKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (kv.Value is not null && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                        return kv.Value.ToString()!.Trim();
                }
            }
        }
        foreach (var alt in alternates)
        {
            foreach (var kv in row)
            {
                if (kv.Key.EndsWith(alt, StringComparison.OrdinalIgnoreCase) && kv.Value is not null && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                    return kv.Value.ToString()!.Trim();
                if (kv.Key.Equals(alt, StringComparison.OrdinalIgnoreCase) && kv.Value is not null)
                    return kv.Value.ToString()!.Trim();
            }
        }
        return null;
    }

    public static (string? Title, string? Description, string? Link, string? DateRaw, string? Category) ExtractCommon(ExtractedRow row)
    {
        var title = ResolveField(row, "Title", "title", "properties.title", "properties.naslov", "properties.name", "name", "header", "naslov");
        var description = ResolveField(row, "Description", "description", "properties.description", "properties.opis", "properties.summary", "summary", "opis", "text");
        var link = ResolveField(row, "Link", "link", "SourceUrl", "href", "url", "properties.url");
        var dateRaw = ResolveField(row, "Date", "date", "properties.date", "properties.datum", "datum", "published", "time");
        var category = ResolveField(row, "Category", "category", "properties.category", "properties.eventType", "properties.type", "status", "label", "kategorija");

        // For HAK GeoJSON: description may be in properties.description + properties.roadName etc.
        if (string.IsNullOrWhiteSpace(description))
        {
            // Try any properties.* that looks like description length > 20
            foreach (var kv in row)
            {
                if (kv.Key.StartsWith("properties.", StringComparison.OrdinalIgnoreCase) && kv.Value is string s && s.Length > 20 && !kv.Key.EndsWith(".title", StringComparison.OrdinalIgnoreCase))
                {
                    description = s.Trim();
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(description))
        {
            // Truncate description as title fallback
            title = description.Length > 120 ? description[..120] : description;
        }

        return (title, description, link, dateRaw, category);
    }

    public static string ResolveUrl(string? link, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(link))
            return sourceUrl;
        link = link.Trim();
        if (Uri.TryCreate(link, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https"))
            return abs.ToString();
        if (link.StartsWith("//", StringComparison.Ordinal))
            return "https:" + link;
        // Relative
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var baseUri))
        {
            try
            {
                var resolved = new Uri(baseUri, link);
                return resolved.ToString();
            }
            catch { }
        }
        return link;
    }

    public static string? MapSeverity(string? category, string? title, string? description)
    {
        var combined = $"{category} {title} {description}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(combined))
            return "Info";
        if (combined.Contains("severe") || combined.Contains("zatvor") || combined.Contains("prekid") || combined.Contains("obustav") || combined.Contains("ne prometuje") || combined.Contains("otkazan") || combined.Contains("cancel"))
            return "Severe";
        if (combined.Contains("warning") || combined.Contains("upozorenje") || combined.Contains("izmjena") || combined.Contains("obilaz") || combined.Contains("radovi") || combined.Contains("prometuje") || combined.Contains("zastoj") || combined.Contains("detour") || combined.Contains("closure"))
            return "Warning";
        return "Info";
    }

    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim();
        // Try common Croatian formats: 12.08.2026, 2026-08-21, 21.08.2026. etc.
        string[] formats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy"];
        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(raw, fmt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.GetCultureInfo("hr-HR"), System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var inv))
            return DateTime.SpecifyKind(inv, DateTimeKind.Utc);
        return null;
    }

    public static (double? Lat, double? Lon, string? GeoJson) ExtractGeometry(ExtractedRow row)
    {
        if (!row.TryGetValue("geometry.type", out var typeObj) && !row.TryGetValue("geometry.Type", out typeObj))
        {
            // Try case-insensitive search
            foreach (var kv in row)
                if (kv.Key.EndsWith("geometry.type", StringComparison.OrdinalIgnoreCase))
                    typeObj = kv.Value;
            if (typeObj is null) return (null, null, null);
        }
        var type = typeObj?.ToString();
        if (string.IsNullOrWhiteSpace(type)) return (null, null, null);

        // geometry.coordinates raw JSON
        string? coordsRaw = null;
        if (row.TryGetValue("geometry.coordinates", out var c) && c is not null) coordsRaw = c.ToString();
        else
        {
            foreach (var kv in row)
                if (kv.Key.EndsWith("geometry.coordinates", StringComparison.OrdinalIgnoreCase) && kv.Value is not null)
                    coordsRaw = kv.Value.ToString();
        }
        if (string.IsNullOrWhiteSpace(coordsRaw)) return (null, null, null);

        string geoJson;
        try
        {
            // Validate coords json
            using var doc = JsonDocument.Parse(coordsRaw!);
            geoJson = $"{{\"type\":\"{type}\",\"coordinates\":{doc.RootElement.GetRawText()}}}";
        }
        catch
        {
            geoJson = $"{{\"type\":\"{type}\",\"coordinates\":{coordsRaw}}}";
        }

        double? lat = null, lon = null;
        if (type.Equals("Point", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(coordsRaw!);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() >= 2)
                {
                    lon = doc.RootElement[0].GetDouble();
                    lat = doc.RootElement[1].GetDouble();
                }
            }
            catch { }
        }
        return (lat, lon, geoJson);
    }

    public static string? DetermineEffect(string? title, string? description)
    {
        var t = $"{title} {description}".ToLowerInvariant();
        if (t.Contains("izmjen") || t.Contains("obilaz") || t.Contains("trasa") || t.Contains("detour") || t.Contains("preusmjeren"))
            return "DETOUR";
        if (t.Contains("ne vozi") || t.Contains("obustav") || t.Contains("prekid") || t.Contains("zatvor") || t.Contains("otkaz") || t.Contains("no_service") || t.Contains("cancel") || t.Contains("suspend"))
            return "NO_SERVICE";
        return "OTHER_EFFECT";
    }
}
