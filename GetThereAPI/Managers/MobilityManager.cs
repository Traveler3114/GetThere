using System.Collections.Concurrent;
using GetThereAPI.Data;
using GetThereShared.Dtos;
using GetThereAPI.Entities;
using GetThereAPI.Parsers.Mobility;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Singleton background service that polls all active <see cref="MobilityProvider"/> feeds
/// and caches their station lists in memory.
///
/// Poll interval: every 2 minutes (bike stations change slowly compared to vehicle positions).
/// </summary>
public class MobilityManager : BackgroundService
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory  _scopeFactory;
    private readonly IHttpClientFactory    _httpFactory;
    private readonly ILogger<MobilityManager> _logger;

    // providerId → live station list
    private readonly ConcurrentDictionary<int, List<BikeStationDto>> _stations = new();

    public MobilityManager(
        IServiceScopeFactory  scopeFactory,
        IHttpClientFactory    httpFactory,
        ILogger<MobilityManager> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _logger       = logger;
    }

    // ── Public read API ───────────────────────────────────────────────────────

    /// <summary>Returns all cached bike stations across every provider.</summary>
    public List<BikeStationDto> GetAllStations()
        => _stations.Values.SelectMany(s => s).ToList();

    /// <summary>Returns cached stations for one provider, or an empty list.</summary>
    public List<BikeStationDto> GetStations(int providerId)
        => _stations.TryGetValue(providerId, out var list) ? list : [];

    // ── Background loop ───────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial fetch before accepting the first request
        await PollAllProvidersAsync(stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAllProvidersAsync(stoppingToken);
        }
    }

    private async Task PollAllProvidersAsync(CancellationToken ct)
    {
        List<MobilityProvider> providers;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            providers = await db.MobilityProviders.AsNoTracking().ToListAsync(ct);
        }

        if (providers.Count == 0) return;

        using var http = _httpFactory.CreateClient();

        var tasks = providers.Select(p => FetchProviderAsync(p, http, ct));
        await Task.WhenAll(tasks);
    }

    private async Task FetchProviderAsync(
        MobilityProvider provider, HttpClient http, CancellationToken ct)
    {
        try
        {
            var parser   = MobilityParserFactory.GetParser(provider);
            var stations = await parser.ParseStationsAsync(provider, http);
            _stations[provider.Id] = stations;

            _logger.LogDebug(
                "MobilityManager: loaded {Count} stations for provider '{Name}' (id={Id})",
                stations.Count, provider.Name, provider.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MobilityManager: failed to fetch stations for provider '{Name}' (id={Id})",
                provider.Name, provider.Id);
            // Keep stale data rather than clearing it on transient error
        }
    }
}
