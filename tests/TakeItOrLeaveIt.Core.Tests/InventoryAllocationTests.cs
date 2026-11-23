using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group E: non-mutating inventory allocation.</summary>
public class InventoryAllocationTests
{
    private static readonly PolicyConfiguration Default = PolicyConfiguration.Default;

    private static InventoryStack Stack(string id, ProductKey product, int quantity) => new(id, product, quantity);

    private static AllocationPlan Plan(int quantity, PolicyConfiguration? policy = null, params InventoryStack?[] stacks)
        => InventoryAllocationPlanner.Plan(Weed, quantity, stacks, policy ?? Default, Snapshot(Standard()));

    [Fact]
    public void Single_stack_fulfils_the_order()
    {
        var plan = Plan(3, null, Stack("s1", OgKush, 5));

        Assert.True(plan.CanFulfill);
        var a = Assert.Single(plan.Allocations);
        Assert.Equal(("s1", OgKush, 3), (a.StackId, a.Product, a.Quantity));
        Assert.Equal(5, plan.EligibleQuantityAvailable);
    }

    [Fact]
    public void Mixed_eligible_products_are_combined()
    {
        var plan = Plan(6, null, Stack("s1", OgKush, 4), Stack("s2", SourDiesel, 4));

        Assert.True(plan.CanFulfill);
        Assert.Equal(new[] { ("s1", 4), ("s2", 2) }, plan.Allocations.Select(a => (a.StackId, a.Quantity)));
        Assert.Equal(6, plan.AllocatedQuantity);
    }

    [Fact]
    public void SelectedOnly_skips_unselected_stacks()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(SourDiesel));
        var plan = Plan(3, policy, Stack("s1", OgKush, 10), Stack("s2", SourDiesel, 3));

        Assert.True(plan.CanFulfill);
        Assert.Equal(new[] { "s2" }, plan.Allocations.Select(a => a.StackId));
        var r = Assert.Single(plan.Rejections);
        Assert.Equal(("s1", StackRejectionReason.Ineligible, (EligibilityReason?)EligibilityReason.ProductNotSelected), (r.StackId, r.Reason, r.Eligibility));
    }

    [Fact]
    public void Wrong_category_and_locked_stacks_are_never_used()
    {
        var plan = Plan(2, null, Stack("a", Meth1, 50), Stack("b", Gdp, 50), Stack("c", OgKush, 1));

        Assert.False(plan.CanFulfill);
        Assert.Equal(AllocationFailure.InsufficientEligibleQuantity, plan.Failure);
        Assert.Equal(1, plan.EligibleQuantityAvailable);
        Assert.Empty(plan.Allocations);
        Assert.Equal(
            new[] { ("a", (EligibilityReason?)EligibilityReason.WrongCategory), ("b", (EligibilityReason?)EligibilityReason.ProductLocked) },
            plan.Rejections.Select(r => (r.StackId!, r.Eligibility)));
    }

    [Fact]
    public void No_eligible_stock_is_reported_distinctly()
    {
        var plan = Plan(1, null, Stack("a", Meth1, 5));

        Assert.Equal(AllocationFailure.NoEligibleStock, plan.Failure);
        Assert.Equal(0, plan.EligibleQuantityAvailable);
    }

    [Fact]
    public void Empty_or_null_inventory_cannot_fulfil()
    {
        Assert.Equal(AllocationFailure.NoEligibleStock, Plan(1, null).Failure);
        Assert.Equal(AllocationFailure.NoEligibleStock, InventoryAllocationPlanner.Plan(Weed, 1, null, Default, Snapshot(Standard())).Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_request_is_invalid(int quantity)
    {
        var plan = Plan(quantity, null, Stack("s1", OgKush, 5));

        Assert.Equal(AllocationFailure.InvalidQuantity, plan.Failure);
        Assert.Empty(plan.Allocations);
    }

    [Fact]
    public void Invalid_stacks_are_rejected_with_reasons()
    {
        var plan = Plan(1, null, null, Stack(" ", OgKush, 3), Stack("x", default, 3), Stack("z", OgKush, 0), Stack("y", OgKush, -2));

        Assert.False(plan.CanFulfill);
        Assert.Equal(5, plan.Rejections.Count);
        Assert.Equal(3, plan.Rejections.Count(r => r.Reason == StackRejectionReason.InvalidStack));
        Assert.Equal(2, plan.Rejections.Count(r => r.Reason == StackRejectionReason.InvalidQuantity));
    }

    [Fact]
    public void Duplicate_stack_ids_are_never_double_counted()
    {
        var plan = Plan(4, null, Stack("dup", OgKush, 3), Stack("dup", OgKush, 3), Stack("ok", SourDiesel, 2));

        Assert.False(plan.CanFulfill);
        Assert.Equal(2, plan.EligibleQuantityAvailable);
        Assert.Equal(2, plan.Rejections.Count(r => r.Reason == StackRejectionReason.DuplicateStackId));
    }

    [Fact]
    public void Disabled_category_fails_at_category_level()
    {
        var plan = Plan(1, Default.WithCategory(Weed, CategoryPolicy.Disabled), Stack("s1", OgKush, 5));

        Assert.Equal(AllocationFailure.CategoryNotEligible, plan.Failure);
        Assert.Equal(EligibilityReason.CategoryDisabled, plan.CategoryReason);
    }

    [Fact]
    public void Empty_selection_fails_at_category_level()
    {
        var plan = Plan(1, Default.WithCategory(Weed, CategoryPolicy.SelectedOnly()), Stack("s1", OgKush, 5));

        Assert.Equal(EligibilityReason.NoProductsSelected, plan.CategoryReason);
    }

    [Fact]
    public void Missing_catalog_fails_closed()
    {
        var plan = InventoryAllocationPlanner.Plan(Weed, 1, new[] { Stack("s1", OgKush, 5) }, Default, null);

        Assert.Equal(AllocationFailure.CategoryNotEligible, plan.Failure);
        Assert.Equal(EligibilityReason.CatalogNotReady, plan.CategoryReason);
    }

    [Fact]
    public void Allocated_total_never_exceeds_request_or_stack_quantity()
    {
        var stacks = new[] { Stack("a", OgKush, 7), Stack("b", SourDiesel, 9), Stack("c", OgKush, 2) };
        for (var requested = 1; requested <= 18; requested++)
        {
            var plan = Plan(requested, null, stacks);
            if (!plan.CanFulfill)
            {
                Assert.True(requested > 18);
                continue;
            }

            Assert.Equal(requested, plan.AllocatedQuantity);
            Assert.All(plan.Allocations, a => Assert.True(a.Quantity > 0 && a.Quantity <= stacks.Single(s => s.StackId == a.StackId).Quantity));
        }

        Assert.Equal(AllocationFailure.InsufficientEligibleQuantity, Plan(19, null, stacks).Failure);
    }

    [Fact]
    public void Planning_does_not_mutate_inputs()
    {
        var stacks = new List<InventoryStack?> { Stack("b", OgKush, 5), Stack("a", SourDiesel, 5) };
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(OgKush, SourDiesel));
        var snapshot = Snapshot(Standard());
        var generation = snapshot.Generation;
        var revision = policy.Revision;

        InventoryAllocationPlanner.Plan(Weed, 7, stacks, policy, snapshot);

        Assert.Equal(new[] { "b", "a" }, stacks.Select(s => s!.StackId));
        Assert.Equal(new[] { 5, 5 }, stacks.Select(s => s!.Quantity));
        Assert.Equal(generation, snapshot.Generation);
        Assert.Equal(revision, policy.Revision);
        Assert.Equal(new[] { OgKush, SourDiesel }, policy.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void Result_is_independent_of_input_order()
    {
        var stacks = new[] { Stack("c", OgKush, 3), Stack("a", SourDiesel, 2), Stack("b", OgKush, 4), Stack("d", Meth1, 9) };

        string Describe(AllocationPlan p) => string.Join(";", p.Allocations.Select(a => a.ToString())) + "|" + string.Join(";", p.Rejections.Select(r => r.ToString()));

        var expected = Describe(Plan(5, null, stacks));
        Assert.Equal("2 x sourdiesel from a;3 x ogkush from b|d: Ineligible (WrongCategory)", expected);
        Assert.Equal(expected, Describe(Plan(5, null, stacks.Reverse().ToArray())));
        Assert.Equal(expected, Describe(Plan(5, null, stacks[2], stacks[0], stacks[3], stacks[1])));
    }

    [Fact]
    public void Plan_records_generation_and_revision_for_revalidation()
    {
        var snapshot = Snapshot(Standard());
        var policy = Default.WithEnabled(true);

        var plan = InventoryAllocationPlanner.Plan(Weed, 1, new[] { Stack("s", OgKush, 1) }, policy, snapshot);

        Assert.Equal(snapshot.Generation, plan.CatalogGeneration);
        Assert.Equal(policy.Revision, plan.PolicyRevision);
    }

    [Fact]
    public void Large_stacks_do_not_overflow_the_total()
    {
        var plan = Plan(int.MaxValue, null, Stack("a", OgKush, int.MaxValue), Stack("b", SourDiesel, int.MaxValue));

        Assert.True(plan.CanFulfill);
        Assert.Equal(2L * int.MaxValue, plan.EligibleQuantityAvailable);
        Assert.Equal(int.MaxValue, plan.AllocatedQuantity);
    }
}
