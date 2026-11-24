using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 04 enhancement suite O: the customer offer filter applied to the game's candidate pool
/// (<c>Customer.GetOrderableProductsWithQuantities</c>) before an offer is chosen and priced. The pool here is a
/// plain list standing in for the game's list (simulated); the filter logic is the production code.
/// </summary>
public class CustomerOfferTests
{
    private static readonly ProductKey Locked = ProductKey.Create("lockedkush");

    private static ProductCatalogSnapshot CatalogWithLocked() => Snapshot(
        Product(OgKush, Weed), Product(SourDiesel, Weed), Product(Gdp, Weed), Product(Locked, Weed, UnlockState.Locked),
        Product(Meth1, Meth), Product(Coke1, Cocaine));

    private static (OfferFilter Filter, InMemoryPolicyProvider Policies) Filter(PolicyConfiguration? policy = null, ProductCatalogSnapshot? catalog = null,
        bool authoritative = true, bool ready = true, bool catalogReady = true)
    {
        var policies = new InMemoryPolicyProvider(policy ?? PolicyConfiguration.Default);
        var snapshot = catalog ?? CatalogWithLocked();
        var filter = new OfferFilter(policies, () => catalogReady ? snapshot : null)
        {
            Authoritative = () => authoritative,
            PolicyReady = () => ready,
        };
        return (filter, policies);
    }

    private static List<string?> Pool(params string?[] ids) => ids.ToList();

    private static List<string?> Run(OfferFilter filter, List<string?> pool)
    {
        filter.Filter(pool.Count, i => pool[i], i => pool.RemoveAt(i));
        return pool;
    }

    private static PolicyConfiguration Selected(params ProductKey[] products)
        => PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, products));

    [Fact]
    public void O01_any_unlocked_keeps_every_unlocked_product()
    {
        var (filter, _) = Filter();
        var pool = Run(filter, Pool("ogkush", "sourdiesel", "granddaddypurple", "meth", "cocaine"));

        Assert.Equal(new[] { "ogkush", "sourdiesel", "granddaddypurple", "meth", "cocaine" }, pool);
        Assert.Equal(0, filter.CandidatesRemoved);
    }

    [Fact]
    public void O02_selected_only_keeps_only_the_selected_products()
    {
        var (filter, _) = Filter(Selected(SourDiesel));
        var pool = Run(filter, Pool("ogkush", "sourdiesel", "granddaddypurple", "meth"));

        Assert.Equal(new[] { "sourdiesel", "meth" }, pool); // other categories keep their own rules
    }

    [Fact]
    public void O03_disabled_category_produces_no_candidates_for_it()
    {
        var (filter, _) = Filter(PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.Disabled, null)));
        var pool = Run(filter, Pool("ogkush", "sourdiesel", "meth"));

        Assert.Equal(new[] { "meth" }, pool);
    }

    [Fact]
    public void O04_global_off_leaves_the_pool_vanilla()
    {
        var (filter, _) = Filter(PolicyConfiguration.Default.WithEnabled(false).WithCategory(Weed, new CategoryPolicy(PolicyMode.Disabled, null)));
        var pool = Run(filter, Pool("ogkush", "lockedkush", "notinthecatalog"));

        Assert.Equal(3, pool.Count);
        Assert.Equal(OfferDecision.Vanilla, filter.Decide("ogkush"));
    }

    [Fact]
    public void O05_settings_not_loaded_leaves_the_pool_vanilla()
    {
        var (filter, _) = Filter(Selected(SourDiesel), ready: false);
        Assert.Equal(2, Run(filter, Pool("ogkush", "sourdiesel")).Count);
    }

    [Fact]
    public void O06_client_or_multiplayer_leaves_the_pool_vanilla()
    {
        var (filter, _) = Filter(Selected(SourDiesel), authoritative: false);
        Assert.Equal(2, Run(filter, Pool("ogkush", "sourdiesel")).Count);
    }

    [Fact]
    public void O07_catalog_not_ready_leaves_the_pool_vanilla()
    {
        var (filter, _) = Filter(Selected(SourDiesel), catalogReady: false);
        Assert.Equal(2, Run(filter, Pool("ogkush", "sourdiesel")).Count);
    }

    [Fact]
    public void O08_locked_products_are_never_offered()
    {
        var (filter, _) = Filter();
        Assert.Equal(new[] { "ogkush" }, Run(filter, Pool("lockedkush", "ogkush")));
        Assert.Equal(OfferDecision.Excluded, filter.Decide("lockedkush"));
    }

    [Fact]
    public void O09_unknown_products_are_excluded_while_the_mod_is_on()
    {
        var (filter, _) = Filter();
        Assert.Equal(new[] { "ogkush" }, Run(filter, Pool("notinthecatalog", "ogkush")));
    }

    [Fact]
    public void O10_null_or_blank_candidates_are_removed()
    {
        var (filter, _) = Filter();
        Assert.Equal(new[] { "ogkush" }, Run(filter, Pool(null, "  ", "ogkush")));
    }

    [Fact]
    public void O11_remaining_order_is_kept_and_emptied_pools_are_counted()
    {
        var (filter, _) = Filter(Selected(Gdp));
        var kept = Run(filter, Pool("granddaddypurple", "ogkush", "meth", "sourdiesel", "cocaine"));
        var emptied = Run(filter, Pool("ogkush", "sourdiesel"));

        Assert.Equal(new[] { "granddaddypurple", "meth", "cocaine" }, kept);
        Assert.Empty(emptied);
        Assert.Equal(4, filter.CandidatesRemoved);
        Assert.Equal(7, filter.CandidatesSeen);
        Assert.Equal(1, filter.PoolsEmptied);
    }

    [Fact]
    public void O12_the_live_policy_is_read_on_every_offer()
    {
        var (filter, policies) = Filter();
        Assert.Equal(2, Run(filter, Pool("ogkush", "sourdiesel")).Count);

        policies.Update(_ => Selected(OgKush));
        Assert.Equal(new[] { "ogkush" }, Run(filter, Pool("ogkush", "sourdiesel")));
    }

    [Fact]
    public void O13_offer_product_quote_and_snapshot_stay_consistent()
    {
        var (filter, policies) = Filter(Selected(SourDiesel, Gdp));
        var pool = Run(filter, Pool("ogkush", "sourdiesel", "granddaddypurple"));
        var chosen = ProductKey.Create(pool[0]); // the game picks from the filtered pool and prices that product
        Assert.True(Tiered.TryGet(chosen, out var price));

        var order = CategoryOrderFactory.Create(Request(ContractA, (chosen, 4)), policies.Current, CatalogWithLocked(), Tiered).Order!;

        Assert.Equal(new[] { chosen }, order.RequestedProducts);
        Assert.Contains(chosen, order.AllowedProducts);
        Assert.Equal(price * 4, order.QuotedValue);
        Assert.DoesNotContain(OgKush, order.AllowedProducts);
    }

    [Fact]
    public void O14_empty_selection_removes_the_whole_category()
    {
        var (filter, _) = Filter(Selected());
        Assert.Equal(new[] { "cocaine" }, Run(filter, Pool("ogkush", "sourdiesel", "cocaine")));
    }

    [Fact]
    public void O15_status_reports_hook_state_and_counts()
    {
        var (filter, _) = Filter(Selected(OgKush));
        Run(filter, Pool("ogkush", "sourdiesel"));

        var lines = filter.StatusLines(hookActive: true);

        Assert.Contains("Customer offer filter: Active", lines);
        Assert.Contains(lines, l => l.StartsWith("Offer candidates removed: 1 of 2"));
        Assert.Contains("Customer offer filter: Unavailable", filter.StatusLines(hookActive: false));
    }
}
