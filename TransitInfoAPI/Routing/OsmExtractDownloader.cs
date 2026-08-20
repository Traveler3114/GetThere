using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace TransitInfoAPI.Routing;

/// <summary>One region's refresh outcome, for the manual trigger and the worker log.</summary>
public sealed record OsmExtractDownloadResult(string Region, string Path, bool Changed, string? Note);

/// <summary>
/// Keeps the Geofabrik OSM extracts this deployment needs on disk and current. Mini-spec contract:
/// streamed download to a <c>.tmp</c> file, MD5 change-detection against Geofabrik's published
/// <c>.md5</c> sidecar, atomic move into place, and no re-download when the hash is unchanged.
/// <para>
/// Output is deterministic per region — <c>{name}-latest.osm.pbf</c> in
/// <c>OsmExtract.OutputDirectory</c> (falling back to the routing export directory, then temp) — so
/// a graph build consuming that path always sees the freshest extract this host knows about.
/// </para>
/// </summary>
public sealed class OsmExtractDownloader
{
    private readonly IOptionsMonitor<RoutingOptions> _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OsmExtractDownloader> _logger;

    public OsmExtractDownloader(
        IOptionsMonitor<RoutingOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<OsmExtractDownloader> logger)
    {
        _options = options;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Downloads every configured region, skipping any whose MD5 is unchanged. Never throws mid-list.</summary>
    public async Task<IReadOnlyList<OsmExtractDownloadResult>> RefreshAllAsync(CancellationToken ct)
    {
        var results = new List<OsmExtractDownloadResult>();
        foreach (var region in _options.CurrentValue.OsmExtract.Regions)
        {
            try
            {
                results.Add(await RefreshRegionAsync(region, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "OSM extract refresh failed for region {Region}", region.Name);
                results.Add(new OsmExtractDownloadResult(region.Name, OutputPath(region.Name), false,
                    $"failed: {ex.Message}"));
            }
        }
        return results;
    }

    private async Task<OsmExtractDownloadResult> RefreshRegionAsync(OsmRegionOptions region, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(region.Name) || string.IsNullOrWhiteSpace(region.GeofabrikPath))
            return new OsmExtractDownloadResult(region.Name, OutputPath(region.Name), false, "region is not configured");

        var outputPath = OutputPath(region.Name);
        var client = _httpFactory.CreateClient("osm-extract");
        var baseUrl = $"https://download.geofabrik.de/{region.GeofabrikPath.Trim('/')}-latest.osm.pbf";

        var md5 = await FetchSidecarAsync(client, $"{baseUrl}.md5", ct);
        _logger.LogDebug("Region {Region}: Geofabrik MD5 {Md5}", region.Name, md5);

        if (File.Exists(outputPath) && FileMd5(outputPath) == md5)
            return new OsmExtractDownloadResult(region.Name, outputPath, false, "unchanged — no download needed");

        // 1.5 GB regions exist; stream without buffering the body, and be generous on total time.
        using var response = await client.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var tmpPath = outputPath + ".tmp";
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var sink = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await source.CopyToAsync(sink, ct);

        var downloadedMd5 = FileMd5(tmpPath);
        if (downloadedMd5 != md5)
        {
            File.Delete(tmpPath);
            throw new InvalidDataException(
                $"Downloaded extract MD5 {downloadedMd5} does not match the published {md5} — deleted.");
        }

        // Atomic enough for a consumer that opens the file once per build: the move is a rename.
        File.Move(tmpPath, outputPath, overwrite: true);
        _logger.LogInformation("OSM extract for {Region} refreshed at {Path} ({Bytes} bytes)",
            region.Name, outputPath, new FileInfo(outputPath).Length);
        return new OsmExtractDownloadResult(region.Name, outputPath, true, "downloaded");
    }

    private static async Task<string> FetchSidecarAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
        var firstToken = text.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (firstToken.Length == 32 && firstToken.All(Uri.IsHexDigit))
            return firstToken;
        throw new InvalidDataException($"Unexpected Geofabrik MD5 sidecar content: '{text}'");
    }

    private string OutputPath(string regionName)
    {
        var dir = _options.CurrentValue.OsmExtract.OutputDirectory
            ?? _options.CurrentValue.Export.OutputDirectory
            ?? Path.GetTempPath();
        return Path.Combine(dir, $"{regionName}-latest.osm.pbf");
    }

    // MD5 is Geofabrik's own change-detection format; this compares, it does not sign.
#pragma warning disable CA5351
    private static string FileMd5(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }
#pragma warning restore CA5351
}