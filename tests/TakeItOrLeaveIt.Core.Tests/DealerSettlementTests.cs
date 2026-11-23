using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Suite D: economy safety against the simulated vanilla settlement (<see cref="DealerHarness"/>). These prove the
/// model; the in-game tests confirm the real payment, cut and XP.
/// </summary>
public class DealerSettlementTests
{
    [Fact]
    public void P04_T35_one_dealer_sale_settles_exactly_once()
    {
        var h = new DealerHarness().Stock(Benji, SourDiesel, 10).Stock(Benji, Meth1, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.True(result.Settled);
        Assert.Equal((1, 1, 1), (h.Payments, h.XpAwards, h.Completions));
        Assert.Equal(80f, h.PlayerCash, 3);
        Assert.Equal(20f, h.DealerCash, 3);
        Assert.Equal((5, 10), (h.UnitsOf(Benji, SourDiesel), h.UnitsOf(Benji, Meth1)));
        Assert.Equal(0, h.Coordinator.ActiveAppointments);
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
    }

    [Fact]
    public void P04_T36_repeated_settlement_callbacks_are_ignored()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        var plan = h.Coordinator.PrepareHandover(ContractA, h.StockOf(Benji));
        h.Coordinator.CompleteHandover(ContractA, 5);

        var first = h.Coordinator.OnHandoverProcessed(ContractA);
        var second = h.Coordinator.OnHandoverProcessed(ContractA);
        var completed = h.Coordinator.OnContractEnded(ContractA, SaleEnd.Completed);
        var late = h.Coordinator.OnHandoverProcessed(ContractA);

        Assert.Equal(HandoverDecision.Fulfil, plan.Decision);
        Assert.Equal((CommitObservation.First, CommitObservation.Duplicate, CommitObservation.NotManaged), (first, second, late));
        Assert.Equal(2, completed!.CommitCount);
        Assert.Equal(AppointmentState.Completed, completed.State);
        Assert.False(h.Coordinator.CompleteHandover(ContractA, 5));
    }

    [Fact]
    public void P04_T37_refused_delivery_hands_items_back_and_pays_nothing()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA, customerAccepts: false);

        Assert.False(result.Settled);
        Assert.Equal((0, 0, 0f, 0f), (h.Payments, h.XpAwards, h.PlayerCash, h.DealerCash));
        Assert.Equal(10, h.UnitsOf(Benji, OgKush));
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount); // the hold ended with the removal; no phantom left behind
    }

    [Fact]
    public void P04_T38_unmanaged_dealer_contract_is_exactly_vanilla()
    {
        var h = new DealerHarness(PolicyConfiguration.Default.WithEnabled(false)).Stock(Benji, SourDiesel, 10).Stock(Benji, OgKush, 5);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Vanilla, result.Decision);
        Assert.True(result.Settled);
        Assert.Equal(new[] { (OgKush, 5) }, result.Items);
        Assert.Equal(10, h.UnitsOf(Benji, SourDiesel));
    }

    [Fact]
    public void P04_T39_forbidden_products_are_never_substituted()
    {
        var selected = PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, new[] { OgKush }));
        var h = new DealerHarness(selected).Stock(Benji, SourDiesel, 50).Stock(Benji, Gdp, 50).Stock(Benji, Meth1, 50).Stock(Benji, OgKush, 3);
        h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.Equal(AppointmentState.WaitingForEligibleStock, h.Coordinator.Find(ContractA)!.State);
        var result = h.Handover(ContractA);

        Assert.DoesNotContain(result.Items, i => i.Product != OgKush);
        Assert.Equal((50, 50, 50), (h.UnitsOf(Benji, SourDiesel), h.UnitsOf(Benji, Gdp), h.UnitsOf(Benji, Meth1)));
        Assert.Equal(0, h.Payments);
    }

    [Fact]
    public void P04_T40_customer_judges_the_dealer_delivery_with_the_snapshot()
    {
        var h = new DealerHarness().Stock(Benji, SourDiesel, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Coordinator.PrepareHandover(ContractA, h.StockOf(Benji));
        h.Coordinator.CompleteHandover(ContractA, 5);

        var valid = h.Coordinator.Validate(ContractA, new List<DeliveredItem?> { Item("0", SourDiesel, 5) });
        var wrong = h.Coordinator.Validate(ContractA, new List<DeliveredItem?> { Item("0", Meth1, 5) });
        var short_ = h.Coordinator.Validate(ContractA, new List<DeliveredItem?> { Item("0", SourDiesel, 4) });

        Assert.True(valid!.IsValid);
        Assert.Equal(1f, DeliveryMatch.Compute(valid, new[] { new RatedUnits(5, 1f) }).Match, 3);
        Assert.Equal(DeliveryRejection.WrongCategory, wrong!.Rejection);
        Assert.Equal(DeliveryRejection.TooFew, short_!.Rejection);
        Assert.Null(h.Coordinator.Validate(ContractB, new List<DeliveredItem?> { Item("0", SourDiesel, 5) }));
    }
}
