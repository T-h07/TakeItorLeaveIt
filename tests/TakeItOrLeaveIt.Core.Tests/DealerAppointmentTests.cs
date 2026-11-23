using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite C: the appointment lifecycle, waiting for stock, replanning and the handover decision.</summary>
public class DealerAppointmentTests
{
    [Fact]
    public void P04_T23_short_stock_waits_instead_of_failing()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 2);

        var created = h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.True(created.IsManaged);
        Assert.Equal(AppointmentState.WaitingForEligibleStock, created.Appointment!.State);
        Assert.Equal(ReserveOutcome.InsufficientStock, created.Reserve!.Outcome);
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
        Assert.Equal(1, h.Coordinator.WaitingAppointments);
    }

    [Fact]
    public void P04_T24_waiting_appointment_reserves_when_stock_arrives()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 2);
        h.Accept(ContractA, 100f, (OgKush, 5));

        h.Stock(Benji, SourDiesel, 3);
        var report = h.Tick(Benji);

        Assert.Equal(new[] { ContractA }, report.Reserved);
        Assert.Equal(AppointmentState.Reserved, h.Coordinator.Find(ContractA)!.State);
        Assert.Equal(5, h.Coordinator.Ledger.HeldUnitsTotal);
    }

    [Fact]
    public void P04_T25_waiting_appointments_are_served_in_acceptance_order_and_bounded()
    {
        var h = new DealerHarness();
        var count = DealerSaleCoordinator.MaxReservationsPerReconcile + 4;
        for (var i = 1; i <= count; i++)
        {
            h.Accept(Contract(i), 10f, (OgKush, 1));
        }

        h.Stock(Benji, OgKush, count);
        var first = h.Tick(Benji);
        var second = h.Tick(Benji);

        Assert.Equal(Enumerable.Range(1, DealerSaleCoordinator.MaxReservationsPerReconcile).Select(Contract), first.Reserved);
        Assert.Equal(4, second.Reserved.Count);
        Assert.Equal(0, h.Coordinator.WaitingAppointments);
    }

    [Fact]
    public void P04_T26_dealer_delivers_a_permitted_substitute()
    {
        var h = new DealerHarness().Stock(Benji, SourDiesel, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.True(result.Settled);
        Assert.Equal(HandoverDecision.Fulfil, result.Decision);
        Assert.Equal(new[] { (SourDiesel, 5) }, result.Items);
        Assert.Equal(5, h.UnitsOf(Benji, SourDiesel));
        Assert.Equal(5, h.UnitsRemoved);
    }

    [Fact]
    public void P04_T27_mixed_delivery_removes_exactly_the_plan()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 2).Stock(Benji, SourDiesel, 2).Stock(Benji, Gdp, 2);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.True(result.Settled);
        Assert.Equal(5, result.Items.Sum(i => i.Units));
        Assert.Equal(1, h.TotalUnits(Benji));
        Assert.Equal(3, h.RemovalCalls); // one removal per planned product
    }

    [Fact]
    public void P04_T28_no_eligible_stock_returns_to_vanilla_before_removal()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 2);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Vanilla, result.Decision);
        Assert.False(result.Settled);
        Assert.Null(h.Coordinator.Find(ContractA));
        Assert.Equal(2, h.UnitsOf(Benji, OgKush)); // vanilla's short delivery was handed back
        Assert.Equal(0, h.Payments);
        Assert.Contains(h.Log.Infos, m => m.Contains("Returned to vanilla"));
    }

    [Fact]
    public void P04_T29_second_handover_removes_nothing()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 20);
        h.Accept(ContractA, 100f, (OgKush, 5));
        var plan = h.Coordinator.PrepareHandover(ContractA, h.StockOf(Benji));

        var again = h.Coordinator.PrepareHandover(ContractA, h.StockOf(Benji));

        Assert.Equal(HandoverDecision.Fulfil, plan.Decision);
        Assert.Equal(HandoverDecision.NothingToGive, again.Decision);
        Assert.Empty(again.Lines);
        Assert.NotNull(h.Coordinator.LastError);
    }

    [Fact]
    public void P04_T30_lost_stock_replans_to_another_product()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 5).Stock(Benji, SourDiesel, 1);
        h.Accept(ContractA, 100f, (OgKush, 5));

        h.Take(Benji, OgKush, 5);
        h.Stock(Benji, SourDiesel, 4);
        var result = h.Handover(ContractA);

        Assert.True(result.Settled);
        Assert.Equal(new[] { (SourDiesel, 5) }, result.Items.GroupBy(i => i.Product).Select(g => (g.Key, g.Sum(i => i.Units))));
    }

    [Fact]
    public void P04_T31_repeated_stock_loss_returns_to_vanilla()
    {
        var h = new DealerHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));
        for (var i = 0; i <= DealerSaleCoordinator.MaxInvalidations; i++)
        {
            h.Stock(Benji, OgKush, 5);
            h.Tick(Benji);
            h.Take(Benji, OgKush, 5);
            h.Tick(Benji);
        }

        Assert.Null(h.Coordinator.Find(ContractA));
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
    }

    [Fact]
    public void P04_T32_contract_moved_to_another_dealer_returns_to_vanilla()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10).Stock(Molly, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));

        h.Reassign(ContractA, Molly);
        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Vanilla, result.Decision);
        Assert.Equal(10, h.UnitsOf(Benji, OgKush)); // Benji's hold released, nothing taken from him
        Assert.Equal(0, h.Coordinator.Ledger.HeldUnitsTotal);
    }

    [Theory]
    [InlineData(SaleEnd.Expired, AppointmentState.Expired)]
    [InlineData(SaleEnd.Canceled, AppointmentState.Canceled)]
    [InlineData(SaleEnd.Failed, AppointmentState.Failed)]
    public void P04_T33_ending_a_contract_releases_its_stock(SaleEnd end, AppointmentState expected)
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 5);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var ended = h.Coordinator.OnContractEnded(ContractA, end);
        var second = h.Accept(ContractB, 100f, (OgKush, 5));

        Assert.Equal(expected, ended!.State);
        Assert.Equal(AppointmentState.Reserved, second.Appointment!.State);
        Assert.Null(h.Coordinator.Find(ContractA));
        Assert.Null(h.Coordinator.OnContractEnded(ContractA, end));
        Assert.Equal(5, h.UnitsOf(Benji, OgKush));
    }

    [Fact]
    public void P04_T34_reconcile_is_skipped_until_something_changes()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 2);
        h.Accept(ContractA, 100f, (OgKush, 5));

        // Acceptance already reconciled this dealer with the same stock.
        var unchanged = h.Tick(Benji);
        var again = h.Tick(Benji);
        h.Stock(Benji, OgKush, 3);
        var changed = h.Tick(Benji);

        Assert.True(unchanged.Skipped);
        Assert.True(again.Skipped);
        Assert.Equal(1, again.StillWaiting);
        Assert.False(changed.Skipped);
        Assert.Equal(new[] { ContractA }, changed.Reserved);
    }
}
