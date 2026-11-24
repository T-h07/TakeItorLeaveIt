using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>Lifecycle of one inventory reservation. Released, Expired and Invalidated are terminal.</summary>
public enum ReservationState
{
    /// <summary>Planned but not yet holding stock (only inside <see cref="ReservationLedger.TryReserve"/>).</summary>
    Proposed,

    /// <summary>Holding units of the dealer's stock against other appointments.</summary>
    Reserved,

    /// <summary>The game is removing the planned units right now (still holding).</summary>
    Committing,

    /// <summary>The units left the dealer's inventory through the game's own removal; the hold is released.</summary>
    CommitObserved,

    /// <summary>Given up without a commit (contract ended or order returned to vanilla).</summary>
    Released,

    /// <summary>The contract expired before the handover.</summary>
    Expired,

    /// <summary>The reserved stock disappeared (sold, moved, lost); the appointment must replan.</summary>
    Invalidated,
}

/// <summary>Units of one product (at one quality tier, Phase 05) a reservation holds or will draw.</summary>
public sealed class ReservationLine
{
    internal ReservationLine(ProductKey product, int units, QualityTier? quality = null)
    {
        Product = product;
        Units = units;
        Quality = quality;
    }

    public ProductKey Product { get; }

    public int Units { get; }

    /// <summary>The lot's quality tier; null when unknown (only reserved for orders without a quality minimum).</summary>
    public QualityTier? Quality { get; }

    public StockLot Lot => new(Product, Quality);

    public override string ToString() => Quality is { } q ? $"{Units} x {Product} ({q})" : $"{Units} x {Product}";
}

/// <summary>One contract's claim on one dealer's stock. Only <see cref="ReservationLedger"/> changes it.</summary>
public sealed class Reservation
{
    internal Reservation(string contractId, string dealerId, long sequence, IReadOnlyList<ReservationLine> lines)
    {
        ContractId = contractId;
        DealerId = dealerId;
        Sequence = sequence;
        Lines = lines;
        Units = lines.Sum(l => l.Units);
        State = ReservationState.Proposed;
    }

    /// <summary>One reservation per contract: the contract id is the reservation id.</summary>
    public string ContractId { get; }

    /// <summary>The owner: the dealer whose stock is held.</summary>
    public string DealerId { get; }

    /// <summary>Ledger-wide creation order (newer reservations are invalidated first).</summary>
    public long Sequence { get; }

    public IReadOnlyList<ReservationLine> Lines { get; }

    public int Units { get; }

    public ReservationState State { get; private set; }

    /// <summary>True while the reservation holds units against the dealer's stock.</summary>
    public bool IsHolding => State is ReservationState.Reserved or ReservationState.Committing;

    public bool IsTerminal => IsTerminalState(State);

    public static bool IsTerminalState(ReservationState state)
        => state is ReservationState.Released or ReservationState.Expired or ReservationState.Invalidated or ReservationState.CommitObserved;

    /// <summary>Allowed transitions; anything else is refused.</summary>
    public static bool CanTransition(ReservationState from, ReservationState to) => (from, to) switch
    {
        (ReservationState.Proposed, ReservationState.Reserved) => true,
        (ReservationState.Reserved, ReservationState.Committing) => true,
        (ReservationState.Reserved or ReservationState.Committing, ReservationState.CommitObserved) => true,
        (ReservationState.Reserved or ReservationState.Committing, ReservationState.Released) => true,
        (ReservationState.Reserved, ReservationState.Expired or ReservationState.Invalidated) => true,
        _ => false,
    };

    internal bool TryMove(ReservationState to)
    {
        if (!CanTransition(State, to))
        {
            return false;
        }

        State = to;
        return true;
    }

    public override string ToString() => $"{ContractId}@{DealerId} [{State}] {string.Join(", ", Lines)}";
}

public enum ReserveOutcome
{
    /// <summary>A new reservation now holds the units.</summary>
    Reserved,

    /// <summary>This contract already holds a reservation with this dealer (idempotent repeat).</summary>
    AlreadyReserved,

    /// <summary>Not enough eligible, unreserved units: the appointment waits for stock.</summary>
    InsufficientStock,

    /// <summary>The order's category has no eligible product any more (should not happen with a snapshot).</summary>
    NotEligible,

    /// <summary>The contract already holds a reservation with another dealer.</summary>
    OwnerMismatch,

    /// <summary>The order or stock does not belong to this request (bad ids).</summary>
    InvalidRequest,
}

public sealed class ReserveResult
{
    internal ReserveResult(ReserveOutcome outcome, Reservation? reservation, long eligibleAvailable, string detail)
    {
        Outcome = outcome;
        Reservation = reservation;
        EligibleAvailable = eligibleAvailable;
        Detail = detail;
    }

    public ReserveOutcome Outcome { get; }

    /// <summary>The holding reservation (new or existing) for Reserved / AlreadyReserved.</summary>
    public Reservation? Reservation { get; }

    /// <summary>Eligible units of the dealer not held by other appointments at the time of the request.</summary>
    public long EligibleAvailable { get; }

    public string Detail { get; }

    public bool IsHolding => Outcome is ReserveOutcome.Reserved or ReserveOutcome.AlreadyReserved;
}

/// <summary>
/// Accounts for hired-dealer stock promised to managed appointments, so the same units are never promised
/// twice. It never touches the game's inventory: it compares the dealer's current stock snapshot with what
/// that dealer's other appointments hold.
/// <para><b>Invariants</b> (checked by <see cref="CheckInvariants"/>): per dealer and product, the held units
/// equal the sum of the holding reservations' lines, are never negative, and never exceed the dealer's stock
/// at the last reconcile; one reservation per contract; a reservation holds only its owner's stock.</para>
/// <para>Atomic: a request either creates a complete reservation or changes nothing. Product choice reuses
/// <see cref="InventoryAllocationPlanner"/> with the order's snapshot policy, so eligibility is decided by the
/// same <c>SaleEligibilityPolicy</c> as everywhere else. Main-thread only.</para>
/// </summary>
public sealed class ReservationLedger
{
    private readonly Dictionary<string, Reservation> _byContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<StockLot, long>> _held = new(StringComparer.Ordinal);
    private long _sequence;

    /// <summary>Holding reservations (Reserved or Committing).</summary>
    public int ActiveCount => _byContract.Count;

    public long HeldUnitsTotal => _held.Values.Sum(d => d.Values.Sum());

    /// <summary>Bumped on every change.</summary>
    public long Version { get; private set; }

    public IReadOnlyList<Reservation> Active => _byContract.Values.OrderBy(r => r.Sequence).ToList();

    public bool TryGet(string? contractId, out Reservation reservation)
    {
        if (contractId is not null && _byContract.TryGetValue(contractId, out var found))
        {
            reservation = found;
            return true;
        }

        reservation = null!;
        return false;
    }

    /// <summary>Held units of a product over all its quality lots.</summary>
    public long HeldUnits(string dealerId, ProductKey product)
        => _held.TryGetValue(dealerId, out var byLot) ? byLot.Where(p => p.Key.Product == product).Sum(p => p.Value) : 0;

    public long HeldUnits(string dealerId, StockLot lot)
        => _held.TryGetValue(dealerId, out var byLot) && byLot.TryGetValue(lot, out var units) ? units : 0;

    /// <summary>Units of <paramref name="product"/> the dealer has that no other appointment holds (any quality).</summary>
    public long Available(DealerStock stock, ProductKey product, string? excludingContract = null)
        => Available(stock, product, null, excludingContract);

    /// <summary>
    /// Unheld units of <paramref name="product"/> at or above <paramref name="minimum"/>, lot by lot (holds are per lot, so
    /// a hold on Premium units never hides Standard ones and the reverse).
    /// </summary>
    public long Available(DealerStock stock, ProductKey product, QualityTier? minimum, string? excludingContract = null)
        => stock.Lots.Where(l => l.Product == product && l.Meets(minimum)).Sum(l => Available(stock, l, excludingContract));

    public long Available(DealerStock stock, StockLot lot, string? excludingContract = null)
    {
        var held = HeldUnits(stock.DealerId, lot);
        if (excludingContract is not null && _byContract.TryGetValue(excludingContract, out var own) && own.DealerId == stock.DealerId)
        {
            held -= own.Lines.Where(l => l.Lot.Equals(lot)).Sum(l => (long)l.Units);
        }

        return Math.Max(0, stock.UnitsOf(lot) - held);
    }

    /// <summary>
    /// Reserves <see cref="CategoryOrder.RequiredUnits"/> eligible units of the dealer's stock for the order, or
    /// changes nothing. Only lots at or above the order's quality minimum count. Any mix allowed: the planner's
    /// deterministic draw across eligible lots (by product, then lowest acceptable quality first). Otherwise one
    /// combination group (one product, or one base strain) with enough unreserved units: the one with the most,
    /// ties by group id.
    /// </summary>
    public ReserveResult TryReserve(CategoryOrder order, DealerStock stock, ProductCatalogSnapshot? catalog)
    {
        if (order is null || stock is null)
        {
            return new ReserveResult(ReserveOutcome.InvalidRequest, null, 0, "Missing order or stock.");
        }

        if (_byContract.TryGetValue(order.ContractId, out var existing))
        {
            return existing.DealerId == stock.DealerId
                ? new ReserveResult(ReserveOutcome.AlreadyReserved, existing, 0, "Already reserved.")
                : new ReserveResult(ReserveOutcome.OwnerMismatch, existing, 0, $"Held by dealer {existing.DealerId}.");
        }

        // Pool of unreserved units per lot, one virtual stack per lot (ids sort by product, then lowest quality first).
        var pool = new List<InventoryStack>();
        foreach (var lot in stock.Lots)
        {
            if (!order.IsAllowed(lot.Product) || !lot.Meets(order.MinimumQuality))
            {
                continue;
            }

            var available = Available(stock, lot);
            if (available > 0)
            {
                pool.Add(new InventoryStack(lot.Id, lot.Product, (int)Math.Min(available, int.MaxValue), lot.Quality));
            }
        }

        var policy = order.ToSnapshotPolicy();
        AllocationPlan? plan = null;
        long eligibleTotal = 0;
        var groups = pool.SelectMany(stack => order.GroupsOf(stack.Product)).Distinct(StringComparer.Ordinal).ToList();
        if (groups.Count <= 1 && order.MixedDeliveries)
        {
            plan = InventoryAllocationPlanner.Plan(order.Category, order.RequiredUnits, pool, policy, catalog);
            eligibleTotal = plan.EligibleQuantityAvailable;
        }
        else
        {
            var candidates = groups
                .Select(g => (Group: g, Stacks: pool.Where(stack => order.GroupsOf(stack.Product).Contains(g, StringComparer.Ordinal)).ToList()))
                .Select(c => (c.Group, c.Stacks, Units: c.Stacks.Sum(stack => (long)stack.Quantity)))
                .OrderByDescending(c => c.Units)
                .ThenBy(c => c.Group, StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                var attempt = InventoryAllocationPlanner.Plan(order.Category, order.RequiredUnits, candidate.Stacks, policy, catalog);
                eligibleTotal = Math.Max(eligibleTotal, attempt.EligibleQuantityAvailable);
                if (attempt.CanFulfill)
                {
                    plan = attempt;
                    break;
                }

                plan ??= attempt;
            }

            plan ??= InventoryAllocationPlanner.Plan(order.Category, order.RequiredUnits, Array.Empty<InventoryStack>(), policy, catalog);
        }

        if (!plan.CanFulfill)
        {
            var outcome = plan.Failure == AllocationFailure.CategoryNotEligible ? ReserveOutcome.NotEligible : ReserveOutcome.InsufficientStock;
            var quality = order.MinimumQuality is { } q ? $" at {q} or better" : string.Empty;
            var detail = order.MixedDeliveries
                ? $"{eligibleTotal}/{order.RequiredUnits} eligible unreserved unit(s){quality}."
                : $"No single {(order.Mixing == MixingMode.SingleBaseStrain ? "base strain" : "product")} has {order.RequiredUnits} eligible unreserved unit(s){quality} (best {eligibleTotal}); mixing is limited.";
            return new ReserveResult(outcome, null, eligibleTotal, detail);
        }

        var lines = plan.Allocations
            .GroupBy(a => new StockLot(a.Product, a.Quality))
            .OrderBy(g => g.Key)
            .Select(g => new ReservationLine(g.Key.Product, g.Sum(a => a.Quantity), g.Key.Quality))
            .ToList();
        var reservation = new Reservation(order.ContractId, stock.DealerId, ++_sequence, lines);
        reservation.TryMove(ReservationState.Reserved);
        _byContract.Add(order.ContractId, reservation);
        _minimums[order.ContractId] = order.MinimumQuality;
        Hold(reservation, +1);
        Version++;
        return new ReserveResult(ReserveOutcome.Reserved, reservation, eligibleTotal, string.Join(", ", lines));
    }

    /// <summary>Reserved → Committing (the game is about to remove the units). False if not holding.</summary>
    public bool BeginCommit(string? contractId)
    {
        if (!TryGet(contractId, out var reservation) || !reservation.TryMove(ReservationState.Committing))
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>
    /// The planned units left the inventory: the reservation ends as CommitObserved and its hold is released
    /// (the stock is gone, so nothing may stay promised). Returns the reservation once; null afterwards.
    /// </summary>
    public Reservation? ObserveCommit(string? contractId) => Finish(contractId, ReservationState.CommitObserved);

    /// <summary>Gives up the reservation (Released, Expired or Invalidated). Idempotent: null when none.</summary>
    public Reservation? Release(string? contractId, ReservationState end = ReservationState.Released)
    {
        if (end is not (ReservationState.Released or ReservationState.Expired or ReservationState.Invalidated))
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Release ends a reservation without a commit.");
        }

        return Finish(contractId, end);
    }

    /// <summary>
    /// Compares the dealer's holds with its current stock. Where a product's held units exceed the stock, the
    /// newest Reserved reservations holding that product are invalidated until the rest fits. Committing
    /// reservations are never invalidated (their removal is in progress). Returns the invalidated contract ids.
    /// </summary>
    public IReadOnlyList<string> Reconcile(DealerStock stock)
    {
        var invalidated = new List<string>();
        if (stock is null || !_held.TryGetValue(stock.DealerId, out var byLot))
        {
            return invalidated;
        }

        foreach (var lot in byLot.Keys.OrderBy(l => l).ToList())
        {
            var newestFirst = _byContract.Values
                .Where(r => r.DealerId == stock.DealerId && r.State == ReservationState.Reserved && r.Lines.Any(l => l.Lot.Equals(lot)))
                .OrderByDescending(r => r.Sequence)
                .ToList();
            foreach (var reservation in newestFirst)
            {
                if (HeldUnits(stock.DealerId, lot) <= stock.UnitsOf(lot))
                {
                    break;
                }

                Finish(reservation.ContractId, ReservationState.Invalidated);
                invalidated.Add(reservation.ContractId);
            }
        }

        return invalidated;
    }

    /// <summary>Releases every reservation of one dealer (dealer gone, fired, or save closing). Returns how many.</summary>
    public int ReleaseDealer(string dealerId)
    {
        var ids = _byContract.Values.Where(r => r.DealerId == dealerId).Select(r => r.ContractId).ToList();
        foreach (var id in ids)
        {
            Finish(id, ReservationState.Released);
        }

        return ids.Count;
    }

    public void Clear()
    {
        if (_byContract.Count > 0 || _held.Count > 0)
        {
            _byContract.Clear();
            _held.Clear();
            _minimums.Clear();
            Version++;
        }
    }

    /// <summary>
    /// Every broken invariant as text (empty when consistent). <paramref name="stockOf"/> gives the latest stock
    /// snapshot per dealer, or null to skip the stock comparison for that dealer.
    /// </summary>
    public IReadOnlyList<string> CheckInvariants(Func<string, DealerStock?>? stockOf = null)
    {
        var problems = new List<string>();
        var expected = new Dictionary<(string, StockLot), long>();
        foreach (var reservation in _byContract.Values)
        {
            if (!reservation.IsHolding)
            {
                problems.Add($"{reservation.ContractId}: kept in the ledger while {reservation.State}.");
            }

            foreach (var line in reservation.Lines)
            {
                if (line.Units <= 0)
                {
                    problems.Add($"{reservation.ContractId}: non-positive line {line}.");
                }

                var key = (reservation.DealerId, line.Lot);
                expected[key] = (expected.TryGetValue(key, out var u) ? u : 0) + line.Units;
            }
        }

        foreach (var dealer in _held)
        {
            var stock = stockOf?.Invoke(dealer.Key);
            foreach (var held in dealer.Value)
            {
                var want = expected.TryGetValue((dealer.Key, held.Key), out var e) ? e : 0;
                if (held.Value < 0)
                {
                    problems.Add($"{dealer.Key}/{held.Key}: negative hold {held.Value}.");
                }

                if (held.Value != want)
                {
                    problems.Add($"{dealer.Key}/{held.Key}: hold {held.Value} != reservations {want}.");
                }

                if (stock is not null && held.Value > stock.UnitsOf(held.Key))
                {
                    problems.Add($"{dealer.Key}/{held.Key}: {held.Value} held but only {stock.UnitsOf(held.Key)} in stock.");
                }

                if (held.Key.Quality is null && _byContract.Values.Any(r => r.DealerId == dealer.Key && r.Lines.Any(l => l.Lot.Equals(held.Key)) &&
                        TryOrderMinimum(r) is not null))
                {
                    problems.Add($"{dealer.Key}/{held.Key}: unknown-quality units held for an order with a quality minimum.");
                }
            }
        }

        foreach (var key in expected.Keys)
        {
            if (!_held.TryGetValue(key.Item1, out var byLotHeld) || !byLotHeld.ContainsKey(key.Item2))
            {
                problems.Add($"{key.Item1}/{key.Item2}: reserved but not held.");
            }
        }

        return problems;
    }

    private Reservation? Finish(string? contractId, ReservationState end)
    {
        if (!TryGet(contractId, out var reservation) || !reservation.TryMove(end))
        {
            return null;
        }

        _byContract.Remove(reservation.ContractId);
        Hold(reservation, -1);
        Version++;
        return reservation;
    }

    /// <summary>The quality minimum a reservation was made for (recorded so invariants can be checked).</summary>
    private QualityTier? TryOrderMinimum(Reservation reservation)
        => _minimums.TryGetValue(reservation.ContractId, out var minimum) ? minimum : null;

    private readonly Dictionary<string, QualityTier?> _minimums = new(StringComparer.Ordinal);

    private void Hold(Reservation reservation, int sign)
    {
        if (!_held.TryGetValue(reservation.DealerId, out var byProduct))
        {
            byProduct = new Dictionary<StockLot, long>();
            _held.Add(reservation.DealerId, byProduct);
        }

        foreach (var line in reservation.Lines)
        {
            var next = (byProduct.TryGetValue(line.Lot, out var u) ? u : 0) + sign * (long)line.Units;
            if (next == 0)
            {
                byProduct.Remove(line.Lot);
            }
            else
            {
                byProduct[line.Lot] = next;
            }
        }

        if (sign < 0)
        {
            _minimums.Remove(reservation.ContractId);
        }

        if (byProduct.Count == 0)
        {
            _held.Remove(reservation.DealerId);
        }
    }
}
