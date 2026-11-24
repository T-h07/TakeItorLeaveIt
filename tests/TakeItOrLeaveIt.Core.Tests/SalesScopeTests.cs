using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 05 suite C: separate player / dealer switches, dealer overrides and mixing modes.</summary>
public class SalesScopeTests
{
    private static OrderRequest Player(params (ProductKey P, int Q)[] lines) => Sales.Request(Sales.ContractA, lines);

    private static OrderRequest Dealer(params (ProductKey P, int Q)[] lines) => Sales.DealerRequest(Sales.ContractA, lines);

    [Fact]
    public void P05_C01_player_switch_off_leaves_player_contracts_vanilla_and_dealers_managed()
    {
        var policy = PolicyConfiguration.Default.WithPlayerSales(false);
        var catalog = Snapshot();

        Assert.Equal(OrderOutcome.SalesPathDisabled, CategoryOrderFactory.Create(Player((OgKush, 2)), policy, catalog, Prices).Outcome);
        Assert.Equal(OrderOutcome.Managed, CategoryOrderFactory.CreateForDealer(Dealer((OgKush, 2)), policy, catalog, Prices, Dealers.Benji).Outcome);
        Assert.False(policy.IsPathEnabled(SalesScope.Player));
        Assert.True(policy.IsPathEnabled(SalesScope.Dealer(Dealers.Benji)));
    }

    [Fact]
    public void P05_C02_dealer_switch_off_leaves_dealer_contracts_vanilla_and_players_managed()
    {
        var policy = PolicyConfiguration.Default.WithDealerSales(false);
        var catalog = Snapshot();

        Assert.Equal(OrderOutcome.Managed, CategoryOrderFactory.Create(Player((OgKush, 2)), policy, catalog, Prices).Outcome);
        Assert.Equal(OrderOutcome.SalesPathDisabled, CategoryOrderFactory.CreateForDealer(Dealer((OgKush, 2)), policy, catalog, Prices, Dealers.Benji).Outcome);
    }

    [Fact]
    public void P05_C03_global_switch_overrides_both_paths()
    {
        var policy = PolicyConfiguration.Default.WithEnabled(false);

        Assert.False(policy.IsPathEnabled(SalesScope.Player));
        Assert.False(policy.IsPathEnabled(SalesScope.Dealer(Dealers.Benji)));
        Assert.Equal(OrderOutcome.ModDisabled, CategoryOrderFactory.Create(Player((OgKush, 2)), policy, Snapshot(), Prices).Outcome);
    }

    [Fact]
    public void P05_C04_offers_follow_the_path_switch()
    {
        var catalog = Snapshot();
        var restricted = With(Allow(SelectionMatch.ExactProduct, OgKush));

        Assert.Equal(OfferDecision.Excluded, OfferPolicy.Evaluate(restricted, catalog, SourDiesel.Value, SalesScope.Player));
        Assert.Equal(OfferDecision.Vanilla, OfferPolicy.Evaluate(restricted.WithPlayerSales(false), catalog, SourDiesel.Value, SalesScope.Player));
        Assert.Equal(OfferDecision.Excluded, OfferPolicy.Evaluate(restricted.WithPlayerSales(false), catalog, SourDiesel.Value, SalesScope.Dealer(Dealers.Benji)));
    }

    [Fact]
    public void P05_C05_dealer_override_replaces_the_global_rule_for_that_dealer_only()
    {
        var catalog = Snapshot();
        var policy = With(Allow(SelectionMatch.ExactProduct, OgKush))
            .WithDealerCategory(Dealers.Benji, Weed, CategoryPolicy.AnyUnlocked);

        Assert.Contains(SourDiesel, Eligible(policy.ForScope(SalesScope.Dealer(Dealers.Benji)), catalog));
        Assert.DoesNotContain(SourDiesel, Eligible(policy.ForScope(SalesScope.Dealer(Dealers.Molly)), catalog));
        Assert.DoesNotContain(SourDiesel, Eligible(policy.ForScope(SalesScope.Player), catalog));

        var benji = CategoryOrderFactory.CreateForDealer(Dealer((OgKush, 2)), policy, catalog, Prices, Dealers.Benji);
        var molly = CategoryOrderFactory.CreateForDealer(Dealer((OgKush, 2)), policy, catalog, Prices, Dealers.Molly);
        Assert.Contains(SourDiesel, benji.Order!.AllowedProducts);
        Assert.DoesNotContain(SourDiesel, molly.Order!.AllowedProducts);
    }

    [Fact]
    public void P05_C06_dealer_override_never_bypasses_unlock_or_the_dealer_switch()
    {
        var catalog = Catalog.Snapshot(Standard());
        var policy = PolicyConfiguration.Default.WithDealerCategory(Dealers.Benji, Weed, CategoryPolicy.AnyUnlocked);

        Assert.DoesNotContain(Gdp, Eligible(policy.ForScope(SalesScope.Dealer(Dealers.Benji)), catalog)); // locked
        Assert.Equal(OrderOutcome.SalesPathDisabled,
            CategoryOrderFactory.CreateForDealer(Dealer((OgKush, 2)), policy.WithDealerSales(false), catalog, Prices, Dealers.Benji).Outcome);
    }

    [Fact]
    public void P05_C07_removing_an_override_falls_back_to_global()
    {
        var policy = With(Allow(SelectionMatch.ExactProduct, OgKush))
            .WithDealerCategory(Dealers.Benji, Weed, CategoryPolicy.Disabled);

        Assert.Equal(PolicyMode.Disabled, policy.ForScope(SalesScope.Dealer(Dealers.Benji)).GetEffectivePolicy(Weed).Mode);
        var cleared = policy.WithoutDealerCategory(Dealers.Benji, Weed);
        Assert.Equal(PolicyMode.SelectedOnly, cleared.ForScope(SalesScope.Dealer(Dealers.Benji)).GetEffectivePolicy(Weed).Mode);
        Assert.Empty(cleared.DealerOverrides);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("has space")]
    public void P05_C08_invalid_dealer_ids_are_rejected(string? id)
    {
        Assert.False(PolicyConfiguration.IsValidDealerId(id));
    }

    [Fact]
    public void P05_C09_single_base_strain_mixing_groups_by_recorded_lineage()
    {
        var catalog = Snapshot();
        var policy = PolicyConfiguration.Default.WithMixing(MixingMode.SingleBaseStrain);
        var order = CategoryOrderFactory.Create(Player((OgKush, 4)), policy, catalog, Prices).Order!;

        Assert.True(order.CanCombine(new[] { OgKush, MixA, MixB }));
        Assert.False(order.CanCombine(new[] { OgKush, MixS }));
        Assert.True(order.CanCombine(new[] { Hybrid, MixS })); // share Sour Diesel
        Assert.True(order.CanCombine(new[] { Hybrid, MixA })); // share OG Kush
        Assert.False(order.CanCombine(new[] { Orphan, OgKush })); // unknown lineage: its own group

        var mixed = DeliveryValidator.Validate(order, new DeliveredItem?[] { Sales.Item("a", OgKush, 2), Sales.Item("b", MixS, 2) }, catalog);
        Assert.Equal(DeliveryRejection.MixedNotAllowed, mixed.Rejection);
        Assert.Contains("base strain", mixed.Message);
        Assert.True(DeliveryValidator.Validate(order, new DeliveredItem?[] { Sales.Item("a", OgKush, 2), Sales.Item("b", MixB, 2) }, catalog).IsValid);
    }

    [Fact]
    public void P05_C10_requested_only_takes_just_the_requested_product_when_permitted()
    {
        var catalog = Snapshot();
        var policy = PolicyConfiguration.Default.WithMixing(MixingMode.RequestedOnly);

        var order = CategoryOrderFactory.Create(Player((MixA, 3)), policy, catalog, Prices).Order!;
        Assert.Equal(new[] { MixA }, order.AllowedProducts);

        var blocked = CategoryOrderFactory.Create(Player((MixA, 3)), policy.WithCategory(Weed, Block(SelectionMatch.ExactProduct, MixA)), catalog, Prices);
        Assert.Equal(OrderOutcome.RequestedNotPermitted, blocked.Outcome);
        Assert.Null(blocked.Order);
    }

    [Fact]
    public void P05_C11_single_product_mode_matches_phase_03_mixed_off()
    {
        var policy = PolicyConfiguration.Default.WithMixedDeliveries(false);

        Assert.Equal(MixingMode.SingleProduct, policy.Mixing);
        Assert.False(policy.MixedDeliveries);
        var order = CategoryOrderFactory.Create(Player((OgKush, 4)), policy, Snapshot(), Prices).Order!;
        Assert.False(order.CanCombine(new[] { OgKush, MixA }));
    }

    [Fact]
    public void P05_C12_rule_explainer_gives_the_specific_reason()
    {
        var catalog = Snapshot();
        var order = new CategoryOrder(Sales.ContractA, Weed, 3, new[] { OgKush }, true, 1);

        Assert.Contains("blocked", RuleExplainer.ExplainNotAllowed(order, With(Block(SelectionMatch.BaseStrain, OgKush)), catalog, MixA));
        Assert.Contains("allowed list", RuleExplainer.ExplainNotAllowed(order, With(Allow(SelectionMatch.ExactProduct, OgKush)), catalog, MixA));
        Assert.Contains("effect", RuleExplainer.ExplainNotAllowed(order, With(Effects(EffectMatch.All, new[] { Paranoia })), catalog, MixA));
        Assert.Contains("base strain could not be confirmed", RuleExplainer.ExplainNotAllowed(order, With(Allow(SelectionMatch.BaseStrain, OgKush)), catalog, Orphan));
        Assert.Contains("when the order was accepted", RuleExplainer.ExplainNotAllowed(order, PolicyConfiguration.Default, catalog, MixA));
    }

    [Fact]
    public void P05_C13_existing_orders_keep_their_snapshot_when_switches_change()
    {
        var catalog = Snapshot();
        var policies = new InMemoryPolicyProvider(PolicyConfiguration.Default);
        var coordinator = new PlayerSaleCoordinator(policies, () => catalog, new RecordingLog()) { Prices = Prices };
        coordinator.SetAuthority(isServer: true, playerCount: 1);
        coordinator.BindSave("save-test", null);
        Assert.True(coordinator.OnContractAccepted(Player((OgKush, 2))).IsManaged);

        policies.Update(p => p.WithPlayerSales(false).WithCategory(Weed, CategoryPolicy.Disabled));

        Assert.True(coordinator.Validate(Sales.ContractA, new[] { Sales.Item("a", MixA, 2) })!.IsValid);
    }
}
