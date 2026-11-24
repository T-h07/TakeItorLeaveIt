using System.Linq;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite E: satisfaction, vanilla regression, the mixed-deliveries setting and status reporting.</summary>
public class SalesRegressionTests
{
    [Fact]
    public void E01_allowed_substitution_scores_like_the_requested_recipe()
    {
        // The customer asked for GDP; OG Kush is allowed. Same quality rating → same match, no recipe penalty.
        var order = CategoryOrderFactory.Create(Request(ContractA, (Gdp, 5)), Policy(), AllWeedUnlocked(), Sales.Flat).Order!;
        var asked = DeliveryValidator.Validate(order, new[] { Item("0", Gdp, 5) }, AllWeedUnlocked());
        var substituted = DeliveryValidator.Validate(order, new[] { Item("0", OgKush, 5) }, AllWeedUnlocked());

        Assert.Equal(DeliveryMatch.Compute(asked, new[] { new RatedUnits(5, 0.9f) }).Match,
            DeliveryMatch.Compute(substituted, new[] { new RatedUnits(5, 0.9f) }).Match);
    }

    [Fact]
    public void E02_quality_still_lowers_the_match()
    {
        var valid = DeliveryValidator.Validate(Order(5), new[] { Item("0", OgKush, 5) }, AllWeedUnlocked());

        Assert.True(DeliveryMatch.Compute(valid, new[] { new RatedUnits(5, 0.4f) }).Match < 1f);
    }

    [Fact]
    public void E03_vanilla_orders_still_need_the_exact_product()
    {
        var h = new SettlementHarness(Policy(p => p.WithCategory(Weed, CategoryPolicy.Disabled))).Stock("og", OgKush, 5).Stock("gdp", Gdp, 5);
        Assert.Equal(OrderOutcome.CategoryNotEligible, h.Accept(ContractA, 80f, (Gdp, 5)).Outcome);

        Assert.False(h.Submit(ContractA, ("og", 5)));
        Assert.True(h.Submit(ContractA, ("gdp", 5)));
        Assert.Equal(80f, h.Cash);
    }

    [Fact]
    public void E04_dealer_contracts_are_untouched()
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Sales.Flat };
        coordinator.SetAuthority(true, 1);
        coordinator.BindSave("save-a", null);

        Assert.Equal(OrderOutcome.NotPlayerContract, coordinator.OnContractAccepted(DealerRequest(ContractA, (OgKush, 5))).Outcome);
        Assert.Null(coordinator.Validate(ContractA, new[] { Item("0", Gdp, 5) }));
        Assert.Equal(CommitObservation.NotManaged, coordinator.OnHandoverProcessed(ContractA));
    }

    [Fact]
    public void E05_validation_never_mutates_inputs()
    {
        var items = new[] { Item("0", Gdp, 3), Item("1", OgKush, 2) };

        DeliveryValidator.Validate(Order(5), items, AllWeedUnlocked());

        Assert.Equal(new[] { (3, Gdp.Value), (2, OgKush.Value) }, items.Select(i => (i.Quantity, i.ProductId!)));
    }

    [Fact]
    public void E06_payment_is_the_agreed_payment_whatever_product_is_delivered()
    {
        var h = new SettlementHarness().Stock("og", OgKush, 5);
        h.Accept(ContractA, 120f, (Gdp, 5));

        Assert.True(h.Submit(ContractA, ("og", 5)));
        Assert.Equal(120f, h.Cash);
        Assert.Equal(1, h.PaymentCount);
    }

    [Fact]
    public void Mixed_deliveries_setting_defaults_on_and_round_trips()
    {
        Assert.True(PolicyConfiguration.Default.MixedDeliveries);

        var off = PolicyConfiguration.Default.WithMixedDeliveries(false);
        var json = PolicySettingsJson.ToJson(off.ToSettings());
        var back = PolicyConfiguration.FromSettings(PolicySettingsJson.FromJson(json), out var issues);

        Assert.Contains("\"mixedDeliveries\": false", json);
        Assert.False(back.MixedDeliveries);
        Assert.Empty(issues);
        Assert.True(off.Revision > PolicyConfiguration.Default.Revision);
    }

    [Fact]
    public void Phase02_files_without_the_setting_load_with_mixed_on()
    {
        var settings = PolicySettingsJson.FromJson("{\"schemaVersion\":1,\"enabled\":true,\"defaultMode\":\"anyUnlocked\",\"categories\":{}}");

        Assert.True(PolicyConfiguration.FromSettings(settings, out _).MixedDeliveries);
    }

    [Fact]
    public void Mixed_setting_of_the_wrong_type_is_rejected()
        => Assert.Throws<System.IO.InvalidDataException>(() => PolicySettingsJson.FromJson("{\"schemaVersion\":1,\"mixedDeliveries\":\"yes\"}"));

    [Fact]
    public void Other_policy_changes_keep_the_mixed_setting()
    {
        var off = PolicyConfiguration.Default.WithMixedDeliveries(false)
            .WithEnabled(false).WithDefaultMode(PolicyMode.SelectedOnly).WithCategory(Weed, CategoryPolicy.Disabled).WithoutCategory(Weed);

        Assert.False(off.MixedDeliveries);
    }

    [Fact]
    public void Menu_toggle_changes_the_setting_the_sales_engine_reads()
    {
        var harness = new MenuHarness();
        harness.Load("save-a");

        Assert.True(harness.Menu.MixedDeliveries);
        Assert.True(harness.Menu.SetMixedDeliveries(false));
        Assert.True(harness.Menu.HasUnsavedChanges);
        Assert.True(harness.Menu.Save());

        Assert.False(harness.Provider.Current.MixedDeliveries);
        var order = CategoryOrderFactory.Create(Request(ContractA, (OgKush, 5)), harness.Provider.Current, harness.Catalog.State.Snapshot, Sales.Flat).Order!;
        Assert.False(order.MixedDeliveries);
    }

    [Fact]
    public void Status_reports_enabled_only_when_every_part_is_active()
    {
        var status = new PlayerSalesStatus
        {
            Runtime = "IL2CPP",
            OfferHook = ComponentStatus.Active,
            HandoverValidation = ComponentStatus.Active,
            TransactionIntegration = ComponentStatus.Active,
            CatalogReady = true,
            PolicyLoaded = true,
            ModEnabledForSave = true,
            Authoritative = true,
        };
        Assert.Equal("Player Sales: Enabled", status.Lines()[0]);

        status.TransactionIntegration = ComponentStatus.Unavailable;
        status.HooksUnavailableReason = "built without game references";
        Assert.Equal("Player Sales: Unavailable", status.Lines()[0]);
        Assert.Contains("Transaction integration: Unavailable", status.Lines());

        status.TransactionIntegration = ComponentStatus.Active;
        status.Authoritative = false;
        Assert.Equal("Player Sales: Inactive (unsupported session)", status.Lines()[0]);
    }
}
