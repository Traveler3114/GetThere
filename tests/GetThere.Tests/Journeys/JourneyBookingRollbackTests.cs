using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;
using GetThereAPI.Models;
using GetThereAPI.Sdk;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GetThere.Tests.Journeys;

[Collection(JourneyBookingCollection.Name)]
public class JourneyBookingRollbackTests
{
    [Fact]
    public async Task Failed_booking_leaves_no_journey_and_restores_wallet()
    {
        using var db = JourneyBookingFixture.CreateContext();
        var scenario = await JourneyBookingFixture.CreateTwoOperatorScenarioAsync(db, balance: 100m);

        var adapter = new FakeJourneyAdapter();
        var registry = new AdapterRegistry();
        registry.Register(FakeJourneyAdapter.Type, adapter);

        var bookingManager = JourneyBookingFixture.CreateBookingManager(db, registry);
        var initialBalance = await BalanceAsync(db, scenario.WalletId);
        var initialReserved = await ReservedAsync(db, scenario.WalletId);

        // Second purchasable leg throws — first leg will have debited, second fails
        var callCount = 0;
        adapter.OnPurchase = _ =>
        {
            callCount++;
            if (callCount == 2)
                throw new HttpRequestException("operator down");
            return new PurchaseResult
            {
                Success = true,
                ExternalPurchaseId = "ext-" + Guid.NewGuid().ToString("N"),
                Ticket = new TicketPayload
                {
                    Format = TicketFormat.QR,
                    Data = "payload",
                    ValidFrom = DateTime.UtcNow,
                    ValidTo = DateTime.UtcNow.AddHours(2)
                }
            };
        };

        var request = new BookJourneyRequest("Trip", scenario.Legs);

        await Assert.ThrowsAnyAsync<Exception>(() => bookingManager.BookAsync(scenario.UserId, request));

        // Re-read with fresh context to see committed state
        using var verify = JourneyBookingFixture.CreateContext();
        Assert.Empty(await verify.Journeys.Where(j => j.UserId == scenario.UserId).ToListAsync());
        Assert.Equal(initialBalance, await BalanceAsync(verify, scenario.WalletId));
        Assert.Equal(initialReserved, await ReservedAsync(verify, scenario.WalletId));
        var tickets = await verify.Tickets.Where(t => t.Purchase.UserId == scenario.UserId).ToListAsync();
        Assert.True(tickets.All(t => t.JourneyId == null), "No ticket should reference the deleted journey");
        // At least the first leg's ticket exists but is refunded; it must not be linked to journey
        // If we expected no tickets at all, the first leg's refund would have deleted it, which it does not.
    }

    [Fact]
    public async Task Long_operator_id_is_truncated_to_64_chars()
    {
        using var db = JourneyBookingFixture.CreateContext();
        var longId = new string('x', 128);
        var scenario = await JourneyBookingFixture.CreateSingleOperatorScenarioAsync(db, balance: 100m, operatorGlobalId: longId);

        var adapter = new FakeJourneyAdapter();
        var registry = new AdapterRegistry();
        registry.Register(FakeJourneyAdapter.Type, adapter);

        var bookingManager = JourneyBookingFixture.CreateBookingManager(db, registry);

        var request = new BookJourneyRequest("LongTrip", scenario.Legs);
        var response = await bookingManager.BookAsync(scenario.UserId, request);

        Assert.NotNull(response);

        using var verify = JourneyBookingFixture.CreateContext();
        var journey = await verify.Journeys.AsNoTracking().FirstOrDefaultAsync(j => j.UserId == scenario.UserId);
        Assert.NotNull(journey);

        var reservations = await verify.JourneyReservations.AsNoTracking().Where(r => r.JourneyId == journey!.Id).ToListAsync();
        foreach (var r in reservations)
            Assert.True((r.WalletHoldReference?.Length ?? 0) <= 64, $"WalletHoldReference too long: {r.WalletHoldReference}");

        var purchases = await verify.Purchases.AsNoTracking().Where(p => p.UserId == scenario.UserId).ToListAsync();
        foreach (var p in purchases)
            Assert.True((p.IdempotencyKey?.Length ?? 0) <= 64, $"IdempotencyKey too long: {p.IdempotencyKey}");
    }

    /// <summary>
    /// A failed booking must leave the wallet exactly as it found it, holds included.
    /// <para>
    /// BookAsync runs every operator purchase before it opens the transaction that places holds, so
    /// a purchase that throws cannot strand a hold — there is none yet. A hold placed inside that
    /// transaction is undone by its rollback rather than by a compensating release, which is why
    /// there must be no Hold and no Release row afterwards: not one of each that cancel out.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Failed_booking_leaves_no_hold_behind()
    {
        using var db = JourneyBookingFixture.CreateContext();
        var scenario = await JourneyBookingFixture.CreateHoldAndPurchaseScenarioAsync(db, balance: 100m);

        var purchasing = new FakeJourneyAdapter();
        purchasing.OnPurchase = _ => throw new HttpRequestException("operator down");

        var registry = new AdapterRegistry();
        registry.Register(FakeJourneyAdapter.Type, purchasing);
        registry.Register(NonPurchasingJourneyAdapter.Type, new NonPurchasingJourneyAdapter());

        var bookingManager = JourneyBookingFixture.CreateBookingManager(db, registry);

        var initialBalance = await BalanceAsync(db, scenario.WalletId);
        var initialReserved = await ReservedAsync(db, scenario.WalletId);

        await Assert.ThrowsAnyAsync<Exception>(
            () => bookingManager.BookAsync(scenario.UserId, new BookJourneyRequest("HoldTrip", scenario.Legs)));

        using var verify = JourneyBookingFixture.CreateContext();
        Assert.Equal(initialBalance, await BalanceAsync(verify, scenario.WalletId));
        Assert.Equal(initialReserved, await ReservedAsync(verify, scenario.WalletId));

        var ledger = await verify.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId)
            .ToListAsync();
        Assert.DoesNotContain(ledger, t => t.Type == WalletTransactionType.Hold);
        Assert.DoesNotContain(ledger, t => t.Type == WalletTransactionType.Release);

        Assert.Empty(await verify.Journeys.AsNoTracking().Where(j => j.UserId == scenario.UserId).ToListAsync());
        Assert.Empty(await verify.JourneyReservations.AsNoTracking()
            .Where(r => r.Journey.UserId == scenario.UserId).ToListAsync());
    }

    private static async Task<decimal> BalanceAsync(AppDbContext db, int walletId) =>
        await db.Wallets.AsNoTracking().Where(w => w.Id == walletId).Select(w => w.Balance).FirstAsync();

    private static async Task<decimal> ReservedAsync(AppDbContext db, int walletId) =>
        await db.Wallets.AsNoTracking().Where(w => w.Id == walletId).Select(w => w.Reserved).FirstAsync();
}

public sealed class JourneyBookingFixture : IDisposable
{
    public static readonly string ConnectionString = TestDatabase.ConnectionStringFor("GetThereJourneyBookingTests");

    public JourneyBookingFixture()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
        db.Database.Migrate();
    }

    public static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }

    public static async Task<JourneyBookingScenario> CreateTwoOperatorScenarioAsync(AppDbContext db, decimal balance)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"jb-{Guid.NewGuid():N}@example.com",
            Email = $"jb-{Guid.NewGuid():N}@example.com",
            FullName = "Journey Booking Probe"
        };
        db.Users.Add(user);
        var wallet = new Wallet { UserId = user.Id, Balance = balance, Currency = "EUR" };
        db.Wallets.Add(wallet);

        var adapter1 = new TicketingAdapter
        {
            Name = "Op A",
            AdapterType = FakeJourneyAdapter.Type,
            TransitInfoGlobalId = "op-a",
            BaseUrl = "https://fake.local",
            IsActive = true
        };
        var adapter2 = new TicketingAdapter
        {
            Name = "Op B",
            AdapterType = FakeJourneyAdapter.Type,
            TransitInfoGlobalId = "op-b",
            BaseUrl = "https://fake.local",
            IsActive = true
        };
        db.TicketingAdapters.AddRange(adapter1, adapter2);
        await db.SaveChangesAsync();

        var opt1 = new TicketOption
        {
            TicketingAdapterId = adapter1.Id,
            ExternalProductId = "single-a",
            Name = "Single A",
            Price = 10m,
            Currency = "EUR",
            TicketFormat = TicketFormat.QR,
            IsActive = true
        };
        var opt2 = new TicketOption
        {
            TicketingAdapterId = adapter2.Id,
            ExternalProductId = "single-b",
            Name = "Single B",
            Price = 10m,
            Currency = "EUR",
            TicketFormat = TicketFormat.QR,
            IsActive = true
        };
        db.TicketOptions.AddRange(opt1, opt2);
        await db.SaveChangesAsync();

        var baseTime = new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);
        var legs = new List<QuoteLegDto>
        {
            new("op-a", "BUS", true, baseTime, baseTime.AddMinutes(30), 0,0,0,0),
            new("op-b", "TRAM", true, baseTime.AddMinutes(30), baseTime.AddMinutes(60), 0,0,0,0),
        };

        return new JourneyBookingScenario
        {
            UserId = user.Id,
            WalletId = wallet.Id,
            AdapterId = adapter1.Id,
            OptionId = opt1.Id,
            Legs = legs
        };
    }

    public static async Task<JourneyBookingScenario> CreateSingleOperatorScenarioAsync(AppDbContext db, decimal balance, string operatorGlobalId)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"jb-{Guid.NewGuid():N}@example.com",
            Email = $"jb-{Guid.NewGuid():N}@example.com",
            FullName = "Journey Booking Probe"
        };
        db.Users.Add(user);
        var wallet = new Wallet { UserId = user.Id, Balance = balance, Currency = "EUR" };
        db.Wallets.Add(wallet);

        var adapter = new TicketingAdapter
        {
            Name = "Long Op",
            AdapterType = FakeJourneyAdapter.Type,
            TransitInfoGlobalId = operatorGlobalId,
            BaseUrl = "https://fake.local",
            IsActive = true
        };
        db.TicketingAdapters.Add(adapter);
        await db.SaveChangesAsync();

        var opt = new TicketOption
        {
            TicketingAdapterId = adapter.Id,
            ExternalProductId = "single",
            Name = "Single",
            Price = 5m,
            Currency = "EUR",
            TicketFormat = TicketFormat.QR,
            IsActive = true
        };
        db.TicketOptions.Add(opt);
        await db.SaveChangesAsync();

        var baseTime = new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);
        var legs = new List<QuoteLegDto> { new(operatorGlobalId, "BUS", true, baseTime, baseTime.AddMinutes(30), 0,0,0,0) };

        return new JourneyBookingScenario
        {
            UserId = user.Id,
            WalletId = wallet.Id,
            AdapterId = adapter.Id,
            OptionId = opt.Id,
            Legs = legs
        };
    }

    /// <summary>
    /// One leg whose operator cannot sell (a hold), followed by one that can (a purchase). The
    /// non-purchasing adapter still has an active option, so the leg is priced — which is what makes
    /// it take the hold branch rather than the unpriced buy-on-board branch.
    /// </summary>
    public static async Task<JourneyBookingScenario> CreateHoldAndPurchaseScenarioAsync(AppDbContext db, decimal balance)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"jb-{Guid.NewGuid():N}@example.com",
            Email = $"jb-{Guid.NewGuid():N}@example.com",
            FullName = "Journey Booking Probe"
        };
        db.Users.Add(user);
        var wallet = new Wallet { UserId = user.Id, Balance = balance, Currency = "EUR" };
        db.Wallets.Add(wallet);

        var holdAdapter = new TicketingAdapter
        {
            Name = "Op Hold",
            AdapterType = NonPurchasingJourneyAdapter.Type,
            TransitInfoGlobalId = "op-hold",
            BaseUrl = "https://fake.local",
            IsActive = true
        };
        var buyAdapter = new TicketingAdapter
        {
            Name = "Op Buy",
            AdapterType = FakeJourneyAdapter.Type,
            TransitInfoGlobalId = "op-buy",
            BaseUrl = "https://fake.local",
            IsActive = true
        };
        db.TicketingAdapters.AddRange(holdAdapter, buyAdapter);
        await db.SaveChangesAsync();

        db.TicketOptions.AddRange(
            new TicketOption
            {
                TicketingAdapterId = holdAdapter.Id, ExternalProductId = "hold-single", Name = "Hold Single",
                Price = 10m, Currency = "EUR", TicketFormat = TicketFormat.QR, IsActive = true
            },
            new TicketOption
            {
                TicketingAdapterId = buyAdapter.Id, ExternalProductId = "buy-single", Name = "Buy Single",
                Price = 10m, Currency = "EUR", TicketFormat = TicketFormat.QR, IsActive = true
            });
        await db.SaveChangesAsync();

        var baseTime = new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);
        var legs = new List<QuoteLegDto>
        {
            new("op-hold", "BUS", true, baseTime, baseTime.AddMinutes(30), 0,0,0,0),
            new("op-buy", "TRAM", true, baseTime.AddMinutes(30), baseTime.AddMinutes(60), 0,0,0,0),
        };

        return new JourneyBookingScenario
        {
            UserId = user.Id,
            WalletId = wallet.Id,
            AdapterId = buyAdapter.Id,
            OptionId = 0,
            Legs = legs
        };
    }

    public static JourneyBookingManager CreateBookingManager(AppDbContext db, AdapterRegistry registry)
    {
        var quoteMgr = new JourneyQuoteManager(db, registry, NullLogger<JourneyQuoteManager>.Instance);
        var ticketingMgr = new TicketingManager(db, registry, NullLogger<TicketingManager>.Instance);
        return new JourneyBookingManager(db, quoteMgr, ticketingMgr, new WalletManager(db, NullLogger<WalletManager>.Instance), NullLogger<JourneyBookingManager>.Instance);
    }
}

public sealed class JourneyBookingScenario
{
    public required string UserId { get; init; }
    public required int WalletId { get; init; }
    public required int AdapterId { get; init; }
    public required int OptionId { get; init; }
    public required List<QuoteLegDto> Legs { get; init; }
}

[CollectionDefinition(Name)]
public class JourneyBookingCollection : ICollectionFixture<JourneyBookingFixture>
{
    public const string Name = "journey-booking";
}

public sealed class FakeJourneyAdapter : ITicketingAdapter
{
    public const string Type = "fake-journey";
    public string Name => "Fake Journey";
    public string AdapterType => Type;
    public List<RequiredInput> RequiredInputs => [];

    public Func<PurchaseRequest, PurchaseResult> OnPurchase { get; set; } = _ => new PurchaseResult
    {
        Success = true,
        ExternalPurchaseId = "ext-" + Guid.NewGuid().ToString("N"),
        Ticket = new TicketPayload
        {
            Format = TicketFormat.QR,
            Data = "payload",
            ValidFrom = DateTime.UtcNow,
            ValidTo = DateTime.UtcNow.AddHours(2)
        }
    };

    public Task<PurchaseResult> PurchaseAsync(PurchaseRequest request, CancellationToken ct = default) =>
        Task.FromResult(OnPurchase(request));

    public Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default) =>
        Task.FromResult<TicketPayload?>(null);

    public Task<PurchaseResult?> FindPurchaseAsync(string purchaseReference, CancellationToken ct = default) =>
        Task.FromResult<PurchaseResult?>(null);
}

/// <summary>An operator that quotes a price but cannot sell — the buy-on-board case that takes a hold.</summary>
public sealed class NonPurchasingJourneyAdapter : ITicketingAdapter
{
    public const string Type = "fake-journey-noBuy";
    public string Name => "Fake Journey (no purchase)";
    public string AdapterType => Type;
    public List<RequiredInput> RequiredInputs => [];
    public bool CanPurchase => false;

    public Task<PurchaseResult> PurchaseAsync(PurchaseRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("This operator cannot sell.");

    public Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default) =>
        Task.FromResult<TicketPayload?>(null);

    public Task<PurchaseResult?> FindPurchaseAsync(string purchaseReference, CancellationToken ct = default) =>
        Task.FromResult<PurchaseResult?>(null);
}
