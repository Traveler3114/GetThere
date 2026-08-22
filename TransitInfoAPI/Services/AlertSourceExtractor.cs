using System.Text;

using AngleSharp.Html.Parser;

using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Services;

/// <summary>
/// Fetches an alert source and turns its markup into rows. Split out of AlertPollingWorker so the
/// admin console's preview endpoint runs exactly the same extraction the poller does — a selector
/// that previews correctly is a selector that will poll correctly.
/// </summary>
public sealed class AlertSourceExtractor
{
    private readonly ILogger<AlertSourceExtractor> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;

    public AlertSourceExtractor(
        ILogger<AlertSourceExtractor> logger,
        IHttpClientFactory httpFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Runs every URL on the source and returns the merged rows plus any extraction warnings.
    /// Never throws for a selector that matched nothing — a drifted selector is a warning, because
    /// throwing here would trip the caller's failure handling for what is a content change.
    /// </summary>
    public async Task<(List<ExtractedRow> Rows, List<string> Warnings)> ExtractAsync(
        AlertSource source, CancellationToken ct)
    {
        var urls = source.Url.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0) urls = [source.Url];

        var allRows = new List<ExtractedRow>();
        var extractionResult = new ExtractionResult();

        foreach (var url in urls)
        {
            var body = await FetchBodyAsync(url, ct);
            if (string.IsNullOrEmpty(body))
            {
                _logger.LogWarning("Alert source {SourceKey} fetched empty body from {Url}", source.SourceKey, url);
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
                _logger.LogWarning("Alert source {SourceKey} yielded 0 items from {Url} (selector '{Selector}')", source.SourceKey, url, source.ItemSelector);
                foreach (var w in extractionResult.Warnings)
                    _logger.LogWarning("Alert source {SourceKey} warning: {Warning}", source.SourceKey, w);
            }
            allRows.AddRange(rows);
        }

        return (allRows, extractionResult.Warnings);
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

    private static List<ExtractedRow> ParseHtmlAlerts(string body, AlertSource source, ExtractionResult result)
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
            if (!string.IsNullOrWhiteSpace(source.TitleSelector))
            {
                var titleEl = el.QuerySelector(source.TitleSelector);
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
            if (!string.IsNullOrWhiteSpace(source.DescriptionSelector))
            {
                var descEl = el.QuerySelector(source.DescriptionSelector);
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
            if (!string.IsNullOrWhiteSpace(source.LinkSelector))
            {
                var linkEl = el.QuerySelector(source.LinkSelector);
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
            if (!string.IsNullOrWhiteSpace(source.DateSelector))
            {
                var dateEl = el.QuerySelector(source.DateSelector);
                dateRaw = dateEl?.TextContent.Trim() ?? dateEl?.GetAttribute("datetime");
            }
            else
            {
                var dateEl = el.QuerySelector(".date, .c-article-card__date, .entry-date, .news-meta, time");
                dateRaw = dateEl?.TextContent.Trim() ?? dateEl?.GetAttribute("datetime");
            }

            // Category / status
            if (!string.IsNullOrWhiteSpace(source.CategorySelector))
            {
                var catEl = el.QuerySelector(source.CategorySelector);
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
}
