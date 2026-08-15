using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using AngleSharp.Html.Parser;

using CsvHelper;
using CsvHelper.Configuration;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services;

/// <summary>A flat row of extracted values, before it becomes a typed record.</summary>
public sealed class ExtractedRow : Dictionary<string, object?>
{
    public ExtractedRow() : base(StringComparer.OrdinalIgnoreCase) { }
}

public sealed class ExtractionResult
{
    public List<ExtractedRow> Rows { get; } = [];
    public List<string> Log { get; } = [];
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// Fetches and flattens one <see cref="CustomSourceRequest"/> into rows, then maps those rows onto
/// target fields.
/// <para>
/// Deliberately knows nothing about transit: it turns a paginated HTTP endpoint into a list of
/// dictionaries and applies the configured mappings. Turning those dictionaries into stops, routes
/// and trips is <see cref="CustomHttpSource"/>'s job, which keeps the fiddly parts — pagination,
/// auth, data-path traversal — testable without any transit model in the way.
/// </para>
/// </summary>
public partial class CustomSourceEngine
{
    private const int MaxPages = 500;
    private const int MaxRows = 500_000;

    /// <summary>
    /// Ceiling on a single response body.
    /// <para>
    /// These reads were <c>ReadAsStringAsync</c> with no bound at all, in a loop of up to
    /// <see cref="MaxPages"/> pages — a hostile or merely broken operator endpoint could hand back
    /// an endless body and exhaust the server's memory. <see cref="ExternalFeedSource"/> has capped
    /// its downloads at 512 MB since it was written; this path never did.
    /// </para>
    /// <para>
    /// Much smaller than the feed ceiling on purpose: these are JSON/CSV/XML pages of rows, not
    /// whole GTFS archives, and 32 MB of any of them is already far past what a page should carry.
    /// </para>
    /// </summary>
    private const long MaxResponseBytes = 32L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CustomSourceEngine> _logger;
    private readonly bool _allowPrivateNetworkUrls;
    private readonly string _contentRootPath;

    public CustomSourceEngine(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<CustomSourceEngine> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _contentRootPath = env.ContentRootPath;
        _allowPrivateNetworkUrls = configuration.GetValue("Feeds:AllowPrivateNetworkUrls", false);
    }

    /// <param name="maxRows">Caps extraction for preview, where a full pull is wasted work.</param>
    public async Task<ExtractionResult> ExecuteAsync(
        CustomSourceRequest request,
        string? authConfig,
        int? maxRows = null,
        CancellationToken ct = default)
    {
        var result = new ExtractionResult();
        var limit = maxRows ?? MaxRows;

        // An uploaded artifact is read from disk instead of fetched. It goes through the same
        // mapping, deduplication and completion afterwards, so a spreadsheet an operator emails over
        // is configured exactly like an endpoint they host.
        if (request.Url.StartsWith(UploadScheme, StringComparison.OrdinalIgnoreCase))
        {
            // Capped like the HTTP path. This return used to skip the limit entirely, so a preview
            // of an upload-backed source ran the full file through mapping and completion instead of
            // the 200 rows it asked for — and MaxRows never applied to an uploaded file at all.
            var uploaded = ReadUploadedRows(request, result);
            if (uploaded.Count > limit)
            {
                result.Log.Add($"{request.TargetSection}: reached the {limit:N0} row cap");
                uploaded = uploaded.GetRange(0, limit);
            }

            result.Rows.AddRange(uploaded);
            return result;
        }

        var pagination = ParsePagination(request.PaginationConfig);

        var page = 0;
        string? cursor = null;
        var totalSoFar = 0;

        while (page < MaxPages)
        {
            var url = ApplyPagination(request.Url, pagination, page, totalSoFar, cursor);

            // The SSRF check now lives inside SendFollowingRedirectsAsync, which applies it to this
            // URL and to every redirect hop after it — the hops being the part a pre-flight check
            // here could never cover.
            var http = _httpFactory.CreateClient("customsource");
            using var response = await SendFollowingRedirectsAsync(
                http, request.HttpMethod, url, authConfig, result, request.TargetSection.ToString(), ct);

            // Null means the hop chain was refused and the reason is already in Warnings.
            if (response is null) break;

            if (!response.IsSuccessStatusCode)
            {
                result.Warnings.Add($"{request.TargetSection}: {(int)response.StatusCode} from {url}");
                break;
            }

            string body;
            try
            {
                body = await ReadCappedAsync(response, ct);
            }
            catch (Exceptions.AppException ex)
            {
                result.Warnings.Add($"{request.TargetSection}: {ex.Message}");
                break;
            }

            result.Log.Add($"{request.TargetSection}: page {page + 1}, {body.Length:N0} chars");

            var pageRows = request.Format switch
            {
                CustomSourceFormat.Json => ParseJsonRows(body, request.DataPath, result),
                CustomSourceFormat.Csv => ParseCsvRows(body, result),
                CustomSourceFormat.Xml => ParseXmlRows(body, request.DataPath, result),
                CustomSourceFormat.Html => ParseHtmlRows(body, request.DataPath, result),
                _ => throw new Exceptions.AppException(
                    $"Format {request.Format} cannot be read from an HTTP response — {request.Format} sources are uploaded, not polled.",
                    400, "UNSUPPORTED_FORMAT")
            };

            if (pageRows.Count == 0)
            {
                result.Log.Add($"{request.TargetSection}: page {page + 1} returned no rows, stopping");
                break;
            }

            result.Rows.AddRange(pageRows);
            totalSoFar = result.Rows.Count;

            if (result.Rows.Count >= limit)
            {
                result.Log.Add($"{request.TargetSection}: reached the {limit:N0} row cap");
                break;
            }

            if (pagination.Kind == PaginationKind.None) break;

            cursor = pagination.Kind == PaginationKind.Cursor ? ReadCursor(body, pagination) : null;
            if (pagination.Kind == PaginationKind.Cursor && string.IsNullOrEmpty(cursor)) break;
            if (pagination.PageSize > 0 && pageRows.Count < pagination.PageSize) break;

            page++;
        }

        if (page >= MaxPages)
            result.Warnings.Add($"{request.TargetSection}: stopped at the {MaxPages}-page ceiling");

        return result;
    }

    /// <summary>
    /// Fetches a single response body untouched, for structure discovery.
    /// <para>
    /// Discovery cannot go through <see cref="ExecuteAsync"/>: that returns rows, and finding out
    /// where the rows <em>are</em> is the whole question. Reusing the row parser here answered with
    /// the top-level envelope keys instead of the array beneath them.
    /// </para>
    /// </summary>
    public async Task<string> FetchRawAsync(CustomSourceRequest request, string? authConfig, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("customsource");
        var discovery = new ExtractionResult();

        using var response = await SendFollowingRedirectsAsync(
            http, request.HttpMethod, request.Url, authConfig, discovery, "discover", ct);

        if (response is null)
            throw new Exceptions.AppException(discovery.Warnings[^1], 502, "REDIRECTED_OFF_HOST");

        response.EnsureSuccessStatusCode();
        return await ReadCappedAsync(response, ct);
    }

    /// <summary>
    /// Reads a response body, aborting past <see cref="MaxResponseBytes"/> so a lying or absent
    /// Content-Length cannot exhaust memory.
    /// </summary>
    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new Exceptions.AppException(
                $"Response exceeds the {MaxResponseBytes / (1024 * 1024)} MB limit.", 413, "RESPONSE_TOO_LARGE");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
                throw new Exceptions.AppException(
                    $"Response exceeds the {MaxResponseBytes / (1024 * 1024)} MB limit.", 413, "RESPONSE_TOO_LARGE");

            buffer.Write(chunk, 0, read);
        }

        // The charset the server declared, falling back to UTF-8. Reading bytes rather than letting
        // HttpContent decode is what makes the cap enforceable, so the decode happens here instead.
        var encoding = Encoding.UTF8;
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset.Trim('"')); }
            catch (ArgumentException) { /* unknown charset — UTF-8 is the better guess than failing */ }
        }

        return encoding.GetString(buffer.ToArray());
    }

    /// <summary>How many redirect hops one request may take before it is abandoned.</summary>
    /// <remarks>
    /// <c>SocketsHttpHandler</c>'s own default is 50. Five is plenty for the "moved to /v2" and
    /// "http to https" cases that actually occur, and every extra hop is another chance for a chain
    /// to wander somewhere the operator never configured.
    /// </remarks>
    private const int MaxRedirects = 5;

    /// <summary>
    /// Sends a request and follows redirects by hand, deciding at each hop whether the credential
    /// travels with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the check it replaced ran too late to do anything. That version let
    /// <c>SendAsync</c> follow redirects itself and then compared the requested host with
    /// <c>response.RequestMessage.RequestUri.Host</c>, refusing the response if they differed. The
    /// comparison was right and the conclusion was wrong: by the time it ran, the handler had
    /// already performed the redirected request. <c>HttpClient</c> strips <c>Authorization</c> when
    /// a redirect crosses origins, but it strips nothing else, and <see cref="ApplyAuth"/>'s
    /// <c>header</c> mode sets an arbitrary header name — an API key, in practice. Refusing the body
    /// prevented ingesting the wrong data; it did not stop the key being handed over. The credential
    /// was already gone.
    /// </para>
    /// <para>
    /// The "customsource" client therefore sets <c>AllowAutoRedirect = false</c>, and each hop is
    /// issued here, where the decision can be made <em>before</em> the request goes out:
    /// </para>
    /// <list type="bullet">
    /// <item>Every hop is re-checked against the SSRF guard, so a redirect cannot walk into the
    /// server's own network. (The handler's <c>ConnectCallback</c> also covers this; the explicit
    /// check gives the operator a comprehensible message instead of a connect failure.)</item>
    /// <item>A credentialed request is <b>refused</b> the moment a hop would leave the origin the
    /// operator configured — a different host, an https source dropping to plain http, or a
    /// different port on the same machine — and the credential is never sent. The one exception is
    /// the plain http→https upgrade, which only improves matters.</item>
    /// <item>An unauthenticated source may redirect wherever it likes — there is nothing to leak,
    /// and public feed URLs move around.</item>
    /// </list>
    /// <para>
    /// Returns <c>null</c> when the chain was refused or ran past <see cref="MaxRedirects"/>, having
    /// already recorded why in <paramref name="result"/>'s warnings. Otherwise the caller owns the
    /// returned response and must dispose it.
    /// </para>
    /// </remarks>
    private async Task<HttpResponseMessage?> SendFollowingRedirectsAsync(
        HttpClient http, string httpMethod, string url, string? authConfig,
        ExtractionResult result, string section, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var origin))
            throw new Exceptions.AppException("Source URL must be an absolute HTTP(S) URL.", 400, "INVALID_FEED_URL");

        var credentialed = !string.IsNullOrWhiteSpace(authConfig);
        var current = origin;
        var method = new HttpMethod(httpMethod);

        for (var hop = 0; ; hop++)
        {
            if (!_allowPrivateNetworkUrls)
                ExternalFeedSource.EnsurePublicDestination(current.AbsoluteUri);

            using var message = new HttpRequestMessage(method, current);
            ApplyAuth(message, authConfig);

            // ResponseHeadersRead so ReadCappedAsync's ceiling is enforced while the body streams in.
            // Under the default (ResponseContentRead) HttpClient buffers the whole body first, which
            // is the same mistake this method is here to fix: a guard that runs after the fact.
            var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);

            var location = response.Headers.Location;
            if (!IsRedirect(response.StatusCode) || location is null)
                return response;

            var status = response.StatusCode;
            response.Dispose();

            if (hop >= MaxRedirects)
            {
                result.Warnings.Add(
                    $"{section}: gave up after {MaxRedirects} redirects starting at {origin.Host}.");
                return null;
            }

            if (!Uri.TryCreate(current, location, out var next)
                || next.Scheme is not ("http" or "https"))
            {
                result.Warnings.Add(
                    $"{section}: {(int)status} from {current.Host} pointed at something that is not an "
                    + "HTTP(S) URL — stopping.");
                return null;
            }

            if (credentialed && !string.Equals(next.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(
                    $"{section}: {current.Host} redirected to {next.Host} and this source carries a credential — "
                    + "refusing to send it off-host. Point the source at its final address instead.");
                return null;
            }

            if (credentialed && origin.Scheme == "https" && next.Scheme == "http")
            {
                result.Warnings.Add(
                    $"{section}: {current.Host} redirected from https to http and this source carries a "
                    + "credential — refusing to send it in clear.");
                return null;
            }

            // Same host, same-or-better scheme, different port is still a different origin: ports on
            // one machine are different services, and the operator configured one of them. The single
            // exception is the plain http→https upgrade, where 80 becomes 443 precisely because the
            // scheme improved.
            var schemeUpgrade = origin.Scheme == "http" && next.Scheme == "https"
                && origin.IsDefaultPort && next.IsDefaultPort;

            if (credentialed && next.Port != origin.Port && !schemeUpgrade)
            {
                result.Warnings.Add(
                    $"{section}: {current.Host} redirected to port {next.Port} and this source carries a "
                    + $"credential — refusing to send it to a port other than {origin.Port}.");
                return null;
            }

            // 303 always becomes a GET; 301 and 302 do too for anything that was not GET or HEAD,
            // which is what every agent does in practice and what SocketsHttpHandler did before this
            // method took the redirect over. 307 and 308 preserve the method by definition.
            var bodylessMethod = method == HttpMethod.Get || method == HttpMethod.Head;
            if (status == HttpStatusCode.SeeOther
                || ((status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found) && !bodylessMethod))
            {
                method = HttpMethod.Get;
            }

            current = next;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Drops rows repeating a value in <paramref name="distinctBy"/>.
    /// <para>
    /// Runs on <em>mapped</em> rows, so <c>DistinctBy</c> names a target field like <c>StopId</c> —
    /// the stable vocabulary an admin already sees in the mapping editor, rather than whatever the
    /// operator happened to call it. Applying it to source rows instead silently collapsed every
    /// page to a single row, because the target name matched nothing and every key resolved empty.
    /// </para>
    /// </summary>
    public static List<ExtractedRow> Deduplicate(List<ExtractedRow> rows, string? distinctBy, out int dropped)
    {
        dropped = 0;
        if (string.IsNullOrWhiteSpace(distinctBy)) return rows;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<ExtractedRow>(rows.Count);

        foreach (var row in rows)
        {
            // A row with no value for the key is kept rather than folded in with every other blank:
            // losing rows is worse than keeping an ambiguous one, and the import's own validation
            // rejects it later if it really is unusable.
            if (!row.TryGetValue(distinctBy, out var value) || value is null)
            {
                kept.Add(row);
                continue;
            }

            if (seen.Add(value.ToString() ?? string.Empty)) kept.Add(row);
        }

        dropped = rows.Count - kept.Count;
        return kept;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Applies a request's mappings, turning source-shaped rows into target-shaped ones.</summary>
    public static List<ExtractedRow> ApplyMappings(IReadOnlyList<ExtractedRow> rows, IReadOnlyList<CustomSourceMapping> mappings)
    {
        var mapped = new List<ExtractedRow>(rows.Count);

        foreach (var row in rows)
        {
            var target = new ExtractedRow();
            foreach (var m in mappings.OrderBy(m => m.SortOrder))
                target[m.TargetField] = ApplyMapping(row, m);
            mapped.Add(target);
        }

        return mapped;
    }

    private static object? ApplyMapping(ExtractedRow row, CustomSourceMapping mapping)
    {
        if (mapping.Kind == MappingKind.Static)
            return mapping.SourceExpression;

        var raw = Resolve(row, mapping.SourceExpression);

        return mapping.Kind switch
        {
            MappingKind.Direct => raw,
            MappingKind.TimeExtract => ExtractTime(raw?.ToString()),
            MappingKind.DateExtract => ExtractDate(raw?.ToString()),
            MappingKind.Expression => EvaluateExpression(row, mapping.SourceExpression),
            _ => raw
        };
    }

    /// <summary>
    /// Concatenation only — <c>{a} - {b}</c>. Kept intentionally small: the moment this grows
    /// conditionals and arithmetic it is a programming language stored in a database column, and the
    /// named C# extractor exists precisely so it does not have to become one.
    /// </summary>
    private static string EvaluateExpression(ExtractedRow row, string expression) =>
        PlaceholderPattern().Replace(expression, m => Resolve(row, m.Groups[1].Value)?.ToString() ?? string.Empty);

    private static object? Resolve(ExtractedRow row, string path)
    {
        if (row.TryGetValue(path, out var direct)) return direct;

        // Flattened rows use dotted keys, so a nested path resolves without re-walking the JSON.
        foreach (var kvp in row)
        {
            if (kvp.Key.EndsWith("." + path, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    /// <summary>Normalizes a time to GTFS <c>HH:MM:SS</c>, keeping hours past 24 intact.</summary>
    internal static string? ExtractTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var match = TimePattern().Match(value);
        if (!match.Success) return null;

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = match.Groups[2].Value;
        var seconds = match.Groups[3].Success ? match.Groups[3].Value : "00";
        return $"{hours:00}:{minutes}:{seconds}";
    }

    /// <summary>Normalizes a date to GTFS <c>yyyyMMdd</c>.</summary>
    internal static string? ExtractDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var match = DatePattern().Match(value);
        if (match.Success)
            return $"{match.Groups[1].Value}{match.Groups[2].Value}{match.Groups[3].Value}";

        return CompactDatePattern().Match(value) is { Success: true } compact ? compact.Groups[1].Value : null;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks <paramref name="dataPath"/> to the array of rows, then flattens each element to dotted
    /// keys so a mapping can name a nested field without any traversal syntax of its own.
    /// </summary>
    internal static List<ExtractedRow> ParseJsonRows(string body, string dataPath, ExtractionResult result)
    {
        var rows = new List<ExtractedRow>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            result.Warnings.Add($"Response is not valid JSON: {ex.Message}");
            return rows;
        }

        using (doc)
        {
            var node = doc.RootElement;

            foreach (var segment in (dataPath ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(segment, out var child))
                {
                    node = child;
                }
                else
                {
                    result.Warnings.Add($"Data path segment '{segment}' not found in response");
                    return rows;
                }
            }

            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in node.EnumerateArray())
                {
                    var row = new ExtractedRow();
                    Flatten(element, string.Empty, row);
                    rows.Add(row);
                }
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                // Some endpoints key rows by id instead of listing them.
                foreach (var property in node.EnumerateObject())
                {
                    var row = new ExtractedRow { ["_key"] = property.Name };
                    Flatten(property.Value, string.Empty, row);
                    rows.Add(row);
                }
            }
            else
            {
                result.Warnings.Add($"Data path resolved to {node.ValueKind}, which holds no rows");
            }
        }

        return rows;
    }

    private static void Flatten(JsonElement element, string prefix, ExtractedRow row)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", row);
                break;

            case JsonValueKind.Array:
                // Arrays inside a row are left as raw JSON: nothing downstream consumes them, and
                // exploding them would multiply the row count silently.
                row[prefix] = element.GetRawText();
                break;

            default:
                row[prefix] = ToScalar(element);
                break;
        }
    }

    private static object? ToScalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    internal static List<ExtractedRow> ParseCsvRows(string body, ExtractionResult result)
    {
        var rows = new List<ExtractedRow>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        };

        using var reader = new StringReader(body);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader())
        {
            result.Warnings.Add("CSV response has no header row");
            return rows;
        }

        var headers = csv.HeaderRecord ?? [];
        while (csv.Read())
        {
            var row = new ExtractedRow();
            foreach (var header in headers)
            {
                if (!string.IsNullOrWhiteSpace(header))
                    row[header] = csv.GetField(header);
            }
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>URL scheme marking a request that reads an uploaded file rather than an endpoint.</summary>
    public const string UploadScheme = "upload://";

    /// <summary>
    /// Reads rows out of a file an admin uploaded for this source.
    /// <para>
    /// The file name is taken from the URL after <see cref="UploadScheme"/> and resolved through
    /// <see cref="CustomSourceStorage"/>, which refuses anything that would escape the source's own
    /// directory — the name reaches here from an admin-supplied request row, so it is untrusted
    /// input on a path.
    /// </para>
    /// </summary>
    private List<ExtractedRow> ReadUploadedRows(CustomSourceRequest request, ExtractionResult result)
    {
        var fileName = request.Url[UploadScheme.Length..];
        var storage = new CustomSourceStorage(_contentRootPath);

        string path;
        try
        {
            path = storage.FilePathFor(request.CustomSourceId, fileName);
        }
        catch (Exceptions.AppException ex)
        {
            result.Warnings.Add($"{request.TargetSection}: {ex.Message}");
            return [];
        }

        if (!File.Exists(path))
        {
            result.Warnings.Add($"{request.TargetSection}: uploaded file '{fileName}' is missing — upload it again");
            return [];
        }

        var content = File.ReadAllBytes(path);
        result.Log.Add($"{request.TargetSection}: read {content.Length:N0} bytes from uploaded {fileName}");

        var rows = request.Format switch
        {
            CustomSourceFormat.Xlsx => DocumentTableReader.ReadXlsx(content, result.Warnings),
            CustomSourceFormat.Pdf => DocumentTableReader.ReadPdf(content, result.Warnings),
            CustomSourceFormat.Csv => DocumentTableReader.ReadCsv(content, result.Warnings),
            CustomSourceFormat.Json => ParseJsonRows(Encoding.UTF8.GetString(content), request.DataPath, result)
                .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList(),
            CustomSourceFormat.Xml => ParseXmlRows(Encoding.UTF8.GetString(content), request.DataPath, result)
                .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList(),
            CustomSourceFormat.Html => ParseHtmlRows(Encoding.UTF8.GetString(content), request.DataPath, result)
                .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList(),
            _ => []
        };

        result.Log.Add($"{request.TargetSection}: {rows.Count:N0} row(s) read from file");

        return [.. rows.Select(source =>
        {
            var row = new ExtractedRow();
            foreach (var kv in source) row[kv.Key] = kv.Value;
            return row;
        })];
    }

    /// <summary>
    /// Flattens the elements at <paramref name="dataPath"/> into rows.
    /// <para>
    /// Attributes and child elements land in the same flat namespace, so a mapping refers to
    /// <c>name</c> whether the operator wrote it as an attribute or a nested element — a
    /// distinction their XML makes and their timetable does not. Nested children are exposed
    /// dotted, so <c>position/lat</c> is addressable as <c>position.lat</c>, matching how the JSON
    /// parser flattens the same shape.
    /// </para>
    /// </summary>
    internal static List<ExtractedRow> ParseXmlRows(string body, string dataPath, ExtractionResult result)
    {
        var rows = new List<ExtractedRow>();

        XDocument doc;
        try
        {
            // DtdProcessing.Prohibit is the default for XDocument.Parse, which is what keeps this
            // from being an XXE vector on operator-controlled input.
            doc = XDocument.Parse(body, LoadOptions.None);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"XML response could not be parsed: {ex.Message}");
            return rows;
        }

        if (doc.Root is null) return rows;

        // The path names the repeating element. Empty means "every direct child of the root",
        // which is the shape most operator feeds use.
        IEnumerable<XElement> elements = string.IsNullOrWhiteSpace(dataPath)
            ? doc.Root.Elements()
            : dataPath.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Aggregate((IEnumerable<XElement>)[doc.Root], (current, segment) =>
                    current.SelectMany(e => e.Elements().Where(c =>
                        string.Equals(c.Name.LocalName, segment, StringComparison.OrdinalIgnoreCase))));

        var truncated = false;
        foreach (var element in elements)
        {
            var row = new ExtractedRow();
            truncated |= FlattenXml(element, row, string.Empty, depth: 0);
            if (row.Count > 0) rows.Add(row);
        }

        if (truncated)
        {
            result.Warnings.Add(
                $"XML nested deeper than {MaxXmlDepth} levels; the levels below that were not flattened into keys");
        }

        return rows;
    }

    /// <summary>
    /// How far <see cref="FlattenXml"/> follows nesting before it stops.
    /// <para>
    /// <see cref="FlattenXml"/> recurses once per level, and nothing upstream bounds how deep an
    /// operator's XML goes: <c>XmlReaderSettings</c> has no depth limit to set, and
    /// <c>XDocument.Parse</c> builds its tree iteratively, so it happily returns a document nested
    /// tens of thousands deep. Recursing that far overflows the stack, and a
    /// <c>StackOverflowException</c> cannot be caught — it takes the process down, which on a polled
    /// source means one broken operator endpoint stops every feed.
    /// </para>
    /// <para>
    /// A document that reaches the ceiling costs about 40 KB, far under
    /// <see cref="MaxResponseBytes"/>, so the size cap is no defence here.
    /// </para>
    /// <para>
    /// 64 to match <c>JsonDocument</c>'s own default maximum depth, which is what has been quietly
    /// protecting <see cref="Flatten"/> on the JSON path all along. Real operator XML is nowhere
    /// near it — even NeTEx, the deepest schema in this domain, is well under 32.
    /// </para>
    /// </summary>
    private const int MaxXmlDepth = 64;

    /// <returns><c>true</c> if nesting below <see cref="MaxXmlDepth"/> was left unread.</returns>
    private static bool FlattenXml(XElement element, ExtractedRow row, string prefix, int depth)
    {
        if (depth >= MaxXmlDepth) return true;

        var truncated = false;

        foreach (var attr in element.Attributes())
            row[prefix + attr.Name.LocalName] = attr.Value;

        foreach (var child in element.Elements())
        {
            var key = prefix + child.Name.LocalName;
            if (child.HasElements || child.HasAttributes)
                truncated |= FlattenXml(child, row, key + ".", depth + 1);

            if (!child.HasElements)
                row[key] = child.Value.Trim();
        }

        return truncated;
    }

    /// <summary>
    /// Reads an HTML table into rows, keyed by its header cells.
    /// <para>
    /// <paramref name="dataPath"/> is a CSS selector picking the table — operator pages routinely
    /// carry several, and "the first one" is wrong as often as it is right. Empty selects the first
    /// table on the page.
    /// </para>
    /// </summary>
    internal static List<ExtractedRow> ParseHtmlRows(string body, string dataPath, ExtractionResult result)
    {
        var rows = new List<ExtractedRow>();

        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(body);

        var selector = string.IsNullOrWhiteSpace(dataPath) ? "table" : dataPath;
        AngleSharp.Dom.IElement? table;
        try
        {
            table = doc.QuerySelector(selector);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"HTML selector '{selector}' is not valid: {ex.Message}");
            return rows;
        }

        if (table is null)
        {
            result.Warnings.Add($"No element matched HTML selector '{selector}'");
            return rows;
        }

        var headerCells = table.QuerySelectorAll("thead th, thead td");
        var headers = headerCells.Select(c => c.TextContent.Trim()).Where(h => h.Length > 0).ToList();

        var bodyRows = table.QuerySelectorAll("tbody tr");
        if (bodyRows.Length == 0) bodyRows = table.QuerySelectorAll("tr");

        foreach (var tr in bodyRows)
        {
            var cells = tr.QuerySelectorAll("td");
            if (cells.Length == 0) continue;

            // A table with no <thead> puts its headers in the first row of cells. Falling back to
            // positional keys keeps such a table usable rather than dropping it.
            if (headers.Count == 0)
            {
                headers = [.. Enumerable.Range(0, cells.Length).Select(i => "col" + i)];
            }

            var row = new ExtractedRow();
            for (var i = 0; i < cells.Length; i++)
            {
                var key = i < headers.Count ? headers[i] : "col" + i;
                row[key] = cells[i].TextContent.Trim();
            }
            rows.Add(row);
        }

        if (rows.Count == 0)
            result.Warnings.Add($"Element matched by '{selector}' has no data rows");

        return rows;
    }

    // ── Pagination ────────────────────────────────────────────────────────────────────────────

    internal sealed record PaginationConfig(
        PaginationKind Kind,
        string Parameter,
        int PageSize,
        int StartAt,
        string? CursorPath,
        string? CursorParameter);

    internal static PaginationConfig ParsePagination(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new PaginationConfig(PaginationKind.None, "page", 0, 0, null, null);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var kind = root.TryGetProperty("type", out var t)
                && Enum.TryParse<PaginationKind>(t.GetString(), true, out var parsed)
                    ? parsed
                    : PaginationKind.None;

            return new PaginationConfig(
                kind,
                root.TryGetProperty("parameter", out var p) ? p.GetString() ?? "page" : "page",
                root.TryGetProperty("pageSize", out var s) && s.TryGetInt32(out var size) ? size : 0,
                root.TryGetProperty("startAt", out var st) && st.TryGetInt32(out var start) ? start : 0,
                root.TryGetProperty("cursorPath", out var cp) ? cp.GetString() : null,
                root.TryGetProperty("cursorParameter", out var cq) ? cq.GetString() : null);
        }
        catch (JsonException)
        {
            return new PaginationConfig(PaginationKind.None, "page", 0, 0, null, null);
        }
    }

    internal static string ApplyPagination(string url, PaginationConfig config, int page, int totalSoFar, string? cursor)
    {
        if (config.Kind == PaginationKind.None) return url;

        var value = config.Kind switch
        {
            PaginationKind.Page => (config.StartAt + page).ToString(CultureInfo.InvariantCulture),
            PaginationKind.Offset => totalSoFar.ToString(CultureInfo.InvariantCulture),
            PaginationKind.Cursor => cursor ?? string.Empty,
            _ => string.Empty
        };

        if (config.Kind == PaginationKind.Cursor && string.IsNullOrEmpty(value))
            return url;

        var parameter = config.Kind == PaginationKind.Cursor
            ? config.CursorParameter ?? config.Parameter
            : config.Parameter;

        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(parameter)}={Uri.EscapeDataString(value)}";
    }

    private static string? ReadCursor(string body, PaginationConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.CursorPath)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var node = doc.RootElement;
            foreach (var segment in config.CursorPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(segment, out var child))
                    return null;
                node = child;
            }
            return node.ValueKind == JsonValueKind.String ? node.GetString() : node.GetRawText().Trim('"');
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Auth ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the configured credential. Only the non-interactive schemes are here; anything
    /// needing a token exchange belongs in a named extractor, where it can hold state and be tested.
    /// </summary>
    private void ApplyAuth(HttpRequestMessage message, string? authConfigJson)
    {
        if (string.IsNullOrWhiteSpace(authConfigJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(authConfigJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            switch (type?.ToLowerInvariant())
            {
                case "basic":
                    var user = root.GetProperty("username").GetString();
                    var pass = root.GetProperty("password").GetString();
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                    message.Headers.TryAddWithoutValidation("Authorization", $"Basic {encoded}");
                    break;

                case "bearer":
                    message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {root.GetProperty("token").GetString()}");
                    break;

                case "header":
                    message.Headers.TryAddWithoutValidation(
                        root.GetProperty("name").GetString()!,
                        root.GetProperty("value").GetString());
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning("Auth config could not be applied: {Message}", ex.Message);
        }
    }

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"(\d{1,2}):(\d{2})(?::(\d{2}))?")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"(\d{4})-(\d{2})-(\d{2})")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b(\d{8})\b")]
    private static partial Regex CompactDatePattern();
}
