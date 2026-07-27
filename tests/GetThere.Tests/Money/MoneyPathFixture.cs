using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;
using GetThereAPI.Models;
using GetThereAPI.Sdk;

using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GetThere.Tests.Money;

/// <summary>
/// A real SQL Server database. The purchase flow debits with raw SQL
/// (<c>UPDATE Wallets SET Balance = Balance - x WHERE ... AND Balance >= x</c>) and relies on
/// transactions and a filtered unique index — none of which the in-memory provider implements, so
/// testing this against it would prove nothing.
/// </summary>
public sealed class MoneyPathFixture : IDisposable
{
    public const string ConnectionString =
        "Server=localhost;Database=GetThereMoneyPathTests;Trusted_Connection=True;TrustServerCertificate=True";

    public MoneyPathFixture()
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
}

[CollectionDefinition(Name)]
public class MoneyPathCollection : ICollectionFixture<MoneyPathFixture>
{
    public const string Name = "money-path";
}

/// <summary>Adapter test double — returns whatever the test tells it to.</summary>
public sealed class FakeTicketingAdapter : ITicketingAdapter
{
    public const string Type = "fake";

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

    public int PurchaseCallCount { get; private set; }

    public string Name => "Fake";
    public string AdapterType => Type;
    public List<RequiredInput> RequiredInputs => [];

    public Task<PurchaseResult> PurchaseAsync(PurchaseRequest request, CancellationToken ct = default)
    {
        PurchaseCallCount++;
        return Task.FromResult(OnPurchase(request));
    }

    public Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default) =>
        Task.FromResult<TicketPayload?>(null);
}

/// <summary>Builds the rows a purchase needs: a user, a wallet, an adapter and an option.</summary>
public sealed class MoneyPathScenario
{
    public required string UserId { get; init; }
    public required int WalletId { get; init; }
    public required int AdapterId { get; init; }
    public required int OptionId { get; init; }
    public required decimal Price { get; init; }

    public static async Task<MoneyPathScenario> CreateAsync(
        AppDbContext db, decimal balance, decimal price, string currency = "EUR", string walletCurrency = "EUR")
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"probe-{Guid.NewGuid():N}@example.com",
            Email = $"probe-{Guid.NewGuid():N}@example.com",
            FullName = "Money Path Probe"
        };
        db.Users.Add(user);

        var wallet = new Wallet { UserId = user.Id, Balance = balance, Currency = walletCurrency };
        db.Wallets.Add(wallet);

        var adapter = new TicketingAdapter
        {
            Name = "Fake Operator",
            AdapterType = FakeTicketingAdapter.Type,
            TransitInfoGlobalId = "o-fake",
            BaseUrl = "https://fake.local",
            IsActive = true
        };
        db.TicketingAdapters.Add(adapter);
        await db.SaveChangesAsync();

        var option = new TicketOption
        {
            TicketingAdapterId = adapter.Id,
            ExternalProductId = "single",
            Name = "Single ride",
            Price = price,
            Currency = currency,
            TicketFormat = TicketFormat.QR,
            IsActive = true
        };
        db.TicketOptions.Add(option);
        await db.SaveChangesAsync();

        return new MoneyPathScenario
        {
            UserId = user.Id,
            WalletId = wallet.Id,
            AdapterId = adapter.Id,
            OptionId = option.Id,
            Price = price
        };
    }

    public static TicketingManager ManagerFor(AppDbContext db, AdapterRegistry registry) =>
        new(db, registry, NullLogger<TicketingManager>.Instance);

    public static async Task<decimal> BalanceAsync(AppDbContext db, int walletId) =>
        await db.Wallets.AsNoTracking().Where(w => w.Id == walletId).Select(w => w.Balance).FirstAsync();
}
