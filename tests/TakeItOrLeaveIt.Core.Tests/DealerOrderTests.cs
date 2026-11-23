using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite A: which dealer contracts become managed appointments, and what their snapshot holds.</summary>
public class DealerOrderTests
{
    [Fact]
    public void P04_T01_dealer_contract_becomes_category_appointment()
    {
        var h = new DealerHarness().Stock(Benji, SourDiesel, 10);

        var created = h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.True(created.IsManaged);
        var a = created.Appointment!;
        Assert.Equal((Weed, 5, Benji), (a.Order.Category, a.Order.RequiredUnits, a.DealerId));
        Assert.Equal(new[] { Gdp, SourDiesel, OgKush }.OrderBy(p => p), a.Order.AllowedProducts);
        Assert.Equal(new[] { OgKush }, a.Order.RequestedProducts);
        Assert.Equal(AppointmentState.Reserved, a.State);
    }

    [Fact]
    public void P04_T02_player_and_dealer_factories_never_cross()
    {
        var catalog = AllWeedUnlocked();

        Assert.Equal(OrderOutcome.NotPlayerContract, CategoryOrderFactory.Create(DealerRequest(ContractA, (OgKush, 5)), PolicyConfiguration.Default, catalog).Outcome);
        Assert.Equal(OrderOutcome.InvalidContract, CategoryOrderFactory.CreateForDealer(Request(ContractA, (OgKush, 5)), PolicyConfiguration.Default, catalog).Outcome);
        Assert.True(CategoryOrderFactory.CreateForDealer(DealerRequest(ContractA, (OgKush, 5)), PolicyConfiguration.Default, catalog).IsManaged);
    }

    [Fact]
    public void P04_T03_mod_disabled_keeps_dealer_vanilla()
    {
        var h = new DealerHarness(PolicyConfiguration.Default.WithEnabled(false)).Stock(Benji, OgKush, 10);

        var created = h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.Equal(OrderOutcome.ModDisabled, created.Outcome);
        Assert.Equal(0, h.Coordinator.ActiveAppointments);
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
    }

    [Fact]
    public void P04_T04_unloaded_settings_keep_dealer_vanilla()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Coordinator.PolicyReady = () => false;

        Assert.Equal(OrderOutcome.PolicyNotLoaded, h.Accept(ContractA, 100f, (OgKush, 5)).Outcome);
        Assert.Equal(0, h.Coordinator.ActiveAppointments);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void P04_T05_clients_and_multiplayer_keep_dealer_vanilla(bool isServer, int players)
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Coordinator.SetAuthority(isServer, players);

        Assert.Equal(OrderOutcome.ModDisabled, h.Accept(ContractA, 100f, (OgKush, 5)).Outcome);
        Assert.Null(h.Coordinator.Find(ContractA));
    }

    [Fact]
    public void P04_T06_disabled_or_empty_category_keeps_dealer_vanilla()
    {
        var disabled = new DealerHarness(PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.Disabled, null))).Stock(Benji, OgKush, 10);
        var empty = new DealerHarness(PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, null))).Stock(Benji, OgKush, 10);

        Assert.Equal(OrderOutcome.CategoryNotEligible, disabled.Accept(ContractA, 100f, (OgKush, 5)).Outcome);
        Assert.Equal(OrderOutcome.CategoryNotEligible, empty.Accept(ContractA, 100f, (OgKush, 5)).Outcome);
    }

    [Fact]
    public void P04_T07_mixed_category_contract_keeps_dealer_vanilla()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10).Stock(Benji, Meth1, 10);

        Assert.Equal(OrderOutcome.MixedCategories, h.Accept(ContractA, 100f, (OgKush, 2), (Meth1, 2)).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad id/with slash")]
    public void P04_T08_unidentified_dealer_keeps_contract_vanilla(string? dealer)
    {
        var h = new DealerHarness();

        var created = h.Coordinator.OnContractAccepted(DealerRequest(ContractA, (OgKush, 5)), dealer, null);

        Assert.Equal(OrderOutcome.InvalidContract, created.Outcome);
        Assert.Equal(0, h.Coordinator.ActiveAppointments);
    }

    [Fact]
    public void P04_T09_repeated_acceptance_keeps_one_snapshot_and_one_reservation()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        var first = h.Accept(ContractA, 100f, (OgKush, 5));

        h.Policies.Update(p => p.WithMixedDeliveries(false));
        var again = h.Coordinator.OnContractAccepted(DealerRequest(ContractA, (OgKush, 5)), Benji, h.StockOf(Benji));

        Assert.Same(first.Appointment, again.Appointment);
        Assert.True(again.Appointment!.Order.MixedDeliveries);
        Assert.Equal(1, h.Coordinator.Ledger.ActiveCount);
        Assert.Equal(5, h.Coordinator.Ledger.HeldUnitsTotal);
    }

    [Fact]
    public void P04_T10_snapshot_survives_later_policy_changes()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));

        // OG Kush unticked after acceptance: this appointment still delivers it; new ones do not.
        h.Policies.Update(p => p.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, new[] { SourDiesel })));
        h.Accept(ContractB, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);
        Assert.True(result.Settled);
        Assert.Equal(new[] { (OgKush, 5) }, result.Items);
        Assert.Equal(AppointmentState.WaitingForEligibleStock, h.Coordinator.Find(ContractB)!.State);
    }
}
