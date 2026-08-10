using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Managers;

public class CustomSourceManager
{
    private readonly TransitDbContext _db;
    private readonly CustomSourceEngine _engine;
    private readonly CustomHttpSource _httpSource;
    private readonly FeedManager _feeds;
    private readonly ILogger<CustomSourceManager> _logger;

    private readonly IWebHostEnvironment _env;
    private readonly CustomExtractorRegistry _extractors;
    private readonly SecretProtector _secrets;

    public CustomSourceManager(
        TransitDbContext db,
        CustomSourceEngine engine,
        CustomHttpSource httpSource,
        FeedManager feeds,
        IWebHostEnvironment env,
        CustomExtractorRegistry extractors,
        SecretProtector secrets,
        ILogger<CustomSourceManager> logger)
    {
        _db = db;
        _engine = engine;
        _httpSource = httpSource;
        _feeds = feeds;
        _env = env;
        _extractors = extractors;
        _secrets = secrets;
        _logger = logger;
    }

    /// <summary>Extensions accepted for upload. Anything else has no reader behind it.</summary>
    private static readonly HashSet<string> AllowedUploadExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".csv", ".xlsx", ".xls", ".pdf", ".json", ".xml", ".html", ".htm" };

    public async Task<UploadedFileResponse> StoreUploadAsync(int sourceId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new Exceptions.AppException("No file was uploaded.", 400, "NO_FILE");

        var source = await _db.CustomSources.FindAsync([sourceId], ct)
            ?? throw new Exceptions.AppException("Custom source not found.", 404, "SOURCE_NOT_FOUND");

        // Only the file's own name is kept — a browser can send a path, and the storage helper
        // refuses anything that escapes the source directory regardless.
        var name = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(name);

        if (!AllowedUploadExtensions.Contains(extension))
            throw new Exceptions.AppException(
                $"'{extension}' files cannot be read. Accepted: {string.Join(", ", AllowedUploadExtensions.Order())}.",
                400, "UNSUPPORTED_UPLOAD");

        var storage = new CustomSourceStorage(_env.ContentRootPath);
        var dir = storage.DirectoryFor(source.Id);
        Directory.CreateDirectory(dir);

        var path = storage.FilePathFor(source.Id, name);
        await using (var stream = File.Create(path))
            await file.CopyToAsync(stream, ct);

        _logger.LogInformation("Stored upload {Name} ({Bytes} bytes) for custom source {SourceId}", name, file.Length, source.Id);

        return new UploadedFileResponse
        {
            FileName = name,
            SizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            Url = CustomSourceEngine.UploadScheme + name
        };
    }

    public List<UploadedFileResponse> ListUploads(int sourceId)
    {
        var dir = new CustomSourceStorage(_env.ContentRootPath).DirectoryFor(sourceId);
        if (!Directory.Exists(dir)) return [];

        return [.. new DirectoryInfo(dir).GetFiles()
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new UploadedFileResponse
            {
                FileName = f.Name,
                SizeBytes = f.Length,
                UploadedAt = f.LastWriteTimeUtc,
                Url = CustomSourceEngine.UploadScheme + f.Name
            })];
    }

    public List<ExtractorResponse> ListExtractors() =>
        [.. _extractors.All.Select(e => new ExtractorResponse { Key = e.Key, Description = e.Description })];

    public async Task<(List<CustomSourceResponse> Items, int Total)> GetAllAsync(int page, int perPage, CancellationToken ct)
    {
        var query = _db.CustomSources.AsNoTracking().OrderBy(cs => cs.Id);
        var total = await query.CountAsync(ct);

        var sources = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Include(cs => cs.Operator)
            .Include(cs => cs.Requests).ThenInclude(r => r.Mappings)
            .ToListAsync(ct);

        var ids = sources.ConvertAll(s => s.Id);
        var lastRuns = await _db.CustomSourceRuns.AsNoTracking()
            .Where(r => ids.Contains(r.CustomSourceId))
            .GroupBy(r => r.CustomSourceId)
            .Select(g => new { Id = g.Key, Status = g.OrderByDescending(r => r.StartedAt).First().Status })
            .ToDictionaryAsync(x => x.Id, x => x.Status.ToString(), ct);

        return ([.. sources.Select(s => CustomSourceMapper.ToResponse(s, lastRuns.GetValueOrDefault(s.Id)))], total);
    }

    public async Task<CustomSourceResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        var source = await LoadAsync(id, tracking: false, ct);
        if (source is null) return null;

        var lastRun = await _db.CustomSourceRuns.AsNoTracking()
            .Where(r => r.CustomSourceId == id)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        return CustomSourceMapper.ToResponse(source, lastRun?.Status.ToString());
    }

    public async Task<CustomSourceResponse> CreateAsync(CreateCustomSourceRequest request, CancellationToken ct)
    {
        var op = await _db.Operators.FindAsync([request.OperatorId], ct)
            ?? throw new AppException("Operator not found.", 404, "OPERATOR_NOT_FOUND");

        var source = new CustomSource
        {
            OperatorId = op.Id,
            Name = request.Name,
            Kind = ParseEnum<CustomSourceKind>(request.Kind, nameof(request.Kind)),
            ExtractorKey = request.ExtractorKey,
            // Encrypted before it reaches the column. See SecretProtector.
            AuthConfig = _secrets.Protect(request.AuthConfig),
            RefreshIntervalSeconds = request.RefreshIntervalSeconds,
            ServiceWindowStart = request.ServiceWindowStart,
            ServiceWindowEnd = request.ServiceWindowEnd,
            CreatedAt = DateTime.UtcNow
        };

        ValidateWindow(source);
        ApplyRequests(source, request.Requests);

        _db.CustomSources.Add(source);
        await _db.SaveChangesAsync(ct);

        source.Operator = op;
        return CustomSourceMapper.ToResponse(source);
    }

    public async Task<CustomSourceResponse?> UpdateAsync(int id, UpdateCustomSourceRequest request, CancellationToken ct)
    {
        var source = await LoadAsync(id, tracking: true, ct);
        if (source is null) return null;

        if (request.OperatorId is { } operatorId)
        {
            _ = await _db.Operators.FindAsync([operatorId], ct)
                ?? throw new AppException("Operator not found.", 404, "OPERATOR_NOT_FOUND");
            source.OperatorId = operatorId;
        }

        if (request.Name is not null) source.Name = request.Name;
        if (request.Kind is not null) source.Kind = ParseEnum<CustomSourceKind>(request.Kind, nameof(request.Kind));
        if (request.ExtractorKey is not null) source.ExtractorKey = request.ExtractorKey;
        // Null means "leave the stored credential alone", which is what lets the editor render a
        // source without ever being sent the secret it holds. An empty string clears it.
        if (request.AuthConfig is not null) source.AuthConfig = _secrets.Protect(request.AuthConfig);
        if (request.RefreshIntervalSeconds is { } interval) source.RefreshIntervalSeconds = interval;
        if (request.IsActive is { } active) source.IsActive = active;
        if (request.ServiceWindowStart is { } start) source.ServiceWindowStart = start;
        if (request.ServiceWindowEnd is { } end) source.ServiceWindowEnd = end;

        ValidateWindow(source);

        if (request.Requests is not null)
        {
            // Replaced wholesale rather than diffed: the editor always posts the full set, and
            // matching mappings by position across an edit is how silent corruption starts.
            _db.CustomSourceMappings.RemoveRange(source.Requests.SelectMany(r => r.Mappings));
            _db.CustomSourceRequests.RemoveRange(source.Requests);
            source.Requests.Clear();
            ApplyRequests(source, request.Requests);
        }

        await _db.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var source = await LoadAsync(id, tracking: true, ct);
        if (source is null) return false;

        var linked = await _db.Feeds.Where(f => f.CustomSourceId == id).Select(f => f.FeedId).ToListAsync(ct);
        if (linked.Count > 0)
            throw new AppException(
                $"Custom source is still used by feed(s): {string.Join(", ", linked)}.", 409, "CUSTOM_SOURCE_IN_USE");

        // Explicit and ordered because every FK in this model is Restrict.
        _db.CustomSourceMappings.RemoveRange(source.Requests.SelectMany(r => r.Mappings));
        _db.CustomSourceRequests.RemoveRange(source.Requests);
        _db.CustomSourceRuns.RemoveRange(_db.CustomSourceRuns.Where(r => r.CustomSourceId == id));
        _db.CustomSources.Remove(source);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<CustomSourceRunResponse>> GetRunsAsync(int id, int limit, CancellationToken ct)
    {
        var runs = await _db.CustomSourceRuns.AsNoTracking()
            .Where(r => r.CustomSourceId == id)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return [.. runs.Select(CustomSourceMapper.ToResponse)];
    }

    /// <summary>
    /// Fetches one page and reports the response's own shape, so the editor can offer real paths
    /// instead of asking an admin to guess at a structure they cannot see.
    /// </summary>
    public async Task<CustomSourceDiscoverResponse> DiscoverAsync(int id, int requestIndex, CancellationToken ct)
    {
        var source = await LoadAsync(id, tracking: false, ct)
            ?? throw new AppException("Custom source not found.", 404, "CUSTOM_SOURCE_NOT_FOUND");

        var ordered = source.Requests.OrderBy(r => r.SortOrder).ToList();
        if (requestIndex < 0 || requestIndex >= ordered.Count)
            throw new AppException("Request index out of range.", 400, "INVALID_REQUEST_INDEX");

        var request = ordered[requestIndex];
        var response = new CustomSourceDiscoverResponse();

        string body;
        try
        {
            body = await _engine.FetchRawAsync(request, _secrets.Unprotect(source.AuthConfig), ct);
        }
        catch (HttpRequestException ex)
        {
            response.LogLines.Add($"Could not reach {request.Url}: {ex.Message}");
            return response;
        }

        response.LogLines.Add($"Fetched {body.Length:N0} chars from {request.Url}");

        if (request.Format != CustomSourceFormat.Json)
        {
            response.LogLines.Add($"Structure discovery currently covers JSON only; this request is {request.Format}.");
            return response;
        }

        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(body);
        }
        catch (System.Text.Json.JsonException ex)
        {
            response.LogLines.Add($"Response is not valid JSON: {ex.Message}");
            return response;
        }

        using (doc)
        {
            var arrayPaths = new List<string>();
            response.Structure = Describe("root", string.Empty, doc.RootElement, depth: 0, arrayPaths);
            response.ArrayPaths = arrayPaths;
        }

        response.LogLines.Add(response.ArrayPaths.Count > 0
            ? $"Found {response.ArrayPaths.Count} array(s) — the data path is most likely one of: {string.Join(", ", response.ArrayPaths)}"
            : "No arrays found in the response.");

        return response;
    }

    /// <summary>
    /// Walks the response and records where the arrays are. Depth-limited because the point is to
    /// find the row array, not to mirror an entire document.
    /// </summary>
    private static StructureNode Describe(
        string name,
        string path,
        System.Text.Json.JsonElement element,
        int depth,
        List<string> arrayPaths)
    {
        const int maxDepth = 6;
        const int maxChildren = 40;

        var node = new StructureNode { Name = name, Path = path, Type = element.ValueKind.ToString().ToLowerInvariant() };

        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                if (depth >= maxDepth) break;
                foreach (var property in element.EnumerateObject().Take(maxChildren))
                {
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    node.Children.Add(Describe(property.Name, childPath, property.Value, depth + 1, arrayPaths));
                }
                break;

            case System.Text.Json.JsonValueKind.Array:
                var items = element.EnumerateArray().ToList();
                node.ArrayItemCount = items.Count;
                if (path.Length > 0) arrayPaths.Add(path);

                // One representative element is enough — every row in an array of records has the
                // same shape, and describing all of them buries the answer.
                if (items.Count > 0 && depth < maxDepth)
                    node.Children.Add(Describe("[0]", path, items[0], depth + 1, arrayPaths));
                break;

            default:
                node.Sample = Truncate(element.ToString());
                break;
        }

        return node;
    }

    /// <summary>Runs the full extraction without importing, capped so a preview stays cheap.</summary>
    public async Task<CustomSourcePreviewResponse> PreviewAsync(int id, CancellationToken ct)
    {
        var source = await LoadAsync(id, tracking: false, ct)
            ?? throw new AppException("Custom source not found.", 404, "CUSTOM_SOURCE_NOT_FOUND");

        var snapshot = await _httpSource.ExtractSnapshotAsync(source, maxRowsPerRequest: 200, ct);
        var document = CustomHttpSource.BuildDocument(snapshot, source.OperatorId, out var warnings);

        var response = new CustomSourcePreviewResponse
        {
            Completeness = document.Completeness.ToString(),
            LogLines = snapshot.Log,
            Warnings = [.. snapshot.Warnings, .. warnings]
        };

        foreach (var (section, rows) in snapshot.Sections)
        {
            response.SectionsPresent.Add(section);
            response.RowCounts[section] = rows.Count;
            response.Samples[section] = [.. rows.Take(5)];
        }

        return response;
    }

    /// <summary>
    /// Fetches, versions and imports — the same path the poller takes, so "run now" in the admin
    /// console exercises exactly what a scheduled run will do rather than a parallel implementation.
    /// </summary>
    public async Task<CustomSourceRunResponse> RunAsync(int id, CancellationToken ct)
    {
        var source = await LoadAsync(id, tracking: true, ct)
            ?? throw new AppException("Custom source not found.", 404, "CUSTOM_SOURCE_NOT_FOUND");

        var feed = await _db.Feeds.FirstOrDefaultAsync(f => f.CustomSourceId == id, ct)
            ?? throw new AppException(
                "No feed is linked to this custom source. Create a feed for it first.", 409, "CUSTOM_SOURCE_NOT_LINKED");

        var run = new CustomSourceRun
        {
            CustomSourceId = id,
            StartedAt = DateTime.UtcNow,
            Status = CustomSourceRunStatus.Running
        };
        _db.CustomSourceRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        try
        {
            var version = await _feeds.TriggerImportAsync(feed.Id, ct);

            // Re-read rather than trusting the returned entity: the counts and final status are
            // written during the import, and the instance handed back was loaded before it ran.
            var imported = await _db.FeedVersions.AsNoTracking()
                .FirstOrDefaultAsync(fv => fv.Id == version.Id, ct) ?? version;

            run.Status = imported.ImportStatus == FeedImportStatus.Failed
                ? CustomSourceRunStatus.Failed
                : CustomSourceRunStatus.Success;
            run.FeedVersionId = imported.Id;
            run.RecordsProduced = imported.StopCount + imported.RouteCount + imported.TripCount;
            run.LogText = $"Feed version {imported.Id} — {imported.ImportStatus}, {imported.Completeness}: "
                + $"{imported.StopCount} stops, {imported.RouteCount} routes, {imported.TripCount} trips"
                + (imported.SynthesizedSections is { } s ? $" (synthesized: {s})" : string.Empty)
                + (imported.ImportError is { } e ? $" — {e}" : string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Custom source {SourceId} run failed", id);
            run.Status = CustomSourceRunStatus.Failed;
            run.LogText = ex.Message;
        }
        finally
        {
            run.CompletedAt = DateTime.UtcNow;
            source.LastRunAt = run.CompletedAt;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return CustomSourceMapper.ToResponse(run);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private async Task<CustomSource?> LoadAsync(int id, bool tracking, CancellationToken ct)
    {
        var query = _db.CustomSources
            .Include(cs => cs.Operator)
            .Include(cs => cs.Requests).ThenInclude(r => r.Mappings)
            .AsQueryable();

        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(cs => cs.Id == id, ct);
    }

    private static void ApplyRequests(CustomSource source, List<CreateCustomSourceRequestItem> items)
    {
        var order = 0;
        foreach (var item in items)
        {
            ValidateRequestUrl(item.Url);

            var request = new CustomSourceRequest
            {
                SortOrder = order++,
                TargetSection = ParseEnum<TransitSection>(item.TargetSection, nameof(item.TargetSection)),
                Url = item.Url,
                HttpMethod = item.HttpMethod,
                Format = ParseEnum<CustomSourceFormat>(item.Format, nameof(item.Format)),
                DataPath = item.DataPath,
                PaginationConfig = item.PaginationConfig,
                DistinctBy = item.DistinctBy
            };

            var mappingOrder = 0;
            foreach (var mapping in item.Mappings)
            {
                request.Mappings.Add(new CustomSourceMapping
                {
                    SortOrder = mappingOrder++,
                    SourceExpression = mapping.SourceExpression,
                    TargetField = mapping.TargetField,
                    Kind = ParseEnum<MappingKind>(mapping.Kind, nameof(mapping.Kind))
                });
            }

            source.Requests.Add(request);
        }
    }

    private static void ValidateWindow(CustomSource source)
    {
        if (source.ServiceWindowStart is { } s && source.ServiceWindowEnd is { } e && e < s)
            throw new AppException("Service window end is before its start.", 400, "INVALID_SERVICE_WINDOW");
    }

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : throw new AppException(
                $"Invalid {field} '{value}'. Expected one of: {string.Join(", ", Enum.GetNames<T>())}.",
                400, "INVALID_ENUM_VALUE");

    /// <summary>
    /// Accepts an HTTP(S) endpoint or an <c>upload://</c> reference, and says so when it is neither.
    /// The SSRF check on HTTP destinations happens at fetch time, after pagination has been applied.
    /// </summary>
    private static void ValidateRequestUrl(string url)
    {
        if (url.StartsWith(CustomSourceEngine.UploadScheme, StringComparison.OrdinalIgnoreCase))
        {
            if (url.Length <= CustomSourceEngine.UploadScheme.Length)
                throw new AppException("An upload:// URL must name a file.", 400, "INVALID_REQUEST_URL");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new AppException(
                $"'{Truncate(url)}' is not a usable request URL — use http(s):// for an endpoint, or upload://name for an uploaded file.",
                400, "INVALID_REQUEST_URL");
    }

    private static string? Truncate(string? value) =>
        value is null ? null : value.Length <= 80 ? value : value[..80] + "…";
}
