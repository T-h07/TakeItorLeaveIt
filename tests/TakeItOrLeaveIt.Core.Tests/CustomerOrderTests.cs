using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite A: category orders created at contract acceptance.</summary>
public class CustomerOrderTests
{
    [Fact]
    public void A01_supported_category_becomes_a_category_order()
    {
        var result = CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), Policy(), AllWeedUnlocked(), Sales.Flat);

        Assert.Equal(OrderOutcome.Managed, result.Outcome);
        Assert.Equal(Weed, result.Order!.Category);
        Assert.Equal(new[] { Gdp, OgKush, SourDiesel }.OrderBy(k => k), result.Order.AllowedProducts);
    }

    [Fact]
    public void A02_requested_quantity_is_the_sum_of_the_vanilla_lines()
    {
        var result = CategoryOrderFactory.Create(Request(ContractA, (OgKush, 3), (SourDiesel, 4)), Policy(), AllWeedUnlocked(), Sales.Flat);

        Assert.Equal(7, result.Order!.RequiredUnits);
        Assert.Equal(new[] { OgKush, SourDiesel }, result.Order.RequestedProducts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A02_invalid_quantities_stay_vanilla(int quantity)
    {
        var result = CategoryOrderFactory.Create(Request(ContractA, (OgKush, quantity)), Policy(), AllWeedUnlocked(), Sales.Flat);

        Assert.Equal(OrderOutcome.InvalidContract, result.Outcome);
        Assert.Null(result.Order);
    }

    [Fact]
    public void A03_unknown_product_or_mixed_categories_stay_vanilla()
    {
        Assert.Equal(OrderOutcome.UnknownProduct, CategoryOrderFactory.Create(Request(ContractA, (Unknown, 5)), Policy(), AllWeedUnlocked(), Sales.Flat).Outcome);
        Assert.Equal(OrderOutcome.MixedCategories, CategoryOrderFactory.Create(Request(ContractA, (OgKush, 2), (Meth1, 2)), Policy(), AllWeedUnlocked(), Sales.Flat).Outcome);
    }

    [Fact]
    public void A04_disabled_category_does_not_create_an_order()
    {
        var policy = Policy(p => p.WithCategory(Weed, CategoryPolicy.Disabled));

        var result = CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), policy, AllWeedUnlocked(), Sales.Flat);

        Assert.Equal(OrderOutcome.CategoryNotEligible, result.Outcome);
        Assert.Null(result.Order);
    }

    [Fact]
    public void A05_selected_only_with_nothing_selected_does_not_create_an_unsatisfiable_order()
    {
        var policy = Policy(p => p.WithCategory(Weed, CategoryPolicy.SelectedOnly()));

        Assert.Equal(OrderOutcome.CategoryNotEligible, CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), policy, AllWeedUnlocked(), Sales.Flat).Outcome);
    }

    [Fact]
    public void A05_selected_only_snapshot_holds_exactly_the_selection()
    {
        var policy = Policy(p => p.WithCategory(Weed, CategoryPolicy.SelectedOnly(Gdp)));

        var order = CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), policy, AllWeedUnlocked(), Sales.Flat).Order!;

        Assert.Equal(new[] { Gdp }, order.AllowedProducts);
    }

    [Fact]
    public void A06_global_disable_and_missing_catalog_keep_vanilla()
    {
        Assert.Equal(OrderOutcome.ModDisabled, CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), Policy(p => p.WithEnabled(false)), AllWeedUnlocked(), Sales.Flat).Outcome);
        Assert.Equal(OrderOutcome.CatalogNotReady, CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), Policy(), null, Sales.Flat).Outcome);
        Assert.Equal(OrderOutcome.ModDisabled, CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), null, AllWeedUnlocked(), Sales.Flat).Outcome);
    }

    [Fact]
    public void A07_creation_is_a_single_bounded_pass()
    {
        // Offers stay vanilla; the factory only inspects the given lines once. A huge contract completes immediately.
        var lines = Enumerable.Range(0, 10_000).Select(_ => (OgKush, 1)).ToArray();

        var result = CategoryOrderFactory.Create(Request(ContractA, lines), Policy(), AllWeedUnlocked(), Sales.Flat);

        Assert.Equal(10_000, result.Order!.RequiredUnits);
    }

    [Fact]
    public void A08_contract_identity_is_kept_and_payment_is_never_part_of_the_order()
    {
        var order = CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), Policy(), AllWeedUnlocked(), Sales.Flat).Order!;

        Assert.Equal(ContractA, order.ContractId);
        Assert.DoesNotContain(typeof(CategoryOrder).GetProperties(), p => p.Name.Contains("Payment"));
    }

    [Fact]
    public void A09_dealer_contracts_and_bad_ids_are_never_managed()
    {
        Assert.Equal(OrderOutcome.NotPlayerContract, CategoryOrderFactory.Create(DealerRequest(ContractA, (OgKush, 5)), Policy(), AllWeedUnlocked(), Sales.Flat).Outcome);
        Assert.Equal(OrderOutcome.InvalidContract, CategoryOrderFactory.Create(Request("bad id with spaces", (OgKush, 5)), Policy(), AllWeedUnlocked(), Sales.Flat).Outcome);
        Assert.Equal(OrderOutcome.InvalidContract, CategoryOrderFactory.Create(Request(ContractA), Policy(), AllWeedUnlocked(), Sales.Flat).Outcome);
        Assert.Equal(OrderOutcome.InvalidContract, CategoryOrderFactory.Create(null, Policy(), AllWeedUnlocked(), Sales.Flat).Outcome);
    }

    [Fact]
    public void A09_contracts_accepted_before_the_mod_are_not_reinterpreted()
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);
        coordinator.BindSave("save-test", null);

        Assert.Null(coordinator.Validate(ContractB, new[] { Item("0", OgKush, 5) }));
    }

    [Fact]
    public void A10_order_metadata_round_trips_through_the_order_file()
    {
        var book = new ActiveOrderBook();
        var prices = new Dictionary<ProductKey, decimal> { [SourDiesel] = 45m, [OgKush] = 40m, [Gdp] = 40m };
        var order = new CategoryOrder(ContractA, Weed, 9, new[] { SourDiesel, OgKush }, false, 42, new[] { Gdp },
            PricingBasis.Priced, 360m, prices);
        book.TryAdd(order);

        var issues = new List<string>();
        var loaded = Assert.Single(ActiveOrderStore.FromJson(ActiveOrderStore.ToJson(book.Orders), issues));

        Assert.Empty(issues);
        Assert.Equal((ContractA, Weed, 9, false, 42L), (loaded.Order.ContractId, loaded.Order.Category, loaded.Order.RequiredUnits, loaded.Order.MixedDeliveries, loaded.Order.PolicyRevision));
        Assert.Equal(new[] { OgKush, SourDiesel }, loaded.Order.AllowedProducts);
        Assert.Equal(new[] { Gdp }, loaded.Order.RequestedProducts);
        Assert.Equal(SaleState.Pending, loaded.State);
        Assert.Equal(PricingBasis.Priced, loaded.Order.PricingBasis);
        Assert.Equal(360m, loaded.Order.QuotedValue);
        Assert.Equal(prices.OrderBy(p => p.Key), loaded.Order.UnitPrices);
    }

    [Fact]
    public void Order_rejects_invalid_construction()
    {
        Assert.Throws<System.ArgumentException>(() => new CategoryOrder("", Weed, 1, new[] { OgKush }, true, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new CategoryOrder(ContractA, Weed, 0, new[] { OgKush }, true, 0));
        Assert.Throws<System.ArgumentException>(() => new CategoryOrder(ContractA, Weed, 1, new ProductKey[0], true, 0));
    }
}
