using System.Linq;
using TakeItOrLeaveIt.Core.Policies;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 05 suite A: base strain vs exact recipe selection, allowlists and blocklists, unknown ancestry.</summary>
public class ProductSelectionTests
{
    [Fact]
    public void P05_A01_exact_allowlist_takes_only_the_listed_recipe()
    {
        var policy = With(Allow(SelectionMatch.ExactProduct, OgKush));

        Assert.Equal(EligibilityReason.Allowed, Reason(policy, OgKush));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, MixA));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, MixB));
        Assert.Equal(new[] { OgKush }, Eligible(policy));
    }

    [Fact]
    public void P05_A02_base_strain_allowlist_includes_direct_and_transitive_descendants()
    {
        var policy = With(Allow(SelectionMatch.BaseStrain, OgKush));

        Assert.Equal(EligibilityReason.Allowed, Reason(policy, OgKush));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixA)); // direct: OG Kush + mixer
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixB)); // transitive: MixA + mixer
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, Hybrid)); // one of two recorded parents
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, SourDiesel));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, MixS));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, Blank));
    }

    [Fact]
    public void P05_A03_ancestry_never_comes_from_names()
    {
        // Orphan's key starts with "ogkush" and its name says "OG Kush", but no recipe links it to OG Kush.
        var allow = With(Allow(SelectionMatch.BaseStrain, OgKush));
        var block = With(Block(SelectionMatch.BaseStrain, OgKush));

        Assert.Equal(EligibilityReason.AncestryUnknown, Reason(allow, Orphan));
        Assert.Equal(EligibilityReason.AncestryUnknown, Reason(block, Orphan)); // blocklist fails closed
        Assert.DoesNotContain(Orphan, Eligible(allow));
        Assert.DoesNotContain(Orphan, Eligible(block));
    }

    [Fact]
    public void P05_A04_unknown_ancestry_still_matches_a_direct_selection()
    {
        var allow = With(Allow(SelectionMatch.BaseStrain, Orphan));

        Assert.Equal(EligibilityReason.Allowed, Reason(allow, Orphan));
    }

    [Fact]
    public void P05_A05_unknown_ancestry_is_irrelevant_to_exact_rules()
    {
        Assert.Equal(EligibilityReason.Allowed, Reason(With(Block(SelectionMatch.ExactProduct, OgKush)), Orphan));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(With(Allow(SelectionMatch.ExactProduct, OgKush)), Orphan));
    }

    [Fact]
    public void P05_A06_base_strain_blocklist_blocks_the_whole_lineage()
    {
        var policy = With(Block(SelectionMatch.BaseStrain, OgKush));

        Assert.Equal(EligibilityReason.ProductBlocked, Reason(policy, OgKush));
        Assert.Equal(EligibilityReason.ProductBlocked, Reason(policy, MixA));
        Assert.Equal(EligibilityReason.ProductBlocked, Reason(policy, MixB));
        Assert.Equal(EligibilityReason.ProductBlocked, Reason(policy, Hybrid));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, SourDiesel));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixS));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, Blank));
    }

    [Fact]
    public void P05_A07_exact_blocklist_blocks_only_the_listed_recipe()
    {
        var policy = With(Block(SelectionMatch.ExactProduct, OgKush));

        Assert.Equal(EligibilityReason.ProductBlocked, Reason(policy, OgKush));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixA));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixB));
    }

    [Fact]
    public void P05_A08_selecting_a_mix_by_lineage_takes_its_descendants_not_its_ancestors()
    {
        var policy = With(Allow(SelectionMatch.BaseStrain, MixA));

        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixA));
        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixB));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, OgKush));
    }

    [Fact]
    public void P05_A09_disabled_any_unlocked_and_empty_lists()
    {
        var snapshot = Snapshot();
        Assert.Empty(Eligible(With(CategoryPolicy.Disabled), snapshot));
        Assert.Equal(EligibilityReason.CategoryDisabled, Reason(With(CategoryPolicy.Disabled), OgKush, snapshot));
        Assert.Equal(snapshot.GetUnlockedProducts(Weed).Count, Eligible(With(CategoryPolicy.AnyUnlocked), snapshot).Length);
        Assert.Equal(EligibilityReason.NoProductsSelected, Reason(With(Allow(SelectionMatch.BaseStrain)), OgKush, snapshot));
        Assert.Equal(snapshot.GetUnlockedProducts(Weed).Count, Eligible(With(Block(SelectionMatch.BaseStrain)), snapshot).Length);
    }

    [Fact]
    public void P05_A10_lists_are_kept_separately_and_switching_mode_keeps_both()
    {
        var rule = Allow(SelectionMatch.ExactProduct, OgKush).WithBlocked(new[] { SourDiesel });

        var blocking = rule.WithMode(PolicyMode.Blocklist);
        var back = blocking.WithMode(PolicyMode.SelectedOnly);

        Assert.Equal(new[] { SourDiesel }, blocking.ActiveList);
        Assert.Equal(new[] { OgKush }, back.ActiveList);
        Assert.Equal(EligibilityReason.ProductBlocked, Reason(With(blocking), SourDiesel));
        Assert.Equal(EligibilityReason.Allowed, Reason(With(blocking), OgKush));
    }

    [Fact]
    public void P05_A11_locked_products_stay_ineligible_under_every_mode()
    {
        var snapshot = Catalog.Snapshot(Standard());
        foreach (var rule in new[] { CategoryPolicy.AnyUnlocked, Block(SelectionMatch.BaseStrain, OgKush), Allow(SelectionMatch.ExactProduct, Gdp) })
        {
            Assert.False(SaleEligibilityPolicy.Evaluate(With(rule), snapshot, Weed, Gdp).IsEligible);
        }
    }

    [Fact]
    public void P05_A12_catalog_reports_lineage_from_recorded_parents_only()
    {
        var snapshot = Snapshot();

        Assert.Equal(new[] { OgKush }, snapshot.GetBaseStrains(MixB));
        Assert.Equal(new[] { MixA, OgKush }.OrderBy(k => k), snapshot.GetAncestors(MixB));
        Assert.Equal(new[] { OgKush, SourDiesel }, snapshot.GetBaseStrains(Hybrid));
        Assert.Equal(new[] { Orphan }, snapshot.GetBaseStrains(Orphan));
        Assert.Empty(snapshot.GetAncestors(Orphan));
        Assert.False(snapshot.IsSameOrDescendantOf(Orphan, OgKush));
    }

    [Fact]
    public void P05_A13_recipe_cycles_do_not_hang_or_make_a_product_its_own_ancestor()
    {
        var x = Core.Products.ProductKey.Create("cycle_x");
        var y = Core.Products.ProductKey.Create("cycle_y");
        var snapshot = Catalog.Snapshot(Mix(x, new[] { y }), Mix(y, new[] { x }), Base(OgKush));

        Assert.DoesNotContain(x, snapshot.GetAncestors(x));
        Assert.Contains(y, snapshot.GetAncestors(x));
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(With(Allow(SelectionMatch.BaseStrain, OgKush)), x, snapshot));
    }
}
