using TakeItOrLeaveIt.Core.Matching;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group D: category restrictions and matching.</summary>
public class CategoryRestrictionTests
{
    private static readonly PolicyConfiguration Default = PolicyConfiguration.Default;

    [Fact]
    public void Weed_order_cannot_be_filled_with_meth()
    {
        var decision = SaleEligibilityPolicy.Evaluate(Default, Snapshot(Standard()), Weed, Meth1);

        Assert.Equal(EligibilityReason.WrongCategory, decision.Reason);
    }

    [Fact]
    public void Meth_order_cannot_be_filled_with_cocaine()
    {
        Assert.Equal(EligibilityReason.WrongCategory, SaleEligibilityPolicy.Evaluate(Default, Snapshot(Standard()), Meth, Coke1).Reason);
    }

    [Fact]
    public void Wrong_category_is_reported_before_mode_rules()
    {
        // Even a Disabled weed category must explain that meth is simply the wrong category.
        var policy = Default.WithCategory(Weed, CategoryPolicy.Disabled);

        Assert.Equal(EligibilityReason.WrongCategory, SaleEligibilityPolicy.Evaluate(policy, Snapshot(Standard()), Weed, Meth1).Reason);
    }

    [Fact]
    public void Selecting_a_product_of_another_category_does_not_make_it_eligible()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(Meth1));
        var snapshot = Snapshot(Standard());

        Assert.Equal(EligibilityReason.WrongCategory, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, Meth1).Reason);
        var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed);
        Assert.Empty(summary.EligibleProducts);
        Assert.Equal(new[] { Meth1 }, summary.InactiveSelections);
    }

    [Fact]
    public void Category_unknown_to_the_catalog_is_unsupported()
    {
        var heroin = CategoryKey.Create("heroin");

        Assert.Equal(EligibilityReason.UnsupportedCategory, SaleEligibilityPolicy.Evaluate(Default, Snapshot(Standard()), heroin, OgKush).Reason);
        Assert.Equal(EligibilityReason.UnsupportedCategory, SaleEligibilityPolicy.EvaluateCategory(Default, Snapshot(Standard()), heroin).Reason);
    }

    [Fact]
    public void Category_matching_uses_keys_not_display_names()
    {
        var p = Product(OgKush, Weed, name: "Meth looking name");

        Assert.True(ProductMatcher.MatchesCategory(p, CategoryKey.Create("MARIJUANA")));
        Assert.False(ProductMatcher.MatchesCategory(p, Meth));
        Assert.False(ProductMatcher.MatchesCategory(p, default));
    }

    [Fact]
    public void EvaluateAll_reports_each_category_independently()
    {
        var policy = Default
            .WithCategory(Weed, CategoryPolicy.SelectedOnly(SourDiesel))
            .WithCategory(Meth, CategoryPolicy.Disabled);

        var all = SaleEligibilityPolicy.EvaluateAll(policy, Snapshot(Standard()));

        Assert.Collection(all,
            c => { Assert.Equal(Cocaine, c.Category); Assert.Equal(EligibilityReason.Allowed, c.Reason); },
            c => { Assert.Equal(Weed, c.Category); Assert.Equal(new[] { SourDiesel }, c.EligibleProducts.Keys()); },
            c => { Assert.Equal(Meth, c.Category); Assert.Equal(EligibilityReason.CategoryDisabled, c.Reason); });
    }
}
