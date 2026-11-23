using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite B: handover eligibility (the table in the Phase 03 brief) and mixed-stack edge cases.</summary>
public class MixedDeliveryTests
{
    private static DeliveryValidation Validate(CategoryOrder order, ProductCatalogSnapshot? catalog, params DeliveredItem?[] items)
        => DeliveryValidator.Validate(order, items, catalog);

    /// <summary>Order created through the real factory under <paramref name="policy"/> (null when it stays vanilla).</summary>
    private static CategoryOrder? Accept(PolicyConfiguration policy, bool mixed = true)
        => CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), policy.WithMixedDeliveries(mixed), AllWeedUnlocked()).Order;

    [Fact]
    public void B01_five_gdp_accepted()
        => Assert.True(Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", Gdp, 5)).IsValid);

    [Fact]
    public void B02_five_og_kush_accepted()
        => Assert.True(Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", OgKush, 5)).IsValid);

    [Fact]
    public void B03_three_gdp_plus_two_og_kush_accepted_with_mixed_on()
    {
        var v = Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", Gdp, 3), Item("1", OgKush, 2));

        Assert.True(v.IsValid);
        Assert.Equal(5, v.DeliveredUnits);
        Assert.Equal(new[] { ("0", 3), ("1", 2) }, v.Plan!.Allocations.Select(a => (a.StackId, a.Quantity)));
    }

    [Fact]
    public void B04_mixed_products_rejected_with_mixed_off()
        => Assert.Equal(DeliveryRejection.MixedNotAllowed, Validate(Accept(Policy(), mixed: false)!, AllWeedUnlocked(), Item("0", Gdp, 3), Item("1", OgKush, 2)).Rejection);

    [Fact]
    public void B04_several_stacks_of_one_product_are_fine_with_mixed_off()
        => Assert.True(Validate(Accept(Policy(), mixed: false)!, AllWeedUnlocked(), Item("0", Gdp, 3), Item("1", Gdp, 2)).IsValid);

    [Fact]
    public void B05_other_category_rejected()
        => Assert.Equal(DeliveryRejection.WrongCategory, Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", Coke1, 5)).Rejection);

    [Fact]
    public void B05_one_wrong_stack_rejects_the_whole_delivery()
        => Assert.Equal(DeliveryRejection.WrongCategory, Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", Gdp, 3), Item("1", Coke1, 2)).Rejection);

    [Fact]
    public void B06_unselected_product_rejected()
    {
        var order = Accept(Policy(p => p.WithCategory(Weed, CategoryPolicy.SelectedOnly(Gdp, OgKush))))!;

        var v = Validate(order, AllWeedUnlocked(), Item("0", Gdp, 3), Item("1", SourDiesel, 2));

        Assert.Equal(DeliveryRejection.ProductNotAllowed, v.Rejection);
        Assert.Equal("1", v.OffendingSlot);
    }

    [Fact]
    public void B07_too_few_rejected()
    {
        var v = Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", Gdp, 3));

        Assert.Equal(DeliveryRejection.TooFew, v.Rejection);
        Assert.Contains("you offered 3", v.Message);
    }

    [Fact]
    public void B08_too_many_rejected_so_no_extra_units_are_handed_over()
        => Assert.Equal(DeliveryRejection.TooMany, Validate(Accept(Policy())!, AllWeedUnlocked(), Item("0", Gdp, 6)).Rejection);

    [Fact]
    public void B09_locked_product_rejected()
    {
        // Standard(): GDP is locked in the live catalog.
        var order = Order(5, true, OgKush, SourDiesel, Gdp);

        Assert.Equal(DeliveryRejection.ProductLocked, Validate(order, Snapshot(Standard()), Item("0", Gdp, 5)).Rejection);
    }

    [Fact]
    public void B10_unknown_product_rejected()
        => Assert.Equal(DeliveryRejection.UnknownProduct, Validate(Order(5), AllWeedUnlocked(), Item("0", Unknown, 5)).Rejection);

    [Fact]
    public void B11_disabled_weed_creates_no_managed_order()
        => Assert.Null(Accept(Policy(p => p.WithCategory(Weed, CategoryPolicy.Disabled))));

    [Fact]
    public void B12_selected_only_empty_creates_no_managed_order()
        => Assert.Null(Accept(Policy(p => p.WithCategory(Weed, CategoryPolicy.SelectedOnly()))));

    [Fact]
    public void B13_global_disable_creates_no_managed_order()
        => Assert.Null(Accept(Policy(p => p.WithEnabled(false))));

    [Fact]
    public void Duplicate_slot_ids_rejected()
        => Assert.Equal(DeliveryRejection.InvalidItem, Validate(Order(5), AllWeedUnlocked(), Item("0", Gdp, 3), Item("0", Gdp, 2)).Rejection);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void Invalid_quantities_rejected(int quantity, int unitsPerItem)
        => Assert.Equal(DeliveryRejection.InvalidItem, Validate(Order(5), AllWeedUnlocked(), Item("0", Gdp, quantity, unitsPerItem)).Rejection);

    [Fact]
    public void Missing_references_rejected()
    {
        Assert.Equal(DeliveryRejection.InvalidItem, Validate(Order(5), AllWeedUnlocked(), (DeliveredItem?)null).Rejection);
        Assert.Equal(DeliveryRejection.InvalidItem, DeliveryValidator.Validate(Order(5), new[] { new DeliveredItem("0", null, 5, 1, true) }, AllWeedUnlocked()).Rejection);
        Assert.Equal(DeliveryRejection.InvalidItem, DeliveryValidator.Validate(Order(5), new[] { new DeliveredItem("", Gdp.Value, 5, 1, true) }, AllWeedUnlocked()).Rejection);
        Assert.Equal(DeliveryRejection.NothingDelivered, DeliveryValidator.Validate(Order(5), null, AllWeedUnlocked()).Rejection);
    }

    [Fact]
    public void Unpackaged_product_rejected()
        => Assert.Equal(DeliveryRejection.NotPackaged, Validate(Order(5), AllWeedUnlocked(), Item("0", Gdp, 5, 1, packaged: false)).Rejection);

    [Fact]
    public void Missing_catalog_rejects_instead_of_approving()
        => Assert.Equal(DeliveryRejection.CatalogNotReady, Validate(Order(5), null, Item("0", Gdp, 5)).Rejection);

    [Fact]
    public void Packaged_units_count_quantity_times_amount()
    {
        // 2 jars of 5 units = 10.
        var v = Validate(Order(10), AllWeedUnlocked(), Item("0", OgKush, 2, unitsPerItem: 5));

        Assert.True(v.IsValid);
        Assert.Equal(10, v.DeliveredUnits);
    }

    [Fact]
    public void Eligible_plus_ineligible_stacks_rejected_even_when_the_eligible_part_is_enough()
        => Assert.Equal(DeliveryRejection.WrongCategory, Validate(Order(5), AllWeedUnlocked(), Item("0", Gdp, 5), Item("1", Meth1, 1)).Rejection);

    [Fact]
    public void Many_stacks_and_a_large_catalog_validate_quickly()
    {
        var products = Enumerable.Range(0, 1000).Select(i => Product(ProductKey.Create($"weed{i:D4}"), Weed)).ToArray();
        var catalog = Snapshot(products);
        var order = new CategoryOrder(ContractA, Weed, 200, products.Select(p => p.Key), true, 0);
        var items = Enumerable.Range(0, 200).Select(i => (DeliveredItem?)Item(i.ToString(), products[i].Key, 1)).ToArray();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var run = 0; run < 100; run++)
        {
            Assert.True(DeliveryValidator.Validate(order, items, catalog).IsValid);
        }

        watch.Stop();
        // Generous bound for CI machines; the measured value is reported in docs/PHASE_03_TESTING.md.
        Assert.True(watch.ElapsedMilliseconds < 5000, $"100 validations of 200 stacks took {watch.ElapsedMilliseconds} ms");
    }
}
