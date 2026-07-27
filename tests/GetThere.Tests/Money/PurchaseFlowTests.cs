using GetThereAPI.Exceptions;
using GetThereAPI.Models;
using GetThereAPI.Sdk;

using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThere.Tests.Money;

/// <summary>
/// The purchase flow moves real money, so each defect it used to have gets a test.
/// <para>
/// Origin: a failed purchase debited the wallet and committed the debit before throwing — no ticket,
/// no refund, no record of the loss. Because no <c>ITicketingAdapter</c> was ever registered, that
/// was the outcome of <b>every</b> purchase.
/// </para>
/// </summary>
[Collection(MoneyPathCollection.Name)]
public class PurchaseFlowTests
{
    private static AdapterRegistry RegistryWith(FakeTicketingAdapter adapter)
    {
        var registry = new AdapterRegistry();
        registry.Register(FakeTicketingAdapter.Type, adapter);
        return registry;
    }

    [Fact]
    public async Task Successful_purchase_debits_once_and_issues_a_ticket()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter()));

        var ticket = await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId);

        Assert.NotNull(ticket);
        Assert.Equal(37.50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));

        var purchase = await db.Purchases.AsNoTracking().SingleAsync(p => p.UserId == scenario.UserId);
        Assert.Equal(PaymentStatus.Completed, purchase.Status);

        var ledger = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId).ToListAsync();
        Assert.Single(ledger);
        Assert.Equal(-12.50m, ledger[0].Amount);
        Assert.Equal(37.50m, ledger[0].BalanceAfter);
    }

    [Fact]
    public async Task Adapter_failure_restores_the_balance_and_records_a_refund()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var adapter = new FakeTicketingAdapter
        {
            OnPurchase = _ => new PurchaseResult { Success = false, ErrorMessage = "Operator declined" }
        };
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId));

        Assert.Contains("Operator declined", ex.Message, StringComparison.Ordinal);

        // The whole point: the user must not be out of pocket.
        Assert.Equal(50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));

        var purchase = await db.Purchases.AsNoTracking().SingleAsync(p => p.UserId == scenario.UserId);
        Assert.Equal(PaymentStatus.Refunded, purchase.Status);

        // Debit and reversal are both visible in the ledger rather than the debit vanishing.
        var ledger = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId).OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(WalletTransactionType.TicketPurchase, ledger[0].Type);
        Assert.Equal(-12.50m, ledger[0].Amount);
        Assert.Equal(WalletTransactionType.Refund, ledger[1].Type);
        Assert.Equal(12.50m, ledger[1].Amount);
        Assert.Equal(50m, ledger[1].BalanceAfter);

        Assert.Empty(await db.Tickets.AsNoTracking().Where(x => x.Purchase.UserId == scenario.UserId).ToListAsync());
    }

    [Fact]
    public async Task Adapter_throwing_also_restores_the_balance()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 20m, price: 5m);
        var adapter = new FakeTicketingAdapter
        {
            OnPurchase = _ => throw new HttpRequestException("connection reset")
        };
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        await Assert.ThrowsAsync<AppException>(() =>
            manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId));

        Assert.Equal(20m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Equal(PaymentStatus.Refunded,
            (await db.Purchases.AsNoTracking().SingleAsync(p => p.UserId == scenario.UserId)).Status);
    }

    [Fact]
    public async Task No_registered_adapter_fails_before_any_money_moves()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 30m, price: 7m);

        // Empty registry — the state the application actually ships in.
        var manager = MoneyPathScenario.ManagerFor(db, new AdapterRegistry());

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(30m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Empty(await db.Purchases.AsNoTracking().Where(p => p.UserId == scenario.UserId).ToListAsync());
        Assert.Empty(await db.WalletTransactions.AsNoTracking().Where(x => x.WalletId == scenario.WalletId).ToListAsync());
    }

    [Fact]
    public async Task Insufficient_balance_is_rejected_without_a_debit()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 3m, price: 10m);
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter()));

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(3m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Empty(await db.WalletTransactions.AsNoTracking().Where(x => x.WalletId == scenario.WalletId).ToListAsync());
    }

    [Fact]
    public async Task A_wallet_in_another_currency_is_rejected()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(
            db, balance: 100m, price: 10m, currency: "USD", walletCurrency: "EUR");
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter()));

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId));

        Assert.Equal("CURRENCY_MISMATCH", ex.ErrorCode);
        Assert.Equal(100m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_charges_once()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 40m, price: 9m);
        var adapter = new FakeTicketingAdapter();
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));
        const string key = "retry-key-0001";

        var first = await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId, key);
        var second = await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId, key);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(31m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Equal(1, adapter.PurchaseCallCount);
        Assert.Single(await db.Purchases.AsNoTracking().Where(p => p.UserId == scenario.UserId).ToListAsync());
        Assert.Single(await db.Tickets.AsNoTracking().Where(x => x.Purchase.UserId == scenario.UserId).ToListAsync());
    }

    [Fact]
    public async Task Different_idempotency_keys_are_separate_purchases()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 40m, price: 9m);
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter()));

        await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId, "key-a-0001");
        await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId, "key-b-0002");

        Assert.Equal(22m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Equal(2, (await db.Purchases.AsNoTracking().Where(p => p.UserId == scenario.UserId).ToListAsync()).Count);
    }

    [Fact]
    public async Task Concurrent_purchases_cannot_overdraw_the_wallet()
    {
        using var setup = MoneyPathFixture.CreateContext();
        // Enough for exactly two of the three attempts.
        var scenario = await MoneyPathScenario.CreateAsync(setup, balance: 20m, price: 10m);

        var attempts = Enumerable.Range(0, 3).Select(async _ =>
        {
            using var db = MoneyPathFixture.CreateContext();
            var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter()));
            try
            {
                await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId);
                return true;
            }
            catch (AppException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(2, results.Count(succeeded => succeeded));
        Assert.Equal(0m, await MoneyPathScenario.BalanceAsync(setup, scenario.WalletId));
    }
}
