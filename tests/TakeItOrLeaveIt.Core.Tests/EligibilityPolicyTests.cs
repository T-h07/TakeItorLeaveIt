using System;
using System.Linq;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group C: policy modes.</summary>
public class EligibilityPolicyTests
{
    private static readonly PolicyConfiguration Default = PolicyConfiguration.Default;

    [Fact]
    public void AnyUnlocked_allows_every_unlocked_product_of_the_category()
    {
        var snapshot = Snapshot(Standard());

        Assert.True(SaleEligibilityPolicy.Evaluate(Default, snapshot, Weed, OgKush).IsEligible);
        Assert.True(SaleEligibilityPolicy.Evaluate(Default, snapshot, Weed, SourDiesel).IsEligible);
        var summary = SaleEligibilityPolicy.EvaluateCategory(Default, snapshot, Weed);
        Assert.Equal(EligibilityReason.Allowed, summary.Reason);
        Assert.Equal(new[] { OgKush, SourDiesel }, summary.EligibleProducts.Keys());
        Assert.Equal(2, summary.UnlockedCount);
    }

    [Fact]
    public void AnyUnlocked_rejects_locked_products()
    {
        var decision = SaleEligibilityPolicy.Evaluate(Default, Snapshot(Standard()), Weed, Gdp);

        Assert.False(decision.IsEligible);
        Assert.Equal(EligibilityReason.ProductLocked, decision.Reason);
    }

    [Fact]
    public void SelectedOnly_allows_only_selected_unlocked_products()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(OgKush));
        var snapshot = Snapshot(Standard());

        Assert.True(SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, OgKush).IsEligible);
        Assert.Equal(EligibilityReason.ProductNotSelected, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, SourDiesel).Reason);
        Assert.Equal(new[] { OgKush }, SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed).EligibleProducts.Keys());
    }

    [Fact]
    public void SelectedOnly_never_allows_a_locked_selection()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(Gdp));
        var snapshot = Snapshot(Standard());

        Assert.Equal(EligibilityReason.ProductLocked, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, Gdp).Reason);
        var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed);
        Assert.Equal(EligibilityReason.NoEligibleProducts, summary.Reason);
        Assert.Equal(new[] { Gdp }, summary.InactiveSelections);
    }

    [Fact]
    public void SelectedOnly_with_empty_selection_means_no_products_selected_not_all()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly());
        var snapshot = Snapshot(Standard());

        Assert.Equal(EligibilityReason.NoProductsSelected, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, OgKush).Reason);
        var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed);
        Assert.Equal(EligibilityReason.NoProductsSelected, summary.Reason);
        Assert.False(summary.HasEligibleProducts);
        Assert.Empty(summary.EligibleProducts);
    }

    [Fact]
    public void Disabled_category_rejects_every_product()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.Disabled);
        var snapshot = Snapshot(Standard());

        Assert.Equal(EligibilityReason.CategoryDisabled, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, OgKush).Reason);
        var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed);
        Assert.Equal(EligibilityReason.CategoryDisabled, summary.Reason);
        Assert.Empty(summary.EligibleProducts);
        Assert.Equal(2, summary.UnlockedCount);
    }

    [Fact]
    public void Disabling_one_category_does_not_affect_others()
    {
        var policy = Default.WithCategory(Weed, CategoryPolicy.Disabled);
        var snapshot = Snapshot(Standard());

        Assert.True(SaleEligibilityPolicy.Evaluate(policy, snapshot, Meth, Meth1).IsEligible);
        Assert.True(SaleEligibilityPolicy.Evaluate(policy, snapshot, Cocaine, Coke1).IsEligible);
    }

    [Fact]
    public void Switching_modes_keeps_selection_without_activating_it()
    {
        var selected = CategoryPolicy.SelectedOnly(OgKush);
        var any = selected.WithMode(PolicyMode.AnyUnlocked);
        var back = any.WithMode(PolicyMode.SelectedOnly);

        Assert.Equal(new[] { OgKush }, any.SelectedProducts);
        Assert.True(SaleEligibilityPolicy.Evaluate(Default.WithCategory(Weed, any), Snapshot(Standard()), Weed, SourDiesel).IsEligible);
        Assert.Equal(new[] { OgKush }, back.SelectedProducts);
    }

    [Fact]
    public void Global_disable_wins_over_every_rule()
    {
        var policy = Default.WithEnabled(false);
        var snapshot = Snapshot(Standard());

        Assert.Equal(EligibilityReason.ModDisabled, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, OgKush).Reason);
        Assert.Equal(EligibilityReason.ModDisabled, SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed).Reason);
    }

    [Fact]
    public void Missing_catalog_fails_closed()
    {
        Assert.Equal(EligibilityReason.CatalogNotReady, SaleEligibilityPolicy.Evaluate(Default, null, Weed, OgKush).Reason);
        Assert.Empty(SaleEligibilityPolicy.EvaluateAll(Default, null));
    }

    [Fact]
    public void Unknown_product_fails_closed()
    {
        var decision = SaleEligibilityPolicy.Evaluate(Default, Snapshot(Standard()), Weed, ProductKey.Create("not-in-catalog"));

        Assert.Equal(EligibilityReason.ProductUnknown, decision.Reason);
    }

    [Fact]
    public void Invalid_request_keys_fail_closed()
    {
        var snapshot = Snapshot(Standard());

        Assert.Equal(EligibilityReason.InvalidRequest, SaleEligibilityPolicy.Evaluate(Default, snapshot, default, OgKush).Reason);
        Assert.Equal(EligibilityReason.InvalidRequest, SaleEligibilityPolicy.Evaluate(Default, snapshot, Weed, default).Reason);
    }

    [Fact]
    public void Decisions_carry_generation_revision_and_explanation()
    {
        var snapshot = Snapshot(Standard());
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(OgKush));

        var decision = SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, SourDiesel);

        Assert.Equal(snapshot.Generation, decision.CatalogGeneration);
        Assert.Equal(policy.Revision, decision.PolicyRevision);
        Assert.False(string.IsNullOrWhiteSpace(decision.Explanation));
    }

    [Fact]
    public void Every_reason_has_an_explanation()
    {
        foreach (EligibilityReason reason in Enum.GetValues(typeof(EligibilityReason)))
        {
            Assert.False(string.IsNullOrWhiteSpace(EligibilityText.Explain(reason)), reason.ToString());
        }
    }

    [Fact]
    public void Evaluation_is_deterministic()
    {
        var snapshot = Snapshot(Standard());
        var policy = Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(SourDiesel, OgKush));

        var first = SaleEligibilityPolicy.EvaluateAll(policy, snapshot).Select(s => (s.Category, s.Reason, string.Join(",", s.EligibleProducts.Keys())));
        var second = SaleEligibilityPolicy.EvaluateAll(policy, snapshot).Select(s => (s.Category, s.Reason, string.Join(",", s.EligibleProducts.Keys())));

        Assert.Equal(first, second);
    }
}
