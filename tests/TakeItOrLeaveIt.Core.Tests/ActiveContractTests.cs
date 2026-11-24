using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite D: mid-order rule changes, save switches and persistence of active orders.</summary>
public class ActiveContractTests
{
    private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tioli-orders");

    [Fact]
    public void D01_old_order_keeps_its_snapshot_and_new_orders_use_the_new_rules()
    {
        var h = new SettlementHarness(Policy(p => p.WithCategory(Weed, CategoryPolicy.SelectedOnly(Gdp, OgKush))))
            .Stock("og", OgKush, 20).Stock("sd", SourDiesel, 20);
        var revisionA = h.Accept(ContractA, 100f, (Gdp, 5)).Order!.PolicyRevision;

        // Revision B: OG Kush unchecked, Sour Diesel checked.
        h.Policies.Update(p => p.WithCategory(Weed, CategoryPolicy.SelectedOnly(Gdp, SourDiesel)));
        var orderB = h.Accept(ContractB, 100f, (Gdp, 5)).Order!;

        Assert.NotEqual(revisionA, orderB.PolicyRevision);
        Assert.False(h.Submit(ContractB, ("og", 5)));       // new order: OG Kush no longer allowed
        Assert.True(h.Submit(ContractA, ("og", 5)));        // old order: still allowed by its snapshot
        Assert.True(h.Submit(ContractB, ("sd", 5)));
        Assert.Equal(2, h.PaymentCount);
    }

    [Fact]
    public void D02_disabling_the_category_or_the_mod_does_not_strand_an_active_order()
    {
        var h = new SettlementHarness().Stock("og", OgKush, 20);
        h.Accept(ContractA, 100f, (Gdp, 5));

        h.Policies.Update(p => p.WithCategory(Weed, CategoryPolicy.Disabled).WithEnabled(false));

        Assert.True(h.Submit(ContractA, ("og", 5)));
        Assert.Equal(1, h.PaymentCount);
    }

    [Fact]
    public void D03_new_contracts_after_global_disable_are_vanilla()
    {
        var h = new SettlementHarness(Policy(p => p.WithEnabled(false))).Stock("og", OgKush, 20).Stock("gdp", Gdp, 20);

        Assert.Equal(OrderOutcome.ModDisabled, h.Accept(ContractA, 100f, (Gdp, 5)).Outcome);
        Assert.False(h.Submit(ContractA, ("og", 5)));   // vanilla wants the exact product
        Assert.True(h.Submit(ContractA, ("gdp", 5)));
    }

    [Fact]
    public void D04_product_unlocked_after_acceptance_is_not_added_to_the_snapshot()
    {
        var h = new SettlementHarness(catalog: Snapshot(Standard())).Stock("gdp", Gdp, 5); // GDP locked at acceptance
        h.Accept(ContractA, 100f, (OgKush, 5));

        h.Catalog = AllWeedUnlocked();

        Assert.False(h.Submit(ContractA, ("gdp", 5)));
        Assert.Equal(DeliveryRejection.ProductNotAllowed, h.Coordinator.Book.Orders[0].LastRejection);
    }

    [Fact]
    public void D05_switching_saves_keeps_orders_apart()
    {
        var fs = new MemoryFileSystem();
        var store = new ActiveOrderStore(Root, fs);
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);

        coordinator.BindSave("save-a", null);
        coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5)));
        store.Save("save-a", coordinator.Book.Orders);

        coordinator.BindSave("save-b", store.Load("save-b").Orders);
        Assert.Equal(0, coordinator.Book.Count);
        Assert.Null(coordinator.Validate(ContractA, new[] { Item("0", OgKush, 5) }));

        coordinator.BindSave("save-a", store.Load("save-a").Orders);
        Assert.True(coordinator.Validate(ContractA, new[] { Item("0", Gdp, 5) })!.IsValid);
    }

    [Fact]
    public void D06_reloaded_order_survives_a_restart_including_a_commit_mark()
    {
        var fs = new MemoryFileSystem();
        var store = new ActiveOrderStore(Root, fs);
        var book = new ActiveOrderBook();
        book.TryAdd(new CategoryOrder(ContractA, Weed, 5, new[] { OgKush }, true, 3));
        book.TryAdd(new CategoryOrder(ContractB, Weed, 4, new[] { Gdp }, false, 3));
        book.ObserveCommit(ContractB);
        store.Save("save-a", book.Orders);

        var loaded = store.Load("save-a");

        Assert.Equal(OrdersLoadStatus.Loaded, loaded.Status);
        Assert.Equal(new[] { (ContractA, SaleState.Pending), (ContractB, SaleState.CommitObserved) },
            loaded.Orders.Select(o => (o.Order.ContractId, o.State)));
    }

    [Fact]
    public void D07_complete_cancel_expire_and_vanished_contracts_are_cleaned_up()
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);
        coordinator.BindSave("save-a", null);
        var ids = new[] { "c1", "c2", "c3", "c4", "c5" };
        foreach (var id in ids)
        {
            coordinator.OnContractAccepted(Request(id, (OgKush, 5)));
        }

        coordinator.OnContractEnded("c1", SaleEnd.Completed);
        coordinator.OnContractEnded("c2", SaleEnd.Canceled);
        coordinator.OnContractEnded("c3", SaleEnd.Expired);
        var pruned = coordinator.Prune(new[] { "c5", "unrelated" });

        Assert.Equal(1, pruned);
        Assert.Equal(new[] { "c5" }, coordinator.Book.Orders.Select(o => o.Order.ContractId));
        Assert.True(coordinator.HasUnsavedOrders);
    }

    [Fact]
    public void D08_missing_stale_or_corrupt_metadata_falls_back_to_vanilla()
    {
        var fs = new MemoryFileSystem();
        var store = new ActiveOrderStore(Root, fs);

        fs.Files[store.PathFor("save-a")] = "{ not json";
        var unreadable = store.Load("save-a");
        Assert.Equal(OrdersLoadStatus.Unreadable, unreadable.Status);
        Assert.Empty(unreadable.Orders);

        fs.Files[store.PathFor("save-a")] = "{\"schemaVersion\":1,\"orders\":[" +
            "{\"contract\":\"ok\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\"]}," +
            "{\"contract\":\"ok\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\"]}," +
            "{\"contract\":\"noproducts\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[]}," +
            "{\"contract\":\"badqty\",\"category\":\"marijuana\",\"requiredUnits\":-1,\"allowedProducts\":[\"ogkush\"]}," +
            "{\"contract\":\"bad id!\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\"]}," +
            "42]}";
        var partial = store.Load("save-a");
        Assert.Equal(new[] { "ok" }, partial.Orders.Select(o => o.Order.ContractId));
        Assert.Equal(6, partial.Issues.Count); // 5 broken entries + the legacy (unpriced) note for "ok"

        fs.Files[store.PathFor("save-a")] = "{\"schemaVersion\":99,\"orders\":[]}";
        var future = store.Load("save-a");
        Assert.Empty(future.Orders);
        Assert.Single(future.Issues);
    }

    [Fact]
    public void D09_snapshot_is_taken_once_even_if_acceptance_is_reported_twice()
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);
        coordinator.BindSave("save-a", null);
        var first = coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5))).Order;

        var second = coordinator.OnContractAccepted(Request(ContractA, (OgKush, 9))).Order;

        Assert.Same(first, second);
        Assert.Equal(5, coordinator.Book.Orders.Single().Order.RequiredUnits);
    }

    [Fact]
    public void D10_failed_write_keeps_orders_in_memory_and_the_old_file()
    {
        var fs = new MemoryFileSystem();
        var store = new ActiveOrderStore(Root, fs);
        var book = new ActiveOrderBook();
        book.TryAdd(Order(5));
        store.Save("save-a", book.Orders);
        var before = fs.Files[store.PathFor("save-a")];

        fs.FailWrites = true;
        book.TryAdd(new CategoryOrder(ContractB, Weed, 3, new[] { OgKush }, true, 1));
        Assert.Throws<System.IO.IOException>(() => store.Save("save-a", book.Orders));

        Assert.Equal(before, fs.Files[store.PathFor("save-a")]);
        Assert.Equal(2, book.Count);
        Assert.DoesNotContain(fs.Files.Keys, k => k.EndsWith(".tmp"));
    }

    [Fact]
    public void D11_unsupported_session_leaves_everything_vanilla()
    {
        var log = new RecordingLog();
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked(), log) { Prices = Sales.Flat };
        coordinator.BindSave("save-a", new List<(CategoryOrder, SaleState)> { (Order(5), SaleState.Pending) });

        coordinator.SetAuthority(isServer: true, playerCount: 2);
        Assert.Equal(OrderOutcome.ModDisabled, coordinator.OnContractAccepted(Request(ContractB, (OgKush, 5))).Outcome);
        Assert.Null(coordinator.Validate(ContractA, new[] { Item("0", OgKush, 5) }));

        coordinator.SetAuthority(isServer: false, playerCount: 1);
        Assert.Null(coordinator.Validate(ContractA, new[] { Item("0", OgKush, 5) }));

        coordinator.SetAuthority(isServer: true, playerCount: 1);
        Assert.True(coordinator.Validate(ContractA, new[] { Item("0", OgKush, 5) })!.IsValid);
        Assert.Equal(3, log.Infos.Count(l => l.StartsWith("Player sales authority")));
    }

    [Fact]
    public void D12_no_save_bound_means_no_new_orders()
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);

        Assert.False(coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5))).IsManaged);
    }

    [Fact]
    public void D13_unloaded_or_unreadable_settings_keep_new_contracts_vanilla()
    {
        var ready = false;
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { PolicyReady = () => ready, Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);
        coordinator.BindSave("save-a", null);

        Assert.Equal(OrderOutcome.PolicyNotLoaded, coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5))).Outcome);

        ready = true;
        Assert.True(coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5))).IsManaged);
    }

    [Fact]
    public void D14_menu_reports_unreadable_settings_as_not_ready_for_sales_until_saved()
    {
        var store = new MemoryPolicyStore();
        var harness = new MenuHarness(store: store);
        Assert.False(harness.Menu.PolicyReadyForSales);

        store.Files["save-a"] = "{ not json";
        harness.Load("save-a");
        Assert.False(harness.Menu.PolicyReadyForSales);

        Assert.True(harness.Menu.Save());
        Assert.True(harness.Menu.PolicyReadyForSales);
    }
}
