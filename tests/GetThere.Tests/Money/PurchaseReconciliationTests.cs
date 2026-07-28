using GetThereAPI.Models;
using GetThereAPI.Sdk;

using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThere.Tests.Money;

/// <summary>
/// Recovery of purchases that were paid for but never resolved.
/// <para>
/// Origin: the wallet debit commits before the operator is called, so a process that died in that
/// window left a Pending purchase — money taken, no ticket, and nothing in the system that would
/// ever settle it. The comment on the purchase flow called that state "recoverable", but no code
/// recovered it; it waited for a human to notice the row in the admin console.
/// </para>
/// </summary>
[Collection(MoneyPathCollection.Name)]
public class PurchaseReconciliationTests
{
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Cutoff = TimeSpan.FromMinutes(5);

    private static AdapterRegistry RegistryWith(FakeTicketingAdapter adapter)
    {
        var registry = new AdapterRegistry();
        registry.Register(FakeTicketingAdapter.Type, adapter);
        return registry;
    }

    [Fact]
    public async Task A_purchase_the_operator_never_saw_is_refunded()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var purchaseId = await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, Stale);

        // The money is gone and there is no ticket — the state this whole mechanism exists for.
        Assert.Equal(37.50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));

        var adapter = new FakeTicketingAdapter { OnFind = _ => null };
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        var settled = await manager.ReconcilePendingPurchasesAsync(Cutoff);

        Assert.Equal(1, settled);
        Assert.Equal(1, adapter.FindCallCount);
        Assert.Equal(50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));

        var purchase = await db.Purchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId);
        Assert.Equal(PaymentStatus.Refunded, purchase.Status);

        var ledger = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId).OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(WalletTransactionType.Refund, ledger[1].Type);
        Assert.Equal(12.50m, ledger[1].Amount);
    }

    [Fact]
    public async Task A_ticket_the_operator_did_issue_is_recovered_rather_than_refunded()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var purchaseId = await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, Stale);

        var adapter = new FakeTicketingAdapter
        {
            OnFind = reference => new PurchaseResult
            {
                Success = true,
                ExternalPurchaseId = $"ext-for-{reference}",
                Ticket = new TicketPayload
                {
                    Format = TicketFormat.QR,
                    Data = "recovered-payload",
                    ValidFrom = DateTime.UtcNow,
                    ValidTo = DateTime.UtcNow.AddHours(2)
                }
            }
        };
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        var settled = await manager.ReconcilePendingPurchasesAsync(Cutoff);

        Assert.Equal(1, settled);

        // The user paid and now has what they paid for; refunding here would have given away a ticket.
        Assert.Equal(37.50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));

        var purchase = await db.Purchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId);
        Assert.Equal(PaymentStatus.Completed, purchase.Status);
        Assert.Equal($"ext-for-{purchaseId}", purchase.ExternalPurchaseId);
        Assert.NotNull(purchase.CompletedAt);

        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.PurchaseId == purchaseId);
        Assert.Equal("recovered-payload", ticket.Data);
        Assert.Equal(TicketStatus.Active, ticket.Status);

        // No refund row: the debit stands.
        Assert.Single(await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId).ToListAsync());
    }

    [Fact]
    public async Task An_operator_that_cannot_be_reached_leaves_the_purchase_alone()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var purchaseId = await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, Stale);

        var adapter = new FakeTicketingAdapter
        {
            OnFind = _ => throw new HttpRequestException("connection reset")
        };
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        var settled = await manager.ReconcilePendingPurchasesAsync(Cutoff);

        // Refunding on an unknown answer risks giving back money for a ticket that was issued.
        // Waiting for the next sweep is the safe move, so nothing should have changed.
        Assert.Equal(0, settled);
        Assert.Equal(37.50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Equal(PaymentStatus.Pending,
            (await db.Purchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId)).Status);
    }

    [Fact]
    public async Task An_unregistered_adapter_leaves_the_purchase_alone()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var purchaseId = await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, Stale);

        var manager = MoneyPathScenario.ManagerFor(db, new AdapterRegistry());

        var settled = await manager.ReconcilePendingPurchasesAsync(Cutoff);

        Assert.Equal(0, settled);
        Assert.Equal(PaymentStatus.Pending,
            (await db.Purchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId)).Status);
    }

    [Fact]
    public async Task A_purchase_still_in_flight_is_not_touched()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);

        // Two seconds old: an adapter call that is simply slow, not one that died.
        var purchaseId = await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, TimeSpan.FromSeconds(2));

        var adapter = new FakeTicketingAdapter { OnFind = _ => null };
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        var settled = await manager.ReconcilePendingPurchasesAsync(Cutoff);

        // Sweeping this would refund a purchase that is about to succeed, and the user would end up
        // with a ticket they were not charged for.
        Assert.Equal(0, settled);
        Assert.Equal(0, adapter.FindCallCount);
        Assert.Equal(PaymentStatus.Pending,
            (await db.Purchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId)).Status);
    }

    [Fact]
    public async Task Reconciling_twice_refunds_once()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, Stale);

        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter { OnFind = _ => null }));

        await manager.ReconcilePendingPurchasesAsync(Cutoff);
        await manager.ReconcilePendingPurchasesAsync(Cutoff);

        // The second pass finds nothing Pending, but even if it did the refund guard holds: crediting
        // twice would hand out money that was never taken.
        Assert.Equal(50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Single(await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId && t.Type == WalletTransactionType.Refund).ToListAsync());
    }

    [Fact]
    public async Task A_failed_refund_is_not_repeated_when_the_sweep_retries()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 12.50m);
        var purchaseId = await MoneyPathScenario.StrandPendingPurchaseAsync(db, scenario, Stale);

        // The live path refunded and credited the wallet, but died before the purchase row was
        // flipped — so the sweep still sees it as Pending and will try to refund it again.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Wallets SET Balance = Balance + {scenario.Price} WHERE Id = {scenario.WalletId}");
        db.WalletTransactions.Add(new GetThereAPI.Entities.WalletTransaction
        {
            WalletId = scenario.WalletId,
            Amount = scenario.Price,
            BalanceBefore = 37.50m,
            BalanceAfter = 50m,
            Type = WalletTransactionType.Refund,
            Description = "Refund: interrupted",
            ReferenceId = purchaseId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(new FakeTicketingAdapter { OnFind = _ => null }));

        await manager.ReconcilePendingPurchasesAsync(Cutoff);

        // Balance restored exactly once, and the purchase is now closed out properly.
        Assert.Equal(50m, await MoneyPathScenario.BalanceAsync(db, scenario.WalletId));
        Assert.Single(await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == scenario.WalletId && t.Type == WalletTransactionType.Refund).ToListAsync());
        Assert.Equal(PaymentStatus.Refunded,
            (await db.Purchases.AsNoTracking().SingleAsync(p => p.Id == purchaseId)).Status);
    }

    [Fact]
    public async Task The_live_purchase_sends_a_reference_the_operator_can_be_asked_about_later()
    {
        using var db = MoneyPathFixture.CreateContext();
        var scenario = await MoneyPathScenario.CreateAsync(db, balance: 50m, price: 10m);
        var adapter = new FakeTicketingAdapter();
        var manager = MoneyPathScenario.ManagerFor(db, RegistryWith(adapter));

        await manager.PurchaseTicketAsync(scenario.UserId, scenario.AdapterId, scenario.OptionId);

        // Recovery is only possible if the operator was given our purchase id in the first place.
        var purchase = await db.Purchases.AsNoTracking().SingleAsync(p => p.UserId == scenario.UserId);
        Assert.Equal(
            purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Assert.Single(adapter.SeenPaymentReferences));
    }
}
