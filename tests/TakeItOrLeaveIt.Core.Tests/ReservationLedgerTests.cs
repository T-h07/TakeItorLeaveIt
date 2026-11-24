using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite B: the inventory reservation ledger (atomic, idempotent, per dealer, never over-reserving).</summary>
public class ReservationLedgerTests
{
    private static readonly Discovery.ProductCatalogSnapshot AllWeed = AllWeedUnlocked();

    [Fact]
    public void P04_T11_reserves_from_one_product()
    {
        var ledger = new ReservationLedger();

        var result = ledger.TryReserve(Order(ContractA, 5), StockOf(Benji, (SourDiesel, 8)), AllWeed);

        Assert.Equal(ReserveOutcome.Reserved, result.Outcome);
        Assert.Equal(new[] { (SourDiesel, 5) }, result.Reservation!.Lines.Select(l => (l.Product, l.Units)));
        Assert.Equal(ReservationState.Reserved, result.Reservation.State);
        Assert.Equal(5, ledger.HeldUnits(Benji, SourDiesel));
    }

    [Fact]
    public void P04_T12_mixed_reservation_is_deterministic()
    {
        var stock = StockOf(Benji, (SourDiesel, 3), (OgKush, 3), (Gdp, 3));

        var a = new ReservationLedger().TryReserve(Order(ContractA, 7), stock, AllWeed);
        var b = new ReservationLedger().TryReserve(Order(ContractA, 7), StockOf(Benji, (Gdp, 3), (OgKush, 3), (SourDiesel, 3)), AllWeed);

        // Product id order: granddaddypurple, ogkush, sourdiesel.
        var expected = new[] { (Gdp, 3), (OgKush, 3), (SourDiesel, 1) };
        Assert.Equal(expected, a.Reservation!.Lines.Select(l => (l.Product, l.Units)));
        Assert.Equal(expected, b.Reservation!.Lines.Select(l => (l.Product, l.Units)));
    }

    [Fact]
    public void P04_T13_mixed_off_uses_one_product_with_enough_stock()
    {
        var ledger = new ReservationLedger();

        var result = ledger.TryReserve(Order(ContractA, 5, mixed: false), StockOf(Benji, (Gdp, 3), (OgKush, 6), (SourDiesel, 9)), AllWeed);

        Assert.Equal(new[] { (SourDiesel, 5) }, result.Reservation!.Lines.Select(l => (l.Product, l.Units)));
    }

    [Fact]
    public void P04_T14_mixed_off_never_combines_products()
    {
        var ledger = new ReservationLedger();

        var result = ledger.TryReserve(Order(ContractA, 5, mixed: false), StockOf(Benji, (Gdp, 3), (OgKush, 4)), AllWeed);

        Assert.Equal(ReserveOutcome.InsufficientStock, result.Outcome);
        Assert.Equal(4, result.EligibleAvailable);
        Assert.Equal(0, ledger.ActiveCount);
    }

    [Fact]
    public void P04_T15_two_appointments_never_share_units()
    {
        var ledger = new ReservationLedger();
        var stock = StockOf(Benji, (OgKush, 8));

        Assert.Equal(ReserveOutcome.Reserved, ledger.TryReserve(Order(ContractA, 5), stock, AllWeed).Outcome);
        var second = ledger.TryReserve(Order(ContractB, 5), stock, AllWeed);

        Assert.Equal(ReserveOutcome.InsufficientStock, second.Outcome);
        Assert.Equal(3, second.EligibleAvailable);
        Assert.Equal(5, ledger.HeldUnits(Benji, OgKush));
        Assert.Empty(ledger.CheckInvariants(_ => stock));
    }

    [Fact]
    public void P04_T16_each_dealer_only_promises_its_own_stock()
    {
        var ledger = new ReservationLedger();
        ledger.TryReserve(Order(ContractA, 8), StockOf(Benji, (OgKush, 8)), AllWeed);

        var molly = ledger.TryReserve(Order(ContractB, 8), StockOf(Molly, (OgKush, 8)), AllWeed);

        Assert.Equal(ReserveOutcome.Reserved, molly.Outcome);
        Assert.Equal(8, ledger.HeldUnits(Benji, OgKush));
        Assert.Equal(8, ledger.HeldUnits(Molly, OgKush));
        Assert.Equal(0, ledger.Available(StockOf(Benji, (OgKush, 8)), OgKush));
    }

    [Fact]
    public void P04_T17_repeated_reserve_is_idempotent()
    {
        var ledger = new ReservationLedger();
        var stock = StockOf(Benji, (OgKush, 20));
        var first = ledger.TryReserve(Order(ContractA, 5), stock, AllWeed);
        var version = ledger.Version;

        var again = ledger.TryReserve(Order(ContractA, 5), stock, AllWeed);

        Assert.Equal(ReserveOutcome.AlreadyReserved, again.Outcome);
        Assert.Same(first.Reservation, again.Reservation);
        Assert.Equal(5, ledger.HeldUnitsTotal);
        Assert.Equal(version, ledger.Version);
    }

    [Fact]
    public void P04_T18_contract_cannot_hold_two_dealers_stock()
    {
        var ledger = new ReservationLedger();
        ledger.TryReserve(Order(ContractA, 5), StockOf(Benji, (OgKush, 20)), AllWeed);

        var other = ledger.TryReserve(Order(ContractA, 5), StockOf(Molly, (OgKush, 20)), AllWeed);

        Assert.Equal(ReserveOutcome.OwnerMismatch, other.Outcome);
        Assert.Equal(0, ledger.HeldUnits(Molly, OgKush));
    }

    [Fact]
    public void P04_T19_release_frees_units_once()
    {
        var ledger = new ReservationLedger();
        var stock = StockOf(Benji, (OgKush, 5));
        ledger.TryReserve(Order(ContractA, 5), stock, AllWeed);

        var released = ledger.Release(ContractA);
        var again = ledger.Release(ContractA);

        Assert.Equal(ReservationState.Released, released!.State);
        Assert.Null(again);
        Assert.Equal(0, ledger.HeldUnitsTotal);
        Assert.Equal(ReserveOutcome.Reserved, ledger.TryReserve(Order(ContractB, 5), stock, AllWeed).Outcome);
    }

    [Fact]
    public void P04_T20_ineligible_products_are_never_reserved()
    {
        var ledger = new ReservationLedger();
        var catalog = Snapshot(Standard()); // GDP locked
        var stock = StockOf(Benji, (Gdp, 10), (Meth1, 10), (OgKush, 2), (SourDiesel, 10));

        // Snapshot allows OG Kush and GDP only; Sour Diesel is not allowed, GDP is locked, meth is another category.
        var result = ledger.TryReserve(Order(ContractA, 5, true, OgKush, Gdp), stock, catalog);

        Assert.Equal(ReserveOutcome.InsufficientStock, result.Outcome);
        Assert.Equal(2, result.EligibleAvailable);
        Assert.Equal(0, ledger.HeldUnitsTotal);
    }

    [Fact]
    public void P04_T21_lost_stock_invalidates_newest_reservations_first()
    {
        var ledger = new ReservationLedger();
        var full = StockOf(Benji, (OgKush, 9));
        ledger.TryReserve(Order(ContractA, 3), full, AllWeed);
        ledger.TryReserve(Order(ContractB, 3), full, AllWeed);
        ledger.TryReserve(Order(ContractC, 3), full, AllWeed);

        var invalidated = ledger.Reconcile(StockOf(Benji, (OgKush, 4)));

        Assert.Equal(new[] { ContractC, ContractB }, invalidated);
        Assert.True(ledger.TryGet(ContractA, out _));
        Assert.Equal(3, ledger.HeldUnits(Benji, OgKush));
        Assert.Empty(ledger.CheckInvariants(_ => StockOf(Benji, (OgKush, 4))));
    }

    [Fact]
    public void P04_T22_commit_releases_the_hold_exactly_once()
    {
        var ledger = new ReservationLedger();
        ledger.TryReserve(Order(ContractA, 5), StockOf(Benji, (OgKush, 5)), AllWeed);

        Assert.True(ledger.BeginCommit(ContractA));
        Assert.False(ledger.BeginCommit(ContractA));
        Assert.Empty(ledger.Reconcile(StockOf(Benji))); // Committing is never invalidated
        var committed = ledger.ObserveCommit(ContractA);
        var again = ledger.ObserveCommit(ContractA);

        Assert.Equal(ReservationState.CommitObserved, committed!.State);
        Assert.Null(again);
        Assert.Equal(0, ledger.HeldUnitsTotal);
        Assert.Empty(ledger.CheckInvariants(_ => StockOf(Benji)));
        Assert.False(Reservation.CanTransition(ReservationState.CommitObserved, ReservationState.Reserved));
        Assert.False(Reservation.CanTransition(ReservationState.Released, ReservationState.Committing));
    }
}
