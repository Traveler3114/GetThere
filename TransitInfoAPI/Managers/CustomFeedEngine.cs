using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.XPath;

using CsvHelper;
using CsvHelper.Configuration;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Managers;

public class CustomFeedRunResult
{
    public List<Dictionary<string, object?>> Records { get; set; } = [];
    public Dictionary<string, List<Dictionary<string, object?>>>? TableRecords { get; set; }
    public int RecordCount => TableRecords is not null
        ? TableRecords.Values.Sum(t => t.Count)
        : Records.Count;
    public List<string> LogLines { get; set; } = [];
}

public class CustomFeedEngine
{
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int MaxPages = 100;
    private static readonly ConcurrentDictionary<int, LoginTokenEntry> _loginTokenCache = new();

    private record LoginTokenEntry(string Token, DateTime FetchedAt);

public CustomFeedEngine(IHttpClientFactory httpClientFactory) { _httpClientFactory = httpClientFactory; }

    public async Task<CustomFeedRunResult> ExecuteAsync(CustomFeed config, CancellationToken ct)
    {
        var result = new CustomFeedRunResult();
        var log = result.LogLines;

        log.Add($"Starting custom feed: {config.Name}");
        log.Add($"Output format: {config.OutputFormat}");

        // Multi-table path (GTFS static with per-table configs)
        if (config.TableConfigs is { Count: > 0 })
        {
            log.Add("Multi-table mode active — processing each table config...");
            Dictionary<string, List<Dictionary<string, object?>>> tableRecords = [];

            foreach (var table in config.TableConfigs.OrderBy(t => t.SortOrder))
            {
                log.Add($"--- Table: {table.TargetTable} ---");
                log.Add($"URL: {table.Url}");
                log.Add($"Data path: {table.DataPath}");
                if (table.IsStatic) log.Add("Static table — generating single record from mappings");
                if (table.DistinctBy is not null) log.Add($"DistinctBy: {table.DistinctBy}");

                List<Dictionary<string, object?>> tableRows = [];

                if (table.IsStatic)
                {
                    // Static table: generate a single empty record, mappings set the values
                    tableRows.Add([]);
                    log.Add("Generated 1 empty record for static table");
                }
                else
                {
                    var client = _httpClientFactory.CreateClient("CustomFeed");
                    client.Timeout = TimeSpan.FromSeconds(30);

                    int pageNumber = 0;
                    bool hasMore = true;
                    string? cursorValue = null;

                    while (hasMore && pageNumber < MaxPages)
                    {
                        pageNumber++;
                        string url = table.Url;

                        if (table.PaginationConfig is not null && pageNumber > 1)
                            url = ApplyPagination(url, table.PaginationConfig, pageNumber, cursorValue);

                        log.Add($"Fetching page {pageNumber}...");

                        var request = new HttpRequestMessage(new HttpMethod(table.HttpMethod), url);
                        await ApplyAuth(request, config.AuthConfig, log, config.Id);

                        HttpResponseMessage response;
                        try
                        {
                            response = await client.SendAsync(request, ct);

                            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && IsLoginTokenAuth(config.AuthConfig))
                            {
                                log.Add("Got 401 — clearing login token cache and retrying...");
                                if (config.Id > 0)
                                    _loginTokenCache.TryRemove(config.Id, out _);
                                request = new HttpRequestMessage(new HttpMethod(table.HttpMethod), url);
                                await ApplyAuth(request, config.AuthConfig, log, config.Id);
                                response = await client.SendAsync(request, ct);
                            }

                            response.EnsureSuccessStatusCode();
                        }
                        catch (Exception ex)
                        {
                            log.Add($"HTTP request failed: {ex.Message}");
                            tableRows.Clear();
                            break;
                        }

                        log.Add($"Response status: {(int)response.StatusCode}");

                        string body;
                        try
                        {
                            body = await response.Content.ReadAsStringAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            log.Add($"Failed to read response body: {ex.Message}");
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(body))
                        {
                            log.Add("Empty response body");
                            break;
                        }

                        List<Dictionary<string, object?>> pageRecords;

                        try
                        {
                            pageRecords = table.ResponseFormat switch
                            {
                                ResponseFormat.JSON => ParseJsonRows(body, table.DataPath, log),
                                ResponseFormat.XML => ParseXmlRows(body, table.DataPath, log),
                                ResponseFormat.CSV => ParseCsvRows(body, table.DataPath, log),
                                _ => throw new InvalidOperationException($"Unsupported response format: {table.ResponseFormat}")
                            };
                        }
                        catch (Exception ex)
                        {
                            log.Add($"Parse failed: {ex.Message}");
                            break;
                        }

                        log.Add($"Extracted {pageRecords.Count} records from page {pageNumber}");
                        tableRows.AddRange(pageRecords);

                        if (table.PaginationConfig is null)
                        {
                            hasMore = false;
                        }
                        else
                        {
                            bool more;
                            (more, cursorValue) = HasMorePages(body, table.PaginationConfig, tableRows.Count, pageNumber);
                            hasMore = more;
                        }
                    }
                }

                log.Add($"Total raw records for {table.TargetTable}: {tableRows.Count}");

                if (table.FieldMappings is { Count: > 0 })
                {
                    log.Add($"Applying field mappings for {table.TargetTable}...");
                    var defs = table.FieldMappings.OrderBy(m => m.SortOrder)
                        .Select(m => new MappingDef(m.SourceExpression, m.TargetField, m.MappingKind))
                        .ToList();
                    tableRows = ApplyMappings(tableRows, defs, log);
                }

                // Apply DistinctBy if configured
                if (table.DistinctBy is { Length: > 0 } && tableRows.Count > 0)
                {
                    var before = tableRows.Count;
                    HashSet<string> seen = [];
                    tableRows = tableRows.Where(r =>
                    {
                        var key = r.TryGetValue(table.DistinctBy, out var v) ? v?.ToString() ?? "" : "";
                        return seen.Add(key);
                    }).ToList();
                    log.Add($"DistinctBy '{table.DistinctBy}': {before} → {tableRows.Count} records");
                }

                var tableName = table.TargetTable;
                if (tableRecords.TryGetValue(tableName, out var existing))
                    existing.AddRange(tableRows);
                else
                    tableRecords[tableName] = tableRows;

                log.Add($"--- End table: {tableName} ---");
            }

            result.TableRecords = tableRecords;
            log.Add($"Multi-table complete — tables: {string.Join(", ", tableRecords.Keys)}");
            return result;
        }

        // Single-query path (original behavior)
        log.Add($"URL: {config.BaseUrl}");
        log.Add($"Method: {config.HttpMethod}");
        log.Add($"Response format: {config.ResponseFormat}");
        log.Add($"Data path: {config.DataPath}");

        var singleClient = _httpClientFactory.CreateClient("CustomFeed");
        singleClient.Timeout = TimeSpan.FromSeconds(30);

        int sp = 0;
        bool morePages = true;
        string? cursor = null;

        while (morePages && sp < MaxPages)
        {
            sp++;
            string url = config.BaseUrl;

            if (config.PaginationConfig is not null && sp > 1)
                url = ApplyPagination(url, config.PaginationConfig, sp, cursor);

            log.Add($"Fetching page {sp}...");

            var request = new HttpRequestMessage(new HttpMethod(config.HttpMethod), url);
            await ApplyAuth(request, config.AuthConfig, log, config.Id);

            HttpResponseMessage response;
            try
            {
                response = await singleClient.SendAsync(request, ct);

                // Retry once on 401 if loginToken auth
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && IsLoginTokenAuth(config.AuthConfig))
                {
                    log.Add("Got 401 — clearing login token cache and retrying...");
                    if (config.Id > 0)
                        _loginTokenCache.TryRemove(config.Id, out _);
                    request = new HttpRequestMessage(new HttpMethod(config.HttpMethod), url);
                    await ApplyAuth(request, config.AuthConfig, log, config.Id);
                    response = await singleClient.SendAsync(request, ct);
                }

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                log.Add($"HTTP request failed: {ex.Message}");
                result.Records.Clear();
                return result;
            }

            log.Add($"Response status: {(int)response.StatusCode}");

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                log.Add($"Failed to read response body: {ex.Message}");
                break;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                log.Add("Empty response body");
                break;
            }

            List<Dictionary<string, object?>> pageRecords;

            try
            {
                pageRecords = config.ResponseFormat switch
                {
                    ResponseFormat.JSON => ParseJsonRows(body, config.DataPath, log),
                    ResponseFormat.XML => ParseXmlRows(body, config.DataPath, log),
                    ResponseFormat.CSV => ParseCsvRows(body, config.DataPath, log),
                    _ => throw new InvalidOperationException($"Unsupported response format: {config.ResponseFormat}")
                };
            }
            catch (Exception ex)
            {
                log.Add($"Parse failed: {ex.Message}");
                break;
            }

            log.Add($"Extracted {pageRecords.Count} records from page {sp}");
            result.Records.AddRange(pageRecords);

            if (config.PaginationConfig is null)
            {
                morePages = false;
            }
            else
            {
                bool m;
                (m, cursor) = HasMorePages(body, config.PaginationConfig, result.Records.Count, sp);
                morePages = m;
            }
        }

        log.Add($"Total raw records extracted: {result.Records.Count}");

        if (config.FieldMappings.Count > 0)
        {
            log.Add("Applying field mappings...");
            var mappings = config.FieldMappings.OrderBy(m => m.SortOrder)
                .Select(m => new MappingDef(m.SourceExpression, m.TargetField, m.MappingKind))
                .ToList();
            result.Records = ApplyMappings(result.Records, mappings, log);
            log.Add($"Mapped records: {result.Records.Count}");
        }
        else
        {
            log.Add("No field mappings defined — returning raw records as-is");
        }

        return result;
    }

    private static bool IsLoginTokenAuth(string? authConfigJson)
    {
        if (string.IsNullOrWhiteSpace(authConfigJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(authConfigJson);
            return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "loginToken";
        }
        catch { return false; }
    }

    private async Task ApplyAuth(HttpRequestMessage request, string? authConfigJson, List<string> log, int feedId = 0)
    {
        if (string.IsNullOrWhiteSpace(authConfigJson))
        {
            log.Add("No auth configured");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(authConfigJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "basic":
                    var username = root.GetProperty("username").GetString() ?? "";
                    var password = root.GetProperty("password").GetString() ?? "";
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                    log.Add("Sending authenticated request (Basic)");
                    break;

                case "apiKey":
                    var keyIn = root.GetProperty("in").GetString();
                    var keyName = root.GetProperty("name").GetString() ?? "";
                    var keyValue = root.GetProperty("value").GetString() ?? "";
                    if (keyIn == "header")
                        request.Headers.Add(keyName, keyValue);
                    else if (keyIn == "query")
                    {
                        var uri = request.RequestUri?.ToString() ?? "";
                        var separator = uri.Contains('?') ? '&' : '?';
                        request.RequestUri = new Uri($"{uri}{separator}{keyName}={Uri.EscapeDataString(keyValue)}");
                    }
                    log.Add("Sending authenticated request (API Key)");
                    break;

                case "bearer":
                    var token = root.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() : null;
                    if (token is null && root.TryGetProperty("loginUrl", out var loginUrl))
                    {
                        token = await FetchBearerToken(root, log);
                    }
                    if (token is not null)
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    log.Add("Sending authenticated request (Bearer)");
                    break;

                case "oauth2":
                    var oauthToken = await FetchOAuth2Token(root, log);
                    if (oauthToken is not null)
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
                    log.Add("Sending authenticated request (OAuth2)");
                    break;

                case "loginToken":
                    await ApplyLoginTokenAuth(request, root, log, feedId);
                    log.Add("Sending authenticated request (LoginToken)");
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Add($"Auth config error: {ex.Message}");
        }
    }

    private async Task ApplyLoginTokenAuth(HttpRequestMessage request, JsonElement config, List<string> log, int feedId)
    {
        var loginUrl = config.GetProperty("loginUrl").GetString()!;
        var loginMethod = config.TryGetProperty("loginMethod", out var lm) ? lm.GetString() ?? "GET" : "GET";
        var tokenLocation = config.TryGetProperty("tokenLocation", out var tl) ? tl.GetString() ?? "header" : "header";
        var tokenName = config.TryGetProperty("tokenName", out var tn) ? tn.GetString() ?? "token" : "token";

        // Parse login headers
        Dictionary<string, string> loginHeaders = [];
        if (config.TryGetProperty("loginHeaders", out var lh))
        {
            foreach (var h in lh.EnumerateObject())
                loginHeaders[h.Name] = h.Value.GetString() ?? "";
        }

        // Check cache
        string? token = null;
        if (feedId > 0 && _loginTokenCache.TryGetValue(feedId, out var entry))
        {
            // Cache for 1 hour
            if (DateTime.UtcNow - entry.FetchedAt < TimeSpan.FromHours(1))
                token = entry.Token;
            else
                _loginTokenCache.TryRemove(feedId, out _);
        }

        if (token is null)
        {
            log.Add($"Fetching token from {loginUrl}");
            var client = _httpClientFactory.CreateClient("CustomFeed");
            var loginRequest = new HttpRequestMessage(new HttpMethod(loginMethod), loginUrl);
            foreach (var h in loginHeaders)
                loginRequest.Headers.TryAddWithoutValidation(h.Key, h.Value);

            var loginResponse = await client.SendAsync(loginRequest);
            loginResponse.EnsureSuccessStatusCode();
            token = await loginResponse.Content.ReadAsStringAsync();
            token = token.Trim('"').Trim();

            if (feedId > 0)
                _loginTokenCache[feedId] = new LoginTokenEntry(token, DateTime.UtcNow);

            log.Add("Token obtained and cached");
        }

        if (tokenLocation == "header")
            request.Headers.TryAddWithoutValidation(tokenName, token);
        else if (tokenLocation == "query")
        {
            var uri = request.RequestUri?.ToString() ?? "";
            var separator = uri.Contains('?') ? '&' : '?';
            request.RequestUri = new Uri($"{uri}{separator}{tokenName}={Uri.EscapeDataString(token)}");
        }
    }

    private async Task<string?> FetchBearerToken(JsonElement config, List<string> log)
    {
        try
        {
            var loginUrl = config.GetProperty("loginUrl").GetString()!;
            var clientId = config.GetProperty("clientId").GetString() ?? "";
            var clientSecret = config.GetProperty("clientSecret").GetString() ?? "";
            var tokenField = config.TryGetProperty("tokenField", out var tf) ? tf.GetString() ?? "access_token" : "access_token";

            var client = _httpClientFactory.CreateClient("CustomFeed");
            var loginBody = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret)
            });

            var response = await client.PostAsync(loginUrl, loginBody);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(tokenField, out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            log.Add($"Bearer token fetch failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> FetchOAuth2Token(JsonElement config, List<string> log)
    {
        try
        {
            var tokenUrl = config.GetProperty("tokenUrl").GetString()!;
            var clientId = config.GetProperty("clientId").GetString() ?? "";
            var clientSecret = config.GetProperty("clientSecret").GetString() ?? "";
            var scopes = config.TryGetProperty("scopes", out var s)
                ? string.Join(" ", s.EnumerateArray().Select(e => e.GetString()))
                : "";

            var client = _httpClientFactory.CreateClient("CustomFeed");
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("scope", scopes)
            });

            var response = await client.PostAsync(tokenUrl, body);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            log.Add($"OAuth2 token fetch failed: {ex.Message}");
            return null;
        }
    }

    private string ApplyPagination(string url, string paginationConfigJson, int pageNumber, string? cursorValue = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(paginationConfigJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "page":
                    var pageParam = root.GetProperty("pageParam").GetString() ?? "page";
                    var perPageParam = root.GetProperty("perPageParam").GetString() ?? "per_page";
                    var perPage = root.GetProperty("perPage").GetInt32();
                    var separator = url.Contains('?') ? '&' : '?';
                    return $"{url}{separator}{pageParam}={pageNumber}&{perPageParam}={perPage}";

                case "offset":
                    var offsetParam = root.GetProperty("offsetParam").GetString() ?? "offset";
                    var limitParam = root.GetProperty("limitParam").GetString() ?? "limit";
                    var limit = root.GetProperty("limit").GetInt32();
                    var offset = (pageNumber - 1) * limit;
                    separator = url.Contains('?') ? '&' : '?';
                    return $"{url}{separator}{offsetParam}={offset}&{limitParam}={limit}";

                case "cursor":
                    if (string.IsNullOrWhiteSpace(cursorValue))
                        return url;
                    var cursorParam = root.GetProperty("cursorParam").GetString() ?? "cursor";
                    separator = url.Contains('?') ? '&' : '?';
                    return $"{url}{separator}{cursorParam}={Uri.EscapeDataString(cursorValue)}";
            }
        }
        catch { }

        return url;
    }

    private (bool hasMore, string? cursorValue) HasMorePages(string body, string paginationConfigJson, int totalSoFar, int pageNumber)
    {
        try
        {
            using var doc = JsonDocument.Parse(paginationConfigJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "cursor")
            {
                using var bodyDoc = JsonDocument.Parse(body);
                var cursorField = root.GetProperty("cursorField").GetString()?.TrimStart('$', '.');
                if (cursorField is not null && bodyDoc.RootElement.TryGetProperty(cursorField, out var next))
                {
                    if (next.ValueKind == JsonValueKind.Null || next.ValueKind == JsonValueKind.Undefined)
                        return (false, null);
                    var value = next.ToString();
                    return (true, value);
                }
                return (false, null);
            }

            if (root.TryGetProperty("totalField", out var totalField))
            {
                var fieldName = totalField.GetString()?.TrimStart('$', '.');
                if (fieldName is not null)
                {
                    using var bodyDoc = JsonDocument.Parse(body);
                    if (bodyDoc.RootElement.TryGetProperty(fieldName, out var totalElem) &&
                        totalElem.TryGetInt32(out var total))
                        return (totalSoFar < total, null);
                }
            }

            return (pageNumber < MaxPages, null);
        }
        catch { return (false, null); }
    }

    private List<Dictionary<string, object?>> ParseJsonRows(string body, string dataPath, List<string> log)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var segments = ParseDataPathSegments(dataPath);

        if (segments.Count == 0)
        {
            log.Add("DataPath resolved to root — treating response as an array or wrapping as single row");
            return ExtractValuesFromElement(root);
        }

        List<Dictionary<string, object?>> result = [];
        Dictionary<string, object?> context = [];

        TraverseAndAccumulate(root, segments, 0, context, result, log);
        return result;
    }

    private static List<(string Name, bool IsIterable)> ParseDataPathSegments(string dataPath)
    {
        var path = dataPath.TrimStart('$').TrimStart('.');
        List<(string, bool)> result = [];

        int pos = 0;
        while (pos < path.Length)
        {
            if (path[pos] == '.')
            {
                pos++;
                continue;
            }

            // Find the next [*] or dot or end
            int starIdx = path.IndexOf("[*]", pos);
            int dotIdx = path.IndexOf('.', pos);
            if (dotIdx < 0) dotIdx = path.Length;

            string name;
            bool isIterable;

            if (starIdx >= 0 && starIdx < dotIdx)
            {
                // [*] comes before the next dot — segment is iterable
                name = path[pos..starIdx].Trim('.');
                isIterable = true;
                pos = starIdx + 3; // skip past [*]
            }
            else if (starIdx >= 0 && starIdx == dotIdx - 3 && path[starIdx..dotIdx] == "[*]")
            {
                // [*] is right before the dot: "name[*]."
                name = path[pos..starIdx];
                isIterable = true;
                pos = dotIdx + 1;
            }
            else
            {
                // No [*] — regular segment
                name = path[pos..dotIdx];
                isIterable = false;
                pos = dotIdx + 1;
            }

            if (name.Length > 0)
                result.Add((name, isIterable));
        }

        return result;
    }

    private void TraverseAndAccumulate(
        JsonElement current,
        List<(string Name, bool IsIterable)> segments,
        int depth,
        Dictionary<string, object?> context,
        List<Dictionary<string, object?>> results,
        List<string> log)
    {
        if (depth >= segments.Count)
        {
            var row = new Dictionary<string, object?>(context);
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in current.EnumerateObject())
                {
                    // If a context field has the same name, rename it with parent-level prefix
                    if (context.ContainsKey(prop.Name) && segments.Count >= 2)
                    {
                        var parentPrefix = Singularize(segments[^2].Name) + "_";
                        row[parentPrefix + prop.Name] = context[prop.Name];
                    }
                    row[prop.Name] = ElementToObject(prop.Value);
                }
            }
            results.Add(row);
            return;
        }

        var seg = segments[depth];
        JsonElement? next;

        if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(seg.Name, out var propValue))
            next = propValue;
        else
            next = null;

        if (next is null)
        {
            log.Add($"Segment '{seg.Name}' not found at depth {depth}");
            return;
        }

        // When IsIterable is true and value is an Object, iterate property values (dictionary-as-array)
        // When value is an Array, always iterate items (regardless of IsIterable)
        List<JsonElement> items;
        if (next.Value.ValueKind == JsonValueKind.Array)
        {
            items = next.Value.EnumerateArray().ToList();
        }
        else if (seg.IsIterable && next.Value.ValueKind == JsonValueKind.Object)
        {
            items = next.Value.EnumerateObject().Select(p => p.Value).ToList();
        }
        else
        {
            items = [next.Value];
        }

        foreach (var item in items)
        {
            var newContext = new Dictionary<string, object?>(context);

            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in current.EnumerateObject())
                {
                    if (prop.Name != seg.Name)
                        newContext[prop.Name] = ElementToObject(prop.Value);
                }
            }

            TraverseAndAccumulate(item, segments, depth + 1, newContext, results, log);
        }
    }

    private static string Singularize(string word)
    {
        if (word.EndsWith("ies", StringComparison.Ordinal))
            return word[..^3] + "y";
        if (word.EndsWith("ses", StringComparison.Ordinal) ||
            word.EndsWith("xes", StringComparison.Ordinal) ||
            word.EndsWith("ches", StringComparison.Ordinal) ||
            word.EndsWith("shes", StringComparison.Ordinal))
            return word[..^2];
        if (word.EndsWith("s", StringComparison.Ordinal) && word.Length > 1)
            return word[..^1];
        return word;
    }

    private List<Dictionary<string, object?>> ParseXmlRows(string body, string dataPath, List<string> log)
    {
        var doc = XDocument.Parse(body);
        var nodes = doc.XPathEvaluate(dataPath) as IEnumerable<object>;
        List<Dictionary<string, object?>> result = [];

        if (nodes is null)
        {
            log.Add("XPath returned no results");
            return result;
        }

        foreach (var node in nodes)
        {
            if (node is XElement el)
            {
                Dictionary<string, object?> row = [];
                FlattenXElement(el, row, "");
                result.Add(row);
            }
        }

        return result;
    }

    private void FlattenXElement(XElement el, Dictionary<string, object?> dict, string prefix)
    {
        foreach (var attr in el.Attributes())
        {
            var key = string.IsNullOrEmpty(prefix) ? $"@{attr.Name.LocalName}" : $"{prefix}@{attr.Name.LocalName}";
            dict[key] = attr.Value;
        }

        foreach (var child in el.Elements())
        {
            var key = string.IsNullOrEmpty(prefix) ? child.Name.LocalName : $"{prefix}.{child.Name.LocalName}";
            if (child.HasElements)
                FlattenXElement(child, dict, key);
            else
                dict[key] = child.Value;
        }

        if (!el.HasElements && !string.IsNullOrEmpty(el.Value))
            dict[string.IsNullOrEmpty(prefix) ? "_value" : prefix] = el.Value;
    }

    private List<Dictionary<string, object?>> ParseCsvRows(string body, string dataPath, List<string> log)
    {
        List<Dictionary<string, object?>> result = [];
        var selectedColumns = string.IsNullOrWhiteSpace(dataPath)
            ? null
            : dataPath.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        using var reader = new StringReader(body);
        using var csv = new CsvReader(reader, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        });

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            Dictionary<string, object?> row = [];
            foreach (var header in csv.HeaderRecord!)
            {
                if (selectedColumns is null || selectedColumns.Contains(header))
                    row[header] = csv.GetField(header);
            }
            result.Add(row);
        }

        return result;
    }

    private record MappingDef(string SourceExpression, string TargetField, MappingKind MappingKind);

    private List<Dictionary<string, object?>> ApplyMappings(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<MappingDef> mappings,
        List<string> log)
    {
        var mappedSourceFields = mappings
            .Where(m => m.MappingKind == MappingKind.Direct)
            .Select(m => m.SourceExpression.TrimStart('$').TrimStart('.'))
            .ToHashSet();

        List<Dictionary<string, object?>> result = [];

        foreach (var row in rows)
        {
            Dictionary<string, object?> mapped = [];

            // Pass through unmapped source fields (except those mapped as Direct)
            foreach (var kv in row)
                if (!mappedSourceFields.Contains(kv.Key))
                    mapped[kv.Key] = kv.Value;

            foreach (var mapping in mappings)
            {
                var sourceExpr = mapping.SourceExpression.TrimStart('$').TrimStart('.');

                switch (mapping.MappingKind)
                {
                    case MappingKind.Direct:
                        if (string.IsNullOrWhiteSpace(sourceExpr))
                        {
                            mapped[mapping.TargetField] = null;
                            break;
                        }

                        if (row.TryGetValue(sourceExpr, out var val))
                            mapped[mapping.TargetField] = val;
                        else
                            mapped[mapping.TargetField] = null;
                        break;

                    case MappingKind.Static:
                        mapped[mapping.TargetField] = mapping.SourceExpression;
                        break;

                    case MappingKind.Expression:
                        var expr = mapping.SourceExpression;
                        foreach (var kv in row)
                            expr = expr.Replace($"{{{kv.Key}}}", kv.Value?.ToString() ?? "");
                        mapped[mapping.TargetField] = expr;
                        break;

                    case MappingKind.TimeExtract:
                        if (row.TryGetValue(sourceExpr, out var timeVal) && timeVal is not null)
                        {
                            var str = timeVal.ToString() ?? "";
                            if (DateTime.TryParse(str, out var timeDt))
                                mapped[mapping.TargetField] = timeDt.ToString("HH:mm:ss");
                            else if (str.Length >= 10 && DateTime.TryParse(str[..10], out var dateOnly))
                                mapped[mapping.TargetField] = dateOnly.ToString("HH:mm:ss");
                            else if (str.Length == 8 && str[2] == ':' && str[5] == ':')
                                mapped[mapping.TargetField] = str; // already HH:mm:ss
                            else
                                mapped[mapping.TargetField] = str; // pass through as fallback
                        }
                        else
                        {
                            mapped[mapping.TargetField] = "00:00:00"; // default time
                        }
                        break;

                    case MappingKind.DateExtract:
                        if (row.TryGetValue(sourceExpr, out var dateVal) && dateVal is not null)
                        {
                            var str = dateVal.ToString() ?? "";
                            if (DateTime.TryParse(str, out var dateDt))
                                mapped[mapping.TargetField] = dateDt.ToString("yyyyMMdd");
                            else if (str.Length >= 10 && DateTime.TryParse(str[..10], out var dateOnly))
                                mapped[mapping.TargetField] = dateOnly.ToString("yyyyMMdd");
                            else
                                mapped[mapping.TargetField] = str; // pass through as fallback
                        }
                        else
                        {
                            mapped[mapping.TargetField] = "20250101"; // default date
                        }
                        break;
                }
            }
            result.Add(mapped);
        }

        log.Add($"Applied {mappings.Count} mappings to {result.Count} rows");
        return result;
    }

    private List<Dictionary<string, object?>> ExtractValuesFromElement(JsonElement element)
    {
        List<Dictionary<string, object?>> result = [];

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Dictionary<string, object?> row = [];
                if (item.ValueKind == JsonValueKind.Object)
                    foreach (var prop in item.EnumerateObject())
                        row[prop.Name] = ElementToObject(prop.Value);
                else
                    row["_value"] = ElementToObject(item);
                result.Add(row);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, object?> row = [];
            foreach (var prop in element.EnumerateObject())
                row[prop.Name] = ElementToObject(prop.Value);
            result.Add(row);
        }

        return result;
    }

    private static object? ElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => el.GetRawText(),
            JsonValueKind.Array => el.GetRawText(),
            _ => el.GetRawText()
        };
    }

    public async Task<CustomFeedDiscoverResponse> DiscoverStructureAsync(CustomFeed config, CancellationToken ct)
    {
        List<string> log = [];
        log.Add($"Discovering structure for: {config.BaseUrl}");

        var client = _httpClientFactory.CreateClient("CustomFeed");
        client.Timeout = TimeSpan.FromSeconds(30);

        var request = new HttpRequestMessage(new HttpMethod(config.HttpMethod), config.BaseUrl);
        await ApplyAuth(request, config.AuthConfig, log);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            log.Add($"HTTP request failed: {ex.Message}");
            return new CustomFeedDiscoverResponse { LogLines = log };
        }

        log.Add($"Response status: {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            log.Add("Empty response body");
            return new CustomFeedDiscoverResponse { LogLines = log };
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var structure = BuildStructureNode("$", "$", root, 8);
        List<string> arrayPaths = [];
        CollectArrayPaths(structure, arrayPaths);

        log.Add($"Built structure tree — found {arrayPaths.Count} valid array paths");
        return new CustomFeedDiscoverResponse
        {
            Structure = structure,
            ArrayPaths = arrayPaths,
            LogLines = log
        };
    }

    private static JsonStructureNode BuildStructureNode(string name, string path, JsonElement el, int depth)
    {
        var node = new JsonStructureNode
        {
            Name = name,
            Path = path,
            Type = el.ValueKind.ToString().ToLowerInvariant()
        };

        if (el.ValueKind == JsonValueKind.Array)
        {
            node.ArrayItemCount = el.GetArrayLength();
            if (node.ArrayItemCount > 0)
            {
                var first = el.EnumerateArray().First();
                node.ArrayItemType = first.ValueKind.ToString().ToLowerInvariant();
            }
        }

        if (depth <= 0)
        {
            if (el.ValueKind == JsonValueKind.Object) node.Sample = "{…}";
            else if (el.ValueKind == JsonValueKind.Array) node.Sample = "[…]";
            return node;
        }

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    node.Children.Add(BuildStructureNode(prop.Name, $"{path}.{prop.Name}", prop.Value, depth - 1));
                break;

            case JsonValueKind.Array:
                if (node.ArrayItemCount > 0)
                {
                    var first = el.EnumerateArray().First();
                    if (first.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        node.Children.Add(BuildStructureNode("[0]", $"{path}[0]", first, depth - 1));
                    else
                        node.Sample = ElementToObject(first)?.ToString();
                }
                break;

            case JsonValueKind.String:
                node.Sample = el.GetString();
                break;

            case JsonValueKind.Number:
                node.Sample = el.TryGetInt64(out var l) ? l.ToString() : el.GetDouble().ToString();
                break;

            case JsonValueKind.True:
                node.Sample = "true";
                break;

            case JsonValueKind.False:
                node.Sample = "false";
                break;
        }

        return node;
    }

    private static void CollectArrayPaths(JsonStructureNode node, List<string> paths)
    {
        if (node.Type == "array" && node.ArrayItemType == "object")
        {
            var ap = node.Path.Replace("[0]", "[*]");
            paths.Add(ap);
        }
        foreach (var child in node.Children)
            CollectArrayPaths(child, paths);
    }
}
