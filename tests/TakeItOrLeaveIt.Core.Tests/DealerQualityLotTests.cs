using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 05 suite D: dealer stock lots, quality-aware reservations, monitoring and base-strain groups.</summary>
public class DealerQualityLotTests
{
    private static DealerStock Stock(string dealer, params (ProductKey P, int U, QualityTier? Q)[] stacks)
        => new(dealer, stacks.Select((s, i) => new InventoryStack($"slot:{i}", s.P, s.U, s.Q)));

    private static CategoryOrder Order(string contract, int units, QualityTier? minimum, MixingMode mixing = MixingMode.AnyPermitted, params ProductKey[] allowed)
        => new(contract, Weed, units, allowed.Length > 0 ? allowed : new[] { OgKush, SourDiesel }, mixing == MixingMode.AnyPermitted, 1,
            mixing: mixing, minimumQuality: minimum);

    [Fact]
    public void P05_D01_stock_counts_units_per_lot()
    {
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (OgKush, 3, QualityTier.Premium), (OgKush, 2, null), (OgKush, 1, QualityTier.Premium));

        Assert.Equal(11, stock.UnitsOf(OgKush));
        Assert.Equal(4, stock.UnitsOf(new StockLot(OgKush, QualityTier.Premium)));
        Assert.Equal(4, stock.UnitsOf(OgKush, QualityTier.Standard));
        Assert.Equal(9, stock.UnitsOf(OgKush, QualityTier.Trash)); // unknown quality never meets a minimum
        Assert.Equal(new[] { "ogkush@1", "ogkush@3", "ogkush@?" }, stock.Lots.Select(l => l.Id));
    }

    [Fact]
    public void P05_D02_low_quality_stock_never_satisfies_a_minimum()
    {
        var ledger = new ReservationLedger();
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (OgKush, 3, QualityTier.Premium), (OgKush, 4, null));

        var result = ledger.TryReserve(Order(ContractC, 5, QualityTier.Premium), stock, Snapshot());

        Assert.Equal(ReserveOutcome.InsufficientStock, result.Outcome);
        Assert.Equal(3, result.EligibleAvailable);
        Assert.Contains("Premium or better", result.Detail);
        Assert.Equal(0, ledger.HeldUnits(Benji, OgKush));
    }

    [Fact]
    public void P05_D03_reserves_the_qualifying_lot_and_records_its_quality()
    {
        var ledger = new ReservationLedger();
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (OgKush, 3, QualityTier.Premium));

        var result = ledger.TryReserve(Order(ContractC, 3, QualityTier.Standard), stock, Snapshot());

        Assert.Equal(ReserveOutcome.Reserved, result.Outcome);
        var line = Assert.Single(result.Reservation!.Lines);
        Assert.Equal((OgKush, 3, (QualityTier?)QualityTier.Premium), (line.Product, line.Units, line.Quality));
        Assert.Equal(3, ledger.HeldUnits(Benji, new StockLot(OgKush, QualityTier.Premium)));
        Assert.Equal(0, ledger.HeldUnits(Benji, new StockLot(OgKush, QualityTier.Poor)));
    }

    [Fact]
    public void P05_D04_without_a_minimum_the_lowest_quality_is_drawn_first()
    {
        var ledger = new ReservationLedger();
        var stock = Stock(Benji, (OgKush, 3, QualityTier.Heavenly), (OgKush, 2, QualityTier.Poor), (OgKush, 2, null));

        var result = ledger.TryReserve(Order(ContractC, 4, null), stock, Snapshot());

        Assert.Equal(new (ProductKey, int, QualityTier?)[] { (OgKush, 2, QualityTier.Poor), (OgKush, 2, QualityTier.Heavenly) },
            result.Reservation!.Lines.Select(l => (l.Product, l.Units, l.Quality)));
    }

    [Fact]
    public void P05_D05_holds_on_one_lot_never_hide_another()
    {
        var ledger = new ReservationLedger();
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (OgKush, 3, QualityTier.Premium));

        Assert.Equal(ReserveOutcome.Reserved, ledger.TryReserve(Order(ContractC, 3, QualityTier.Premium), stock, Snapshot()).Outcome);
        var second = ledger.TryReserve(Order(ContractD, 5, null), stock, Snapshot());
        var third = ledger.TryReserve(Order(Contract(0xe), 1, QualityTier.Premium), stock, Snapshot());

        Assert.Equal(ReserveOutcome.Reserved, second.Outcome);
        Assert.Equal(QualityTier.Poor, second.Reservation!.Lines.Single().Quality);
        Assert.Equal(ReserveOutcome.InsufficientStock, third.Outcome);
        Assert.Empty(ledger.CheckInvariants(_ => stock));
    }

    [Fact]
    public void P05_D06_reconcile_invalidates_when_the_reserved_lot_shrinks()
    {
        var ledger = new ReservationLedger();
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (OgKush, 3, QualityTier.Premium));
        ledger.TryReserve(Order(ContractC, 3, QualityTier.Premium), stock, Snapshot());

        // The Premium units leave; Poor units are untouched and must not keep the reservation alive.
        var after = Stock(Benji, (OgKush, 8, QualityTier.Poor), (OgKush, 1, QualityTier.Premium));
        var invalidated = ledger.Reconcile(after);

        Assert.Equal(new[] { ContractC }, invalidated);
        Assert.Equal(0, ledger.HeldUnits(Benji, OgKush));
    }

    [Fact]
    public void P05_D07_monitor_counts_only_qualifying_units()
    {
        var policy = With(MinQuality(QualityTier.Premium));
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (SourDiesel, 2, QualityTier.Heavenly), (MixA, 4, null));

        var units = DealerStockMonitor.EligibleUnitsByCategory(stock, policy, Snapshot());

        Assert.Equal(2, units[Weed]);
        Assert.Equal(11, DealerStockMonitor.EligibleUnitsByCategory(stock, PolicyConfiguration.Default, Snapshot())[Weed]);
    }

    [Fact]
    public void P05_D08_single_base_strain_reservation_stays_within_one_lineage()
    {
        var catalog = Snapshot();
        var policy = PolicyConfiguration.Default.WithMixing(MixingMode.SingleBaseStrain);
        var order = CategoryOrderFactory.CreateForDealer(Sales.DealerRequest(ContractC, (OgKush, 5)), policy, catalog, Prices, Benji).Order!;
        var stock = Stock(Benji, (OgKush, 2, QualityTier.Standard), (MixA, 2, QualityTier.Standard), (MixS, 4, QualityTier.Standard), (SourDiesel, 3, QualityTier.Standard));
        var ledger = new ReservationLedger();

        var result = ledger.TryReserve(order, stock, catalog);

        // OG Kush lineage has 4 units, Sour Diesel lineage has 7: the reservation stays inside Sour Diesel.
        Assert.Equal(ReserveOutcome.Reserved, result.Outcome);
        Assert.All(result.Reservation!.Lines, l => Assert.Contains(SourDiesel.Value, order.GroupsOf(l.Product)));
        Assert.True(order.CanCombine(result.Reservation.Lines.Select(l => l.Product)));
    }

    [Fact]
    public void P05_D09_single_base_strain_reservation_fails_rather_than_crossing_lineages()
    {
        var catalog = Snapshot();
        var policy = PolicyConfiguration.Default.WithMixing(MixingMode.SingleBaseStrain);
        var order = CategoryOrderFactory.CreateForDealer(Sales.DealerRequest(ContractC, (OgKush, 5)), policy, catalog, Prices, Benji).Order!;
        var stock = Stock(Benji, (OgKush, 3, null), (MixS, 3, null));

        var result = new ReservationLedger().TryReserve(order, stock, catalog);

        Assert.Equal(ReserveOutcome.InsufficientStock, result.Outcome);
        Assert.Contains("base strain", result.Detail);
    }

    [Fact]
    public void P05_D10_available_for_an_appointment_respects_quality_and_groups()
    {
        var h = new DealerHarness(With(MinQuality(QualityTier.Standard)), Snapshot());
        var stock = Stock(Benji, (OgKush, 2, QualityTier.Poor), (OgKush, 6, QualityTier.Premium));
        var ledger = new ReservationLedger();
        var order = Order(ContractC, 4, QualityTier.Standard, MixingMode.SingleProduct, OgKush, SourDiesel);
        Assert.Equal(ReserveOutcome.Reserved, ledger.TryReserve(order, stock, h.Catalog).Outcome);

        Assert.Equal(6, ledger.Available(stock, OgKush, QualityTier.Standard, ContractC));
        Assert.Equal(2, ledger.Available(stock, OgKush, QualityTier.Standard));
        Assert.Equal(4, ledger.Available(stock, OgKush, null));
    }
}
