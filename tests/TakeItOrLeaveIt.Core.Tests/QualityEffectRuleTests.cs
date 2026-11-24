using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 05 suite B: quality minimums and effect rules (require all / any, exclude, conflicts).</summary>
public class QualityEffectRuleTests
{
    [Fact]
    public void P05_B01_require_all_effects()
    {
        var policy = With(Effects(EffectMatch.All, new[] { Calming, Energizing }));

        Assert.Equal(EligibilityReason.Allowed, Reason(policy, MixA));
        Assert.Equal(EligibilityReason.EffectMissing, Reason(policy, OgKush));
        Assert.Equal(EligibilityReason.EffectMissing, Reason(policy, SourDiesel));
        Assert.Equal(new[] { MixA }, Eligible(policy));
    }

    [Fact]
    public void P05_B02_require_any_effect()
    {
        var policy = With(Effects(EffectMatch.Any, new[] { Paranoia, Energizing }));

        Assert.Equal(new[] { MixB, MixS, SourDiesel, MixA }.OrderBy(k => k), Eligible(policy));
        Assert.Equal(EligibilityReason.EffectMissing, Reason(policy, OgKush));
    }

    [Fact]
    public void P05_B03_excluded_effects_win_over_required_ones()
    {
        var policy = With(Effects(EffectMatch.Any, new[] { Energizing }, Paranoia));

        Assert.Equal(EligibilityReason.Allowed, Reason(policy, SourDiesel));
        Assert.Equal(EligibilityReason.EffectExcluded, Reason(policy, MixS)); // energizing + paranoia
        Assert.Equal(EligibilityReason.EffectExcluded, Reason(policy, MixB));
    }

    [Fact]
    public void P05_B04_unreadable_effects_fail_only_when_an_effect_rule_exists()
    {
        Assert.Equal(EligibilityReason.Allowed, Reason(With(CategoryPolicy.AnyUnlocked), Blank));
        Assert.Equal(EligibilityReason.EffectsUnknown, Reason(With(Effects(EffectMatch.All, new string[0], Paranoia)), Blank));
        Assert.Equal(EligibilityReason.EffectsUnknown, Reason(With(Effects(EffectMatch.Any, new[] { Calming })), Blank));
    }

    [Fact]
    public void P05_B05_contradictory_effect_rule_is_detected_and_permits_nothing()
    {
        var rule = new EffectRule(EffectMatch.All, new[] { Calming, Energizing }, new[] { Calming });
        var policy = With(new CategoryPolicy(PolicyMode.AnyUnlocked, null, SelectionMatch.ExactProduct, null, rule, null));

        Assert.True(rule.IsContradictory);
        Assert.Equal(new[] { Calming }, rule.Conflicts);
        Assert.Empty(Eligible(policy));
        Assert.Equal(EligibilityReason.EffectRuleContradictory, SaleEligibilityPolicy.EvaluateCategory(policy, Snapshot(), Weed).Reason);
    }

    [Fact]
    public void P05_B06_require_any_with_one_conflict_still_works()
    {
        var rule = new EffectRule(EffectMatch.Any, new[] { Calming, Paranoia }, new[] { Calming });

        Assert.False(rule.IsContradictory);
        Assert.Equal(new[] { MixS }, Eligible(With(new CategoryPolicy(PolicyMode.AnyUnlocked, null, SelectionMatch.ExactProduct, null, rule, null)))
            .Where(k => k == MixS));
        Assert.Equal(EligibilityReason.EffectExcluded, Reason(With(new CategoryPolicy(PolicyMode.AnyUnlocked, null, SelectionMatch.ExactProduct, null, rule, null)), MixA));
    }

    [Fact]
    public void P05_B07_effect_cycle_goes_none_required_excluded_none()
    {
        var rule = EffectRule.None.Cycle(Calming);
        Assert.Equal(new[] { Calming }, rule.Required);
        rule = rule.Cycle(Calming);
        Assert.Empty(rule.Required);
        Assert.Equal(new[] { Calming }, rule.Excluded);
        rule = rule.Cycle(Calming);
        Assert.True(rule.IsEmpty);
    }

    [Fact]
    public void P05_B08_catalog_lists_category_effects()
    {
        Assert.Equal(new[] { Calming, Energizing, Paranoia }, Snapshot().GetEffects(Weed));
    }

    [Theory]
    [InlineData(null, null, EligibilityReason.Allowed)]
    [InlineData(QualityTier.Trash, null, EligibilityReason.Allowed)]
    [InlineData(null, QualityTier.Premium, EligibilityReason.QualityUnknown)]
    [InlineData(QualityTier.Standard, QualityTier.Premium, EligibilityReason.QualityTooLow)]
    [InlineData(QualityTier.Premium, QualityTier.Premium, EligibilityReason.Allowed)]
    [InlineData(QualityTier.Heavenly, QualityTier.Premium, EligibilityReason.Allowed)]
    [InlineData(QualityTier.Trash, QualityTier.Poor, EligibilityReason.QualityTooLow)]
    public void P05_B09_quality_minimum_is_a_floor(QualityTier? stack, QualityTier? minimum, EligibilityReason expected)
    {
        var policy = minimum is { } m ? With(MinQuality(m)) : PolicyConfiguration.Default;

        Assert.Equal(expected, SaleEligibilityPolicy.CheckQuality(policy, Weed, stack));
    }

    [Fact]
    public void P05_B10_trash_minimum_is_no_rule_and_unknown_game_values_are_unknown()
    {
        Assert.Null(MinQuality(QualityTier.Trash).MinimumQuality);
        Assert.Null(QualityTiers.FromGame(7));
        Assert.Null(QualityTiers.FromGame(-1));
        Assert.Equal(QualityTier.Heavenly, QualityTiers.FromGame(4));
    }

    [Fact]
    public void P05_B11_planner_skips_low_quality_stacks()
    {
        var policy = With(MinQuality(QualityTier.Premium));
        var stacks = new[]
        {
            new InventoryStack("a", OgKush, 5, QualityTier.Standard),
            new InventoryStack("b", OgKush, 3, QualityTier.Premium),
            new InventoryStack("c", OgKush, 4, null),
        };

        var plan = InventoryAllocationPlanner.Plan(Weed, 3, stacks, policy, Snapshot());

        Assert.True(plan.CanFulfill);
        Assert.Equal(new[] { "b" }, plan.Allocations.Select(a => a.StackId));
        Assert.Equal(QualityTier.Premium, plan.Allocations[0].Quality);
        Assert.Equal(3, plan.EligibleQuantityAvailable);
    }

    [Fact]
    public void P05_B12_handover_below_the_minimum_is_rejected_with_a_reason()
    {
        var order = new CategoryOrder(Sales.ContractA, Weed, 3, new[] { OgKush }, true, 1, minimumQuality: QualityTier.Premium);
        var catalog = Snapshot();

        var low = DeliveryValidator.Validate(order, new DeliveredItem?[] { new DeliveredItem("s", OgKush.Value, 3, 1, true, QualityTier.Standard) }, catalog);
        var unknown = DeliveryValidator.Validate(order, new DeliveredItem?[] { new DeliveredItem("s", OgKush.Value, 3, 1, true) }, catalog);
        var good = DeliveryValidator.Validate(order, new DeliveredItem?[] { new DeliveredItem("s", OgKush.Value, 3, 1, true, QualityTier.Heavenly) }, catalog);

        Assert.Equal(DeliveryRejection.QualityTooLow, low.Rejection);
        Assert.Contains("Premium or better", low.Message);
        Assert.Equal(DeliveryRejection.QualityUnknown, unknown.Rejection);
        Assert.True(good.IsValid);
    }

    [Fact]
    public void P05_B13_without_a_minimum_quality_is_not_checked()
    {
        var order = new CategoryOrder(Sales.ContractA, Weed, 3, new[] { OgKush }, true, 1);

        var result = DeliveryValidator.Validate(order, new DeliveredItem?[] { new DeliveredItem("s", OgKush.Value, 3, 1, true, QualityTier.Trash) }, Snapshot());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void P05_B14_dealer_minimum_is_the_stricter_of_rule_and_customer()
    {
        var catalog = Snapshot();
        OrderRequest Ask(QualityTier? q) => new(Sales.ContractA, false, new[] { new OrderLine(OgKush.Value, 3) }, q);

        var stricterCustomer = CategoryOrderFactory.CreateForDealer(Ask(QualityTier.Premium), With(MinQuality(QualityTier.Standard)), catalog, null, Dealers.Benji);
        var stricterRule = CategoryOrderFactory.CreateForDealer(Ask(QualityTier.Poor), With(MinQuality(QualityTier.Premium)), catalog, null, Dealers.Benji);
        var vanilla = CategoryOrderFactory.CreateForDealer(Ask(QualityTier.Poor), PolicyConfiguration.Default, catalog, null, Dealers.Benji);

        Assert.Equal(QualityTier.Premium, stricterCustomer.Order!.MinimumQuality);
        Assert.Equal(QualityTier.Premium, stricterRule.Order!.MinimumQuality);
        Assert.Equal(QualityTier.Poor, vanilla.Order!.MinimumQuality); // the customer's own standard, unchanged
    }

    [Fact]
    public void P05_B15_player_minimum_is_the_rule_only()
    {
        var catalog = Snapshot();
        var request = new OrderRequest(Sales.ContractA, true, new[] { new OrderLine(OgKush.Value, 3) }, QualityTier.Heavenly);

        Assert.Null(CategoryOrderFactory.Create(request, PolicyConfiguration.Default, catalog).Order!.MinimumQuality);
        Assert.Equal(QualityTier.Poor, CategoryOrderFactory.Create(request, With(MinQuality(QualityTier.Poor)), catalog).Order!.MinimumQuality);
    }

    [Fact]
    public void P05_B16_quality_minimum_survives_the_active_order_store()
    {
        var fs = new MemoryFileSystem();
        var store = new ActiveOrderStore("root", fs);
        var order = new CategoryOrder(Sales.ContractA, Weed, 3, new[] { OgKush }, false, 4, mixing: MixingMode.SingleBaseStrain,
            baseStrainGroups: new[] { new KeyValuePair<ProductKey, IReadOnlyList<string>>(OgKush, new[] { OgKush.Value }) },
            minimumQuality: QualityTier.Premium);

        var book = new ActiveOrderBook();
        book.TryAdd(order);
        store.Save("save-test", book.Orders);
        var loaded = store.Load("save-test").Orders.Single().Order;

        Assert.Equal(QualityTier.Premium, loaded.MinimumQuality);
        Assert.Equal(MixingMode.SingleBaseStrain, loaded.Mixing);
        Assert.Equal(new[] { OgKush.Value }, loaded.GroupsOf(OgKush));
    }
}
