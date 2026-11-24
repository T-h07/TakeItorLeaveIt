using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 04 enhancement suite P: the fixed-quote fair-price rule. Prices are test data (<see cref="Sales.Tiered"/>:
/// OG Kush 35, Sour Diesel 45, GDP 50), not game values. Settlement cases use the simulated harnesses.
/// </summary>
public class FairPricingTests
{
    private static CategoryOrder Create(IProductPriceSource? prices, params (ProductKey Product, int Quantity)[] lines)
        => CategoryOrderFactory.Create(Request(ContractA, lines), PolicyConfiguration.Default, AllWeedUnlocked(), prices).Order!;

    [Fact]
    public void P01_sour_diesel_quote_never_accepts_cheaper_og_kush()
    {
        var order = Create(Tiered, (SourDiesel, 5));

        Assert.Equal(new[] { Gdp, SourDiesel }, order.AllowedProducts);
        var og = DeliveryValidator.Validate(order, new List<DeliveredItem?> { Item("0", OgKush, 5) }, AllWeedUnlocked());
        Assert.Equal(DeliveryRejection.ProductNotAllowed, og.Rejection);
    }

    [Fact]
    public void P02_equal_or_more_valuable_substitutes_are_accepted()
    {
        var order = Create(Tiered, (SourDiesel, 5));

        Assert.True(DeliveryValidator.Validate(order, new List<DeliveredItem?> { Item("0", Gdp, 5) }, AllWeedUnlocked()).IsValid);
        Assert.True(DeliveryValidator.Validate(order, new List<DeliveredItem?> { Item("0", SourDiesel, 5) }, AllWeedUnlocked()).IsValid);
    }

    [Fact]
    public void P03_cheapest_requested_product_allows_every_pricier_one()
    {
        Assert.Equal(new[] { Gdp, OgKush, SourDiesel }, Create(Tiered, (OgKush, 5)).AllowedProducts);
    }

    [Fact]
    public void P04_quote_is_quantity_times_price_and_the_boundary_is_inclusive()
    {
        var equal = new ProductPrices(new Dictionary<ProductKey, decimal> { [OgKush] = 40m, [SourDiesel] = 40m, [Gdp] = 39.99m });
        var order = Create(equal, (OgKush, 3));

        Assert.Equal(120m, order.QuotedValue);
        Assert.Equal(new[] { OgKush, SourDiesel }, order.AllowedProducts); // 40 == 40 allowed, 39.99 refused
        Assert.Equal(PricingBasis.Priced, order.PricingBasis);
    }

    [Fact]
    public void P05_multi_line_quote_uses_the_average_unit_price()
    {
        // 2 x OG (35) + 2 x GDP (50) = 170, 42.50 a unit: Sour Diesel (45) and GDP qualify, OG Kush does not.
        var order = Create(Tiered, (OgKush, 2), (Gdp, 2));

        Assert.Equal(170m, order.QuotedValue);
        Assert.Equal(new[] { Gdp, SourDiesel }, order.AllowedProducts);
    }

    [Fact]
    public void P06_missing_requested_price_disables_substitution()
    {
        var partial = new ProductPrices(new Dictionary<ProductKey, decimal> { [OgKush] = 35m, [Gdp] = 50m });
        var order = Create(partial, (SourDiesel, 5));

        Assert.Equal(PricingBasis.RequestedOnly, order.PricingBasis);
        Assert.Equal(new[] { SourDiesel }, order.AllowedProducts);
        Assert.Null(order.QuotedValue);
    }

    [Fact]
    public void P07_no_price_source_disables_substitution()
    {
        var order = Create(null, (OgKush, 5));

        Assert.Equal(PricingBasis.RequestedOnly, order.PricingBasis);
        Assert.Equal(new[] { OgKush }, order.AllowedProducts);
    }

    [Fact]
    public void P08_unpriced_substitutes_are_excluded()
    {
        var noGdp = new ProductPrices(new Dictionary<ProductKey, decimal> { [OgKush] = 35m, [SourDiesel] = 45m });
        Assert.Equal(new[] { OgKush, SourDiesel }, Create(noGdp, (OgKush, 5)).AllowedProducts);
    }

    [Fact]
    public void P09_no_fair_product_leaves_the_contract_unmanaged()
    {
        // GDP requested but only OG Kush and Sour Diesel are selected, both cheaper than the quote.
        var selected = PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, new[] { OgKush, SourDiesel }));
        var result = CategoryOrderFactory.Create(Request(ContractA, (Gdp, 5)), selected, AllWeedUnlocked(), Tiered);

        Assert.Equal(OrderOutcome.NoFairProduct, result.Outcome);
        Assert.Null(result.Order);
    }

    [Fact]
    public void P10_no_mixed_basket_is_worth_less_than_the_quote()
    {
        var order = Create(Tiered, (SourDiesel, 5));
        for (var sd = 0; sd <= 5; sd++)
        {
            var basket = new List<(ProductKey, long)> { (SourDiesel, sd), (Gdp, 5 - sd) };
            Assert.True(FairPriceRule.BasketValue(order, basket) >= order.QuotedValue);
            var items = new List<DeliveredItem?> { Item("a", SourDiesel, sd), Item("b", Gdp, 5 - sd) }.Where(i => i!.Quantity > 0).ToList();
            Assert.True(DeliveryValidator.Validate(order, items, AllWeedUnlocked()).IsValid);
        }
    }

    [Fact]
    public void P11_a_snapshot_whose_prices_do_not_prove_fairness_is_downgraded()
    {
        var prices = new Dictionary<ProductKey, decimal> { [OgKush] = 35m, [SourDiesel] = 45m };
        var tampered = new CategoryOrder(ContractA, Weed, 5, new[] { OgKush, SourDiesel }, true, 1, new[] { SourDiesel },
            PricingBasis.Priced, 225m, prices);

        Assert.Equal(PricingBasis.RequestedOnly, tampered.PricingBasis);
        Assert.Null(tampered.QuotedValue);
    }

    [Fact]
    public void P12_priced_and_requested_only_snapshots_round_trip()
    {
        var book = new ActiveOrderBook();
        book.TryAdd(Create(Tiered, (SourDiesel, 5)));
        book.TryAdd(CategoryOrderFactory.Create(Request(ContractB, (OgKush, 2)), PolicyConfiguration.Default, AllWeedUnlocked(), null).Order!);
        var issues = new List<string>();

        var loaded = ActiveOrderStore.FromJson(ActiveOrderStore.ToJson(book.Orders), issues).ToDictionary(o => o.Order.ContractId, o => o.Order);

        Assert.Empty(issues);
        Assert.Equal(PricingBasis.Priced, loaded[ContractA].PricingBasis);
        Assert.Equal(225m, loaded[ContractA].QuotedValue);
        Assert.Equal(new[] { Gdp, SourDiesel }, loaded[ContractA].AllowedProducts);
        Assert.Equal(PricingBasis.RequestedOnly, loaded[ContractB].PricingBasis);
        Assert.Equal(new[] { OgKush }, loaded[ContractB].AllowedProducts);
    }

    [Fact]
    public void P13_legacy_snapshots_are_limited_to_their_requested_products()
    {
        var json = "{\"schemaVersion\":1,\"orders\":[" +
                   "{\"contract\":\"old1\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\",\"sourdiesel\"],\"requestedProducts\":[\"sourdiesel\"]}," +
                   "{\"contract\":\"old2\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\"],\"requestedProducts\":[\"granddaddypurple\"]}]}";
        var issues = new List<string>();

        var loaded = ActiveOrderStore.FromJson(json, issues).ToDictionary(o => o.Order.ContractId, o => o.Order);

        Assert.Equal(new[] { SourDiesel }, loaded["old1"].AllowedProducts); // cheaper OG Kush no longer accepted
        Assert.Equal(PricingBasis.RequestedOnly, loaded["old1"].PricingBasis);
        Assert.Equal(new[] { OgKush }, loaded["old2"].AllowedProducts); // nothing requested was allowed: kept, flagged
        Assert.Equal(PricingBasis.Legacy, loaded["old2"].PricingBasis);
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void P14_damaged_price_data_never_substitutes()
    {
        var json = "{\"schemaVersion\":1,\"orders\":[" +
                   "{\"contract\":\"c1\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\",\"sourdiesel\"],\"requestedProducts\":[\"sourdiesel\"]," +
                   "\"pricing\":\"priced\",\"quotedValue\":225,\"unitPrices\":{\"sourdiesel\":45}}," +
                   "{\"contract\":\"c2\",\"category\":\"marijuana\",\"requiredUnits\":5,\"allowedProducts\":[\"ogkush\"],\"requestedProducts\":[\"ogkush\"],\"pricing\":\"bogus\"}]}";
        var issues = new List<string>();

        var loaded = ActiveOrderStore.FromJson(json, issues);

        var order = Assert.Single(loaded).Order;
        Assert.Equal("c1", order.ContractId);
        Assert.Equal(new[] { SourDiesel }, order.AllowedProducts);
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void P15_player_sale_pays_the_game_quote_once_and_refuses_the_cheap_swap()
    {
        var h = new SettlementHarness().Stock("og", OgKush, 10).Stock("gdp", Gdp, 10);
        h.Coordinator.Prices = Tiered;
        h.Accept(ContractA, 225f, (SourDiesel, 5));

        Assert.False(h.Submit(ContractA, ("og", 5))); // the exploit: refused before anything moves
        Assert.Equal(10, h.UnitsOf("og"));
        Assert.True(h.Submit(ContractA, ("gdp", 5)));
        Assert.Equal((225f, 1, 1), (h.Cash, h.PaymentCount, h.XpAwards)); // the game's own payment, unchanged, once
    }

    [Fact]
    public void P16_dealer_never_reserves_a_cheaper_product_for_an_expensive_quote()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 50);
        h.Coordinator.Prices = Tiered;
        h.Accept(ContractA, 225f, (SourDiesel, 5));

        Assert.Equal(AppointmentState.WaitingForEligibleStock, h.Coordinator.Find(ContractA)!.State);
        var result = h.Handover(ContractA);
        Assert.Equal(HandoverDecision.Withheld, result.Decision);
        Assert.Equal(50, h.UnitsOf(Benji, OgKush));

        h.Stock(Benji, Gdp, 5);
        var later = h.Handover(ContractA);
        Assert.True(later.Settled);
        Assert.Equal(new[] { (Gdp, 5) }, later.Items);
        Assert.Equal(1, h.Payments);
    }

    [Fact]
    public void P17_float_prices_from_the_game_are_cleaned()
    {
        var prices = ProductPrices.FromFloats(new[]
        {
            new KeyValuePair<ProductKey, float>(OgKush, 35.004f),
            new KeyValuePair<ProductKey, float>(SourDiesel, float.NaN),
            new KeyValuePair<ProductKey, float>(Gdp, -1f),
        });

        Assert.True(prices.TryGet(OgKush, out var og));
        Assert.Equal(35m, og);
        Assert.False(prices.TryGet(SourDiesel, out _));
        Assert.False(prices.TryGet(Gdp, out _));
    }
}
