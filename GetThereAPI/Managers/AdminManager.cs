using System.Globalization;

using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Sdk;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class AdminManager
{
    /// <summary>Failure ratio inside the reporting window above which an adapter reads as degraded.</summary>
    private const double DegradedFailureRatio = 0.02;

    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly AdapterRegistry _adapterRegistry;
    private readonly IHttpContextAccessor _httpContext;

    public AdminManager(AppDbContext db, UserManager<AppUser> userManager, AdapterRegistry adapterRegistry, IHttpContextAccessor httpContext)
    {
        _db = db;
        _userManager = userManager;
        _adapterRegistry = adapterRegistry;
        _httpContext = httpContext;
    }

    private string? CurrentUserId => _httpContext.HttpContext?.User.FindFirst(JwtClaimTypes.UserId)?.Value;

    public async Task<PagedResult<UserListItem>> GetUsersAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _userManager.Users.OrderBy(u => u.Email).AsNoTracking();

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserListItem
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName,
                CreatedAt = u.CreatedAt,
                LastLogin = u.LastLogin
            })
            .ToListAsync(ct);

        return new PagedResult<UserListItem>(items, totalCount, page, pageSize);
    }

    /// <summary>Overview KPIs over a rolling window, each compared against the window before it.</summary>
    public async Task<AdminStats> GetStatsAsync(int windowHours = 24, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddHours(-windowHours);
        var previousStart = windowStart.AddHours(-windowHours);

        var purchases = _db.Purchases.AsNoTracking();
        var current = purchases.Where(p => p.PurchasedAt >= windowStart);
        var previous = purchases.Where(p => p.PurchasedAt >= previousStart && p.PurchasedAt < windowStart);

        var sold = await current.CountAsync(p => p.Status == PaymentStatus.Completed, ct);
        var soldPrevious = await previous.CountAsync(p => p.Status == PaymentStatus.Completed, ct);

        var volume = await current.Where(p => p.Status == PaymentStatus.Completed).SumAsync(p => p.Amount, ct);
        var volumePrevious = await previous.Where(p => p.Status == PaymentStatus.Completed).SumAsync(p => p.Amount, ct);

        var failed = await current.CountAsync(p => p.Status == PaymentStatus.Failed, ct);
        var failedPrevious = await previous.CountAsync(p => p.Status == PaymentStatus.Failed, ct);

        // Daily purchase counts for the KPI sparkline: seven buckets, oldest first.
        var sparklineStart = now.Date.AddDays(-6);
        var daily = await purchases
            .Where(p => p.Status == PaymentStatus.Completed && p.PurchasedAt >= sparklineStart)
            .GroupBy(p => p.PurchasedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var walletTransactions = _db.WalletTransactions.AsNoTracking().Where(t => t.CreatedAt >= windowStart);

        // Aggregate in SQL — materialising every pending purchase to call Count()/Min() on it grows
        // unbounded with the size of the backlog.
        var pendingQuery = purchases.Where(p => p.Status == PaymentStatus.Pending);
        var pendingCount = await pendingQuery.CountAsync(ct);
        var oldestPendingAt = pendingCount == 0
            ? null
            : await pendingQuery.MinAsync(p => (DateTime?)p.PurchasedAt, ct);

        var activeAdapters = await _db.TicketingAdapters.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new
            {
                a.AdapterType,
                Options = a.TicketOptions.Count(o => o.IsActive),
                Failures = _db.Purchases.Count(p =>
                    p.TicketingAdapterId == a.Id && p.Status == PaymentStatus.Failed && p.PurchasedAt >= windowStart)
            })
            .ToListAsync(ct);

        return new AdminStats
        {
            // Assigned rather than left to the contract's default, which is what it relied on
            // before. The money figures below are plain SUMs over Amount/Balance with no conversion,
            // so they are only meaningful while every wallet and price shares one currency — which
            // TicketingManager enforces per purchase (CURRENCY_MISMATCH) but nothing enforces
            // across accounts. A deployment that genuinely mixes currencies needs these bucketed per
            // currency; this at least stops the console labelling every total "EUR" by accident.
            Currency = SupportedCurrencies.Default,

            TicketsSold = sold,
            TicketsSoldChangePercent = PercentChange(sold, soldPrevious),
            TicketsSoldDaily = [.. Enumerable.Range(0, 7)
                .Select(offset => sparklineStart.AddDays(offset))
                .Select(day => daily.FirstOrDefault(d => d.Day == day)?.Count ?? 0)],

            GrossVolume = volume,
            GrossVolumeChangePercent = PercentChange((double)volume, (double)volumePrevious),
            AverageBasket = sold > 0 ? decimal.Round(volume / sold, 2) : 0m,
            Refunds = await current.CountAsync(p => p.Status == PaymentStatus.Refunded, ct),

            WalletFloat = await _db.Wallets.AsNoTracking().SumAsync(w => w.Balance, ct),
            TopUps = await walletTransactions.Where(t => t.Type == WalletTransactionType.Deposit).SumAsync(t => Math.Abs(t.Amount), ct),
            Spend = await walletTransactions.Where(t => t.Type == WalletTransactionType.TicketPurchase).SumAsync(t => Math.Abs(t.Amount), ct),

            PurchaseSuccessRate = SuccessRate(sold, failed),
            PurchaseSuccessRateChangePercent = SuccessRate(sold, failed) - SuccessRate(soldPrevious, failedPrevious),

            PendingPurchases = pendingCount,
            OldestPendingPurchaseAt = oldestPendingAt,

            TotalUsers = await _userManager.Users.CountAsync(ct),
            TotalTickets = await _db.Tickets.AsNoTracking().CountAsync(ct),
            AdaptersDegraded = activeAdapters.Count(a => a.Failures > 0 || !_adapterRegistry.HasAdapter(a.AdapterType)),
            AdaptersWithoutOptions = activeAdapters.Count(a => a.Options == 0)
        };
    }

    /// <summary>Purchase feed, newest first. <paramref name="status"/> is a <see cref="PaymentStatus"/> name.</summary>
    public async Task<PagedResult<PurchaseListItem>> GetPurchasesAsync(
        string? status = null, int? adapterId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _db.Purchases.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var parsed))
        {
            query = query.Where(p => p.Status == parsed);
        }

        if (adapterId is not null)
        {
            query = query.Where(p => p.TicketingAdapterId == adapterId);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.PurchasedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PurchaseListItem
            {
                Id = p.Id,
                UserEmail = p.User.Email,
                OperatorName = p.Adapter.Name,
                AdapterType = p.Adapter.AdapterType,
                OptionName = p.TicketOption.Name,
                Amount = p.Amount,
                Currency = p.Currency,
                PaymentStatus = p.Status.ToString(),
                PurchasedAt = p.PurchasedAt,
                FailureReason = p.FailureReason,
                TicketId = _db.Tickets.Where(t => t.PurchaseId == p.Id).Select(t => (int?)t.Id).FirstOrDefault(),
                ExternalTicketId = _db.Tickets.Where(t => t.PurchaseId == p.Id).Select(t => t.ExternalTicketId).FirstOrDefault(),
                TicketStatus = _db.Tickets.Where(t => t.PurchaseId == p.Id).Select(t => t.Status.ToString()).FirstOrDefault()
            })
            .ToListAsync(ct);

        return new PagedResult<PurchaseListItem>(items, totalCount, page, pageSize);
    }

    /// <summary>Configured adapters with their traffic over a rolling window and a derived status.</summary>
    public async Task<List<AdapterHealthItem>> GetAdapterHealthAsync(int windowHours = 24, CancellationToken ct = default)
    {
        var windowStart = DateTime.UtcNow.AddHours(-windowHours);

        var adapters = await _db.TicketingAdapters.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AdapterHealthItem
            {
                Id = a.Id,
                Name = a.Name,
                AdapterType = a.AdapterType,
                TransitInfoGlobalId = a.TransitInfoGlobalId,
                BaseUrl = a.BaseUrl,
                IsActive = a.IsActive,
                HasApiKey = a.ApiKeyEncrypted != null,
                TicketOptions = a.TicketOptions.Count(o => o.IsActive),
                Purchases = _db.Purchases.Count(p => p.TicketingAdapterId == a.Id && p.PurchasedAt >= windowStart),
                Failures = _db.Purchases.Count(p => p.TicketingAdapterId == a.Id && p.PurchasedAt >= windowStart && p.Status == PaymentStatus.Failed),
                Pending = _db.Purchases.Count(p => p.TicketingAdapterId == a.Id && p.Status == PaymentStatus.Pending),
                Volume = _db.Purchases
                    .Where(p => p.TicketingAdapterId == a.Id && p.PurchasedAt >= windowStart && p.Status == PaymentStatus.Completed)
                    .Sum(p => (decimal?)p.Amount) ?? 0m,
                LastPurchaseAt = _db.Purchases
                    .Where(p => p.TicketingAdapterId == a.Id)
                    .Max(p => (DateTime?)p.PurchasedAt)
            })
            .ToListAsync(ct);

        foreach (var adapter in adapters)
        {
            var implementation = _adapterRegistry.Get(adapter.AdapterType);
            adapter.IsRegistered = implementation is not null;
            adapter.RequiredInputs = implementation is null
                ? []
                : [.. implementation.RequiredInputs.Select(i => i.Key)];
            adapter.Status = DeriveStatus(adapter);
        }

        return adapters;
    }

    /// <summary>
    /// Enables or disables an adapter. Disabling hides its options from the app without
    /// touching existing tickets. Returns null when the adapter does not exist.
    /// </summary>
    public async Task<AdapterHealthItem?> SetAdapterActiveAsync(int adapterId, bool isActive, CancellationToken ct = default)
    {
        var adapter = await _db.TicketingAdapters.FirstOrDefaultAsync(a => a.Id == adapterId, ct);
        if (adapter is null) return null;

        if (adapter.IsActive != isActive)
        {
            _db.Set<AuditLog>().Add(new AuditLog
            {
                UserId = CurrentUserId,
                Action = isActive ? "EnableAdapter" : "DisableAdapter",
                EntityType = nameof(TicketingAdapter),
                EntityId = adapter.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = $"{{\"isActive\":{(adapter.IsActive ? "true" : "false")}}}",
                NewValues = $"{{\"isActive\":{(isActive ? "true" : "false")}}}"
            });

            adapter.IsActive = isActive;
            await _db.SaveChangesAsync(ct);
        }

        var health = await GetAdapterHealthAsync(24, ct);
        return health.FirstOrDefault(a => a.Id == adapterId);
    }

    private static string DeriveStatus(AdapterHealthItem adapter)
    {
        if (!adapter.IsActive) return "Disabled";
        if (!adapter.IsRegistered) return "Unregistered";
        if (adapter.Purchases == 0) return "Idle";

        var failureRatio = (double)adapter.Failures / adapter.Purchases;
        if (failureRatio >= 0.5) return "Failing";
        return failureRatio > DegradedFailureRatio ? "Degraded" : "Ok";
    }

    private static double PercentChange(double current, double previous)
        => previous == 0 ? (current > 0 ? 100 : 0) : Math.Round((current - previous) / previous * 100, 1);

    private static double SuccessRate(int completed, int failed)
    {
        var attempts = completed + failed;
        return attempts == 0 ? 100 : Math.Round((double)completed / attempts * 100, 1);
    }

    public async Task<PagedResult<AuditLogEntry>> GetAuditLogsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var query = _db.AuditLogs
            .Include(al => al.User)
            .OrderByDescending(al => al.CreatedAt)
            .AsNoTracking();

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(al => new AuditLogEntry
            {
                Id = al.Id,
                UserId = al.UserId,
                UserEmail = al.User != null ? al.User.Email : null,
                Action = al.Action,
                EntityType = al.EntityType,
                EntityId = al.EntityId,
                OldValues = al.OldValues,
                NewValues = al.NewValues,
                CreatedAt = al.CreatedAt
            })
            .ToListAsync(ct);

        return new PagedResult<AuditLogEntry>(items, totalCount, page, pageSize);
    }
}
