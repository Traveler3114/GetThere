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
    public int RecordCount => Records.Count;
    public List<string> LogLines { get; set; } = [];
}

public class CustomFeedEngine
{
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int MaxPages = 100;

    public CustomFeedEngine(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CustomFeedRunResult> ExecuteAsync(CustomFeed config, CancellationToken ct)
    {
        var result = new CustomFeedRunResult();
        var log = result.LogLines;

        log.Add($"Starting custom feed: {config.Name}");
        log.Add($"URL: {config.BaseUrl}");
        log.Add($"Method: {config.HttpMethod}");
        log.Add($"Response format: {config.ResponseFormat}");
        log.Add($"Output format: {config.OutputFormat}");
        log.Add($"Data path: {config.DataPath}");

        var client = _httpClientFactory.CreateClient("CustomFeed");
        client.Timeout = TimeSpan.FromSeconds(30);

        int pageNumber = 0;
        bool hasMore = true;
        string? cursorValue = null;

        while (hasMore && pageNumber < MaxPages)
        {
            pageNumber++;
            string url = config.BaseUrl;

            if (config.PaginationConfig is not null && pageNumber > 1)
                url = ApplyPagination(url, config.PaginationConfig, pageNumber, cursorValue);

            log.Add($"Fetching page {pageNumber}...");

            var request = new HttpRequestMessage(new HttpMethod(config.HttpMethod), url);
            ApplyAuth(request, config.AuthConfig, log);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, ct);
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

            log.Add($"Extracted {pageRecords.Count} records from page {pageNumber}");
            result.Records.AddRange(pageRecords);

            if (config.PaginationConfig is null)
            {
                hasMore = false;
            }
            else
            {
                bool more;
                (more, cursorValue) = HasMorePages(body, config.PaginationConfig, result.Records.Count, pageNumber);
                hasMore = more;
            }
        }

        log.Add($"Total raw records extracted: {result.Records.Count}");

        if (config.FieldMappings.Count > 0)
        {
            log.Add("Applying field mappings...");
            result.Records = ApplyMappings(result.Records, config.FieldMappings.OrderBy(m => m.SortOrder).ToList(), log);
            log.Add($"Mapped records: {result.Records.Count}");
        }
        else
        {
            log.Add("No field mappings defined — returning raw records as-is");
        }

        return result;
    }

    private void ApplyAuth(HttpRequestMessage request, string? authConfigJson, List<string> log)
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
                        token = FetchBearerToken(root, log);
                    }
                    if (token is not null)
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    log.Add("Sending authenticated request (Bearer)");
                    break;

                case "oauth2":
                    var oauthToken = FetchOAuth2Token(root, log);
                    if (oauthToken is not null)
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
                    log.Add("Sending authenticated request (OAuth2)");
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Add($"Auth config error: {ex.Message}");
        }
    }

    private string? FetchBearerToken(JsonElement config, List<string> log)
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

            var response = client.PostAsync(loginUrl, loginBody).Result;
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().Result;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(tokenField, out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            log.Add($"Bearer token fetch failed: {ex.Message}");
            return null;
        }
    }

    private string? FetchOAuth2Token(JsonElement config, List<string> log)
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

            var response = client.PostAsync(tokenUrl, body).Result;
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().Result;
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

        // Split path into segments: $.countries[*].cities[*].places[*]
        // -> ["countries", "cities", "places"]
        var path = dataPath.TrimStart('$').TrimStart('.');
        var segments = path
            .Split(new[] { "[*]." }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace("[*]", "").Trim('.'))
            .Where(s => s.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            log.Add("DataPath resolved to root — treating response as an array or wrapping as single row");
            return ExtractValuesFromElement(root);
        }

        var result = new List<Dictionary<string, object?>>();
        var context = new Dictionary<string, object?>();

        TraverseAndAccumulate(root, segments, 0, context, result, log);
        return result;
    }

    private void TraverseAndAccumulate(
        JsonElement current,
        List<string> segments,
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
                        var parentPrefix = Singularize(segments[^2]) + "_";
                        row[parentPrefix + prop.Name] = context[prop.Name];
                    }
                    row[prop.Name] = ElementToObject(prop.Value);
                }
            }
            results.Add(row);
            return;
        }

        var segmentName = segments[depth];
        JsonElement? next;

        if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segmentName, out var propValue))
            next = propValue;
        else
            next = null;

        if (next is null)
        {
            log.Add($"Segment '{segmentName}' not found at depth {depth}");
            return;
        }

        var items = next.Value.ValueKind == JsonValueKind.Array
            ? next.Value.EnumerateArray().ToList()
            : new List<JsonElement> { next.Value };

        foreach (var item in items)
        {
            var newContext = new Dictionary<string, object?>(context);

            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in current.EnumerateObject())
                {
                    if (prop.Name != segmentName)
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
        var result = new List<Dictionary<string, object?>>();

        if (nodes is null)
        {
            log.Add("XPath returned no results");
            return result;
        }

        foreach (var node in nodes)
        {
            if (node is XElement el)
            {
                var row = new Dictionary<string, object?>();
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
        var result = new List<Dictionary<string, object?>>();
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
            var row = new Dictionary<string, object?>();
            foreach (var header in csv.HeaderRecord!)
            {
                if (selectedColumns is null || selectedColumns.Contains(header))
                    row[header] = csv.GetField(header);
            }
            result.Add(row);
        }

        return result;
    }

    private List<Dictionary<string, object?>> ApplyMappings(
        List<Dictionary<string, object?>> rows,
        List<CustomFeedFieldMapping> mappings,
        List<string> log)
    {
        var targetFields = mappings.Select(m => m.TargetField).ToHashSet();
        // Collect source fields used by Direct mappings (strip $ prefix)
        var mappedSourceFields = mappings
            .Where(m => m.MappingKind == MappingKind.Direct)
            .Select(m => m.SourceExpression.TrimStart('$').TrimStart('.'))
            .ToHashSet();

        var result = new List<Dictionary<string, object?>>();

        foreach (var row in rows)
        {
            var mapped = new Dictionary<string, object?>();

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
                }
            }
            result.Add(mapped);
        }

        log.Add($"Applied {mappings.Count} mappings to {result.Count} rows");
        return result;
    }

    private List<Dictionary<string, object?>> ExtractValuesFromElement(JsonElement element)
    {
        var result = new List<Dictionary<string, object?>>();

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var row = new Dictionary<string, object?>();
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
            var row = new Dictionary<string, object?>();
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
        var log = new List<string>();
        log.Add($"Discovering structure for: {config.BaseUrl}");

        var client = _httpClientFactory.CreateClient("CustomFeed");
        client.Timeout = TimeSpan.FromSeconds(30);

        var request = new HttpRequestMessage(new HttpMethod(config.HttpMethod), config.BaseUrl);
        ApplyAuth(request, config.AuthConfig, log);

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
        var arrayPaths = new List<string>();
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
