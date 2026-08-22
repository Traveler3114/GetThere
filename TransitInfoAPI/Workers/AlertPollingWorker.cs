using System.Text;

using AngleSharp.Html.Parser;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Workers;

public sealed class AlertPollingWorker : BackgroundService
{
    private readonly ILogger<AlertPollingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<AlertsOptions> _alertsOptions;

    public AlertPollingWorker(
        ILogger<AlertPollingWorker> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        IOptionsMonitor<AlertsOptions> alertsOptions)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _alertsOptions = alertsOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var initialDelay = PollingInterval.InitialDelaySeconds(20, 20, _logger, "Alerts:InitialDelaySeconds");
        _logger.LogInformation("Alert polling worker started with {Delay}s initial delay", initialDelay.TotalSeconds);
        await Task.Delay(initialDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            var sources = _alertsOptions.CurrentValue.Sources.Where(s => s.Enabled).ToList();
            _logger.LogInformation("Polling {Count} alert sources", sources.Count);

            foreach (var source in sources)
            {
                try
                {
                    await PollSourceAsync(source, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to poll alert source {SourceId}", source.Id);
                }
            }

            // Refresh road cache so PlanController sees fresh HAK geometry without extra DB hit
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var roadCache = scope.ServiceProvider.GetRequiredService<RoadAlertCache>();
                await roadCache.RefreshAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to refresh road alert cache");
            }

            var interval = PollingInterval.Minutes(15, 15, _logger, "Alerts:IntervalMinutes");
            // Allow per-source IntervalMinutes to shorten the global 15m? Use minimum across sources? Keep fixed 15m as spec says default 15 min.
            // If any source has a shorter interval, we could use that as global. For now use 15.
            // Optionally respect per-source interval for individual re-poll? For simplicity poll all every 15m.
            await Task.Delay(interval, ct);
        }
    }

    private async Task PollSourceAsync(AlertSourceOptions source, CancellationToken ct)
    {
        _logger.LogDebug("Polling alert source {SourceId} {Url}", source.Id, source.Url);
        // Source.Url may contain ';' separated list (e.g., jadrolinija notices + status). Fetch each and merge rows.
        var urls = source.Url.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0) urls = [source.Url];

        var allRows = new List<ExtractedRow>();
        var extractionResult = new ExtractionResult();

        foreach (var url in urls)
        {
            var body = await FetchBodyAsync(url, ct);
            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("Alert source {SourceId} fetched empty body from {Url}", source.Id, url);
                continue;
            }

            List<ExtractedRow> rows;
            if (source.Format.Equals("Html", StringComparison.OrdinalIgnoreCase))
            {
                // CustomSourceEngine.ParseHtmlRows reads a *table* (its selector picks a <table> whose
                // rows become records). A notices page is a list of articles, not a table, so it needs
                // its own item extractor — which still uses the same AngleSharp parser underneath.
                rows = ParseHtmlAlerts(body, source, extractionResult);
            }
            else if (source.Format.Equals("GeoJson", StringComparison.OrdinalIgnoreCase))
            {
                // GeoJSON is JSON with features array; reuse ParseJsonRows
                var selector = string.IsNullOrWhiteSpace(source.ItemSelector) ? "features" : source.ItemSelector;
                rows = CustomSourceEngine.ParseJsonRows(body, selector, extractionResult);
            }
            else // Json / GeoJson via Json path
            {
                var selector = source.ItemSelector ?? string.Empty;
                rows = CustomSourceEngine.ParseJsonRows(body, selector, extractionResult);
            }

            if (rows.Count == 0)
            {
                _logger.LogWarning("Alert source {SourceId} yielded 0 items from {Url} (selector '{Selector}')", source.Id, url, source.ItemSelector);
                foreach (var w in extractionResult.Warnings)
                    _logger.LogWarning("Alert source {SourceId} warning: {Warning}", source.Id, w);
            }
            allRows.AddRange(rows);
        }

        // If across all URLs we got 0 rows for this source, still log once
        if (allRows.Count == 0)
        {
            _logger.LogWarning("Alert source {SourceId} yielded 0 items overall", source.Id);
            // Still sweep? If source yields 0, we don't want to delete existing alerts just because page layout changed? Spec says log warning don't throw.
            // We keep existing alerts until next successful poll with items? But sweep logic would delete them if we proceed with empty set.
            // To avoid wiping alerts on transient layout drift, skip sweep when 0 rows.
            return;
        }

        await UpsertAlertsAsync(source, allRows, ct);
    }

    private async Task<string> FetchBodyAsync(string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        var host = uri.Host;
        var basicAuth = _configuration.GetSection("Feeds:BasicAuth").GetChildren()
            .FirstOrDefault(c => c.Key.Equals(host, StringComparison.OrdinalIgnoreCase))?.Value;

        HttpClient http;
        HttpResponseMessage response;

        if (!string.IsNullOrWhiteSpace(basicAuth))
        {
            // Use gtfs-basic client (no auto-redirect) and send Authorization
            http = _httpFactory.CreateClient("gtfs-basic");
            // Reuse ExternalFeedSource's redirect-handling? For simplicity use basic auth with manual redirect following up to 5 hops.
            // We'll implement simple fetch with auth staying on host (mirroring ExternalFeedSource logic) but using gtfs-basic which already guards off-host redirects via handler? Actually gtfs-basic handler has ConnectCallback for private network but no auth header auto.
            // We'll just send with header and let handler follow? But gtfs-basic has AllowAutoRedirect = false, so we must handle redirects ourselves.
            response = await FetchWithBasicAuthAsync(http, url, basicAuth!, ct);
        }
        else
        {
            // For regular sources, use customsource client (also no auto-redirect, but will handle via ExternalFeedSource style). For alert sources, redirects are not critical but handle manually via simple.
            http = _httpFactory.CreateClient("customsource");
            // Use customsource client which has AllowAutoRedirect = false; we need to handle redirects manually as well for unauthenticated sources (they can redirect anywhere).
            // For simplicity, use GetAsync with manual redirect handling similar to CustomSourceEngine but simplified for GET.
            response = await FetchWithRedirectAsync(http, url, null, ct);
        }

        // Ensure success
        if (response is null)
            throw new InvalidOperationException($"No response from {url}");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Alert source {url} returned {(int)response.StatusCode} {response.ReasonPhrase}");

        // Check content length cap (32 MB)
        const long maxBytes = 32L * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidOperationException($"Response exceeds 32 MB limit");

        var body = await ReadCappedAsync(response, maxBytes, ct);
        return body;
    }

    private static async Task<HttpResponseMessage> FetchWithBasicAuthAsync(HttpClient http, string url, string credential, CancellationToken ct)
    {
        var origin = new Uri(url);
        var current = origin;
        for (var hop = 0; hop < 5; hop++)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, current);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
            msg.Headers.TryAddWithoutValidation("Authorization", $"Basic {encoded}");
            var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!IsRedirect(resp.StatusCode) || resp.Headers.Location is null)
                return resp;
            var status = resp.StatusCode;
            resp.Dispose();
            if (!Uri.TryCreate(current, resp.Headers.Location, out var next) || next.Scheme is not ("http" or "https"))
                throw new InvalidOperationException($"{(int)status} from {current.Host} pointed at non-HTTP(S) URL");
            if (!string.Equals(next.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{current.Host} redirected to {next.Host} with credential — refusing off-host");
            if (origin.Scheme == "https" && next.Scheme == "http")
                throw new InvalidOperationException($"{current.Host} redirected https->http with credential — refusing");
            current = next;
        }
        throw new InvalidOperationException("Too many redirects");
    }

    private static async Task<HttpResponseMessage> FetchWithRedirectAsync(HttpClient http, string url, string? credential, CancellationToken ct)
    {
        var current = new Uri(url);
        for (var hop = 0; hop < 5; hop++)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, current);
            if (!string.IsNullOrWhiteSpace(credential))
            {
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
                msg.Headers.TryAddWithoutValidation("Authorization", $"Basic {encoded}");
            }
            var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!IsRedirect(resp.StatusCode) || resp.Headers.Location is null)
                return resp;
            var status = resp.StatusCode;
            var location = resp.Headers.Location;
            resp.Dispose();
            if (!Uri.TryCreate(current, location, out var next) || next.Scheme is not ("http" or "https"))
                throw new InvalidOperationException($"{(int)status} from {current.Host} pointed at non-HTTP(S) URL");
            current = next;
        }
        throw new InvalidOperationException("Too many redirects");
    }

    private static bool IsRedirect(System.Net.HttpStatusCode s) => s is System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Found or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect;

    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + read > maxBytes) throw new InvalidOperationException("Response exceeds size limit");
            ms.Write(buf, 0, read);
        }
        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding enc = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { enc = Encoding.GetEncoding(charset.Trim('"')); } catch { }
        }
        return enc.GetString(ms.ToArray());
    }

    private static List<ExtractedRow> ParseHtmlAlerts(string body, AlertSourceOptions source, ExtractionResult result)
    {
        var rows = new List<ExtractedRow>();
        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(body);
        var selector = string.IsNullOrWhiteSpace(source.ItemSelector) ? "article" : source.ItemSelector;
        AngleSharp.Dom.IHtmlCollection<AngleSharp.Dom.IElement> items;
        try
        {
            items = doc.QuerySelectorAll(selector);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"HTML selector '{selector}' invalid: {ex.Message}");
            return rows;
        }
        if (items.Length == 0)
        {
            result.Warnings.Add($"No element matched HTML selector '{selector}'");
            return rows;
        }

        foreach (var el in items)
        {
            var row = new ExtractedRow();
            string? title = null, description = null, dateRaw = null, link = null, category = null;

            // Title
            if (!string.IsNullOrWhiteSpace(source.Fields.Title))
            {
                var titleEl = el.QuerySelector(source.Fields.Title);
                title = titleEl?.TextContent.Trim();
                // If selector didn't match and el itself is the title container (e.g., anchor), fallback to el text
                if (string.IsNullOrWhiteSpace(title))
                    title = el.TextContent.Trim();
            }
            else
            {
                // No field selector: if element is an anchor, its text is the title
                if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                    title = el.TextContent.Trim();
                else
                {
                    // Try common title selectors inside the item
                    var titleEl = el.QuerySelector("h1, h2, h3, h4, .title, .c-article-card__title, a");
                    title = titleEl?.TextContent.Trim() ?? el.TextContent.Trim();
                }
                // Limit to first line/first 200 chars to avoid pulling whole card
                if (title is not null && title.Length > 300)
                    title = title[..300];
            }

            // Description
            if (!string.IsNullOrWhiteSpace(source.Fields.Description))
            {
                var descEl = el.QuerySelector(source.Fields.Description);
                description = descEl?.TextContent.Trim();
            }
            else
            {
                var descEl = el.QuerySelector("p, .summary, .c-article-card__summary, .entry-content, div.text-container, .card__data, .news-content");
                description = descEl?.TextContent.Trim();
                if (string.IsNullOrWhiteSpace(description) && !el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                    description = null;
            }

            // Link
            if (!string.IsNullOrWhiteSpace(source.Fields.Link))
            {
                var linkEl = el.QuerySelector(source.Fields.Link);
                link = linkEl?.GetAttribute("href") ?? linkEl?.TextContent.Trim();
            }
            else
            {
                // If element itself is anchor
                if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                    link = el.GetAttribute("href");
                else
                {
                    var linkEl = el.QuerySelector("a[href]");
                    link = linkEl?.GetAttribute("href");
                }
            }

            // Date
            if (!string.IsNullOrWhiteSpace(source.Fields.Date))
            {
                var dateEl = el.QuerySelector(source.Fields.Date);
                dateRaw = dateEl?.TextContent.Trim() ?? dateEl?.GetAttribute("datetime");
            }
            else
            {
                var dateEl = el.QuerySelector(".date, .c-article-card__date, .entry-date, .news-meta, time");
                dateRaw = dateEl?.TextContent.Trim() ?? dateEl?.GetAttribute("datetime");
            }

            // Category / status
            if (!string.IsNullOrWhiteSpace(source.Fields.Category))
            {
                var catEl = el.QuerySelector(source.Fields.Category);
                category = catEl?.TextContent.Trim();
            }
            else
            {
                var catEl = el.QuerySelector(".c-article-card__label, .news-meta, .status, .category, .label");
                category = catEl?.TextContent.Trim();
            }

            // Edge: for split's label that contains category
            title = title?.Trim();
            if (string.IsNullOrWhiteSpace(title)) continue; // skip empty

            row["Title"] = title;
            if (!string.IsNullOrWhiteSpace(description)) row["Description"] = description;
            if (!string.IsNullOrWhiteSpace(link)) row["Link"] = link;
            if (!string.IsNullOrWhiteSpace(dateRaw)) row["Date"] = dateRaw;
            if (!string.IsNullOrWhiteSpace(category)) row["Category"] = category;

            // Keep raw element html for debugging?
            rows.Add(row);
        }
        return rows;
    }

    private async Task UpsertAlertsAsync(AlertSourceOptions source, List<ExtractedRow> rows, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
        var matcherLogger = scope.ServiceProvider.GetRequiredService<ILogger<AlertRouteMatcher>>();
        var matcher = new AlertRouteMatcher(db, matcherLogger);

        // Resolve operator id if needed – uses config's OperatorSlug, no hardcoded per-operator C#.
        int? operatorId = source.OperatorId;
        if (operatorId is null && !string.IsNullOrWhiteSpace(source.OperatorSlug))
        {
            var op = await db.Operators.AsNoTracking()
                .FirstOrDefaultAsync(o => o.GlobalId == "gt-" + source.OperatorSlug || o.ShortName == source.OperatorSlug, ct);
            if (op is not null) operatorId = op.Id;
        }

        var existing = await db.Alerts.Where(a => a.SourceKey != null && a.SourceKey.StartsWith(source.Id + ":")).ToListAsync(ct);
        var byKey = existing.ToDictionary(a => a.SourceKey!);

        var now = DateTime.UtcNow;
        var seenKeys = new HashSet<string>();
        var toAdd = new List<Entities.Alert>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = AlertSourceMapper.BuildSourceKey(source.Id, row, i);
            seenKeys.Add(key);

            var (title, description, linkRaw, dateRaw, category) = AlertSourceMapper.ExtractCommon(row);
            if (string.IsNullOrWhiteSpace(title))
                title = row.TryGetValue("Title", out var t) ? t?.ToString() : "Untitled";

            var link = AlertSourceMapper.ResolveUrl(linkRaw, source.Url.Split(';')[0]);
            var severity = AlertSourceMapper.MapSeverity(category, title, description);
            var dateParsed = AlertSourceMapper.ParseDate(dateRaw);
            var (lat, lon, geoJson) = AlertSourceMapper.ExtractGeometry(row);
            // For Road alerts, geometry was extracted; for Transit alerts lat/lon stays null.

            // For HAK sources, title fallback to description category
            if (source.Kind.Equals("Road", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(title))
                title = description ?? category ?? "Road disruption";

            // Road alerts (HAK) carry no operator and describe roads, not transit lines. Matching them
            // would compare a road notice against every operator's route short names and attach an
            // arbitrary one, which would then reach OTP as a disruption on an unrelated route.
            var matchedRouteIds = source.Kind.Equals("Road", StringComparison.OrdinalIgnoreCase)
                ? null
                : await matcher.MatchAsync(title, description, operatorId, ct);
            var effect = AlertSourceMapper.DetermineEffect(title, description);

            if (byKey.TryGetValue(key, out var existingAlert))
            {
                existingAlert.HeaderText = title;
                existingAlert.DescriptionText = description;
                existingAlert.Url = link;
                existingAlert.SourceUrl = link;
                existingAlert.FetchedAt = now;
                existingAlert.SourceKey = key;
                existingAlert.Kind = source.Kind;
                existingAlert.Severity = severity;
                existingAlert.Latitude = lat;
                existingAlert.Longitude = lon;
                existingAlert.GeometryGeoJson = geoJson;
                existingAlert.MatchedRouteIds = matchedRouteIds;
                existingAlert.Cause = "UNKNOWN_CAUSE";
                existingAlert.Effect = effect;
                existingAlert.ActivePeriodStart = dateParsed ?? existingAlert.ActivePeriodStart ?? now;
                // Keep OperatorId if already set or use inferred
                if (operatorId.HasValue) existingAlert.OperatorId = operatorId;
            }
            else
            {
                var alert = new Entities.Alert
                {
                    FeedId = null,
                    OperatorId = operatorId,
                    HeaderText = title,
                    DescriptionText = description,
                    Url = link,
                    SourceUrl = link,
                    Cause = "UNKNOWN_CAUSE",
                    Effect = effect,
                    ActivePeriodStart = dateParsed ?? now,
                    ActivePeriodEnd = null,
                    FetchedAt = now,
                    CreatedAt = now,
                    Kind = source.Kind,
                    SourceKey = key,
                    Severity = severity,
                    Latitude = lat,
                    Longitude = lon,
                    GeometryGeoJson = geoJson,
                    MatchedRouteIds = matchedRouteIds
                };
                toAdd.Add(alert);
            }
        }

        if (toAdd.Count > 0)
            db.Alerts.AddRange(toAdd);

        // Sweep: remove alerts of this source no longer present
        var toRemove = existing.Where(a => !seenKeys.Contains(a.SourceKey!)).ToList();
        if (toRemove.Count > 0)
            db.Alerts.RemoveRange(toRemove);

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Alert source {SourceId}: upserted {Added} new, updated {Updated}, removed {Removed}", source.Id, toAdd.Count, existing.Count - toRemove.Count, toRemove.Count);
    }


}
