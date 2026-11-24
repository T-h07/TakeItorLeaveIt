using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>Result of accepting a dealer contract.</summary>
public sealed class DealerOrderCreation
{
    internal DealerOrderCreation(OrderOutcome outcome, DealerAppointment? appointment, ReserveResult? reserve, string detail)
    {
        Outcome = outcome;
        Appointment = appointment;
        Reserve = reserve;
        Detail = detail;
    }

    public OrderOutcome Outcome { get; }

    public DealerAppointment? Appointment { get; }

    /// <summary>The first reservation attempt (null when the contract is not managed or was a repeat).</summary>
    public ReserveResult? Reserve { get; }

    public string Detail { get; }

    public bool IsManaged => Outcome == OrderOutcome.Managed && Appointment is not null;
}

public enum HandoverDecision
{
    /// <summary>Not a managed appointment and not an excluded product: run the game's own removal.</summary>
    Vanilla,

    /// <summary>Remove exactly <see cref="HandoverPlan.Lines"/> with the game's removal method, nothing else.</summary>
    Fulfil,

    /// <summary>The appointment already handed its units over: remove nothing (no double removal).</summary>
    NothingToGive,

    /// <summary>
    /// Strict mode: a managed appointment without enough eligible stock (or an unmanaged contract for a product the
    /// settings exclude). Nothing is removed and the game's original removal does not run, so no excluded or partial
    /// stock is ever handed over; the game's own short-delivery handling applies and the appointment keeps waiting.
    /// </summary>
    Withheld,
}

/// <summary>What happened after the game's removal ran for a Fulfil plan.</summary>
public enum HandoverCompletion
{
    /// <summary>Exactly the planned units were removed: the customer gets them, the hold is released.</summary>
    Committed,

    /// <summary>
    /// The removal returned a different number of units: the caller must give every removed item back to the dealer
    /// and hand over nothing (no partial delivery). The appointment waits for stock again.
    /// </summary>
    RolledBack,

    /// <summary>Not a committing managed appointment (repeat call, ended contract): nothing to do.</summary>
    Ignored,
}

public sealed class HandoverPlan
{
    internal HandoverPlan(HandoverDecision decision, DealerAppointment? appointment, IReadOnlyList<ReservationLine> lines, string detail)
    {
        Decision = decision;
        Appointment = appointment;
        Lines = lines;
        Detail = detail;
    }

    public HandoverDecision Decision { get; }

    public DealerAppointment? Appointment { get; }

    /// <summary>Product and units to remove (only for <see cref="HandoverDecision.Fulfil"/>); they sum to the order's units.</summary>
    public IReadOnlyList<ReservationLine> Lines { get; }

    public string Detail { get; }
}

public sealed class ReconcileReport
{
    internal ReconcileReport(bool skipped, IReadOnlyList<string> invalidated, IReadOnlyList<string> reserved, int stillWaiting)
    {
        Skipped = skipped;
        Invalidated = invalidated;
        Reserved = reserved;
        StillWaiting = stillWaiting;
    }

    /// <summary>Nothing changed since the last reconcile of this dealer (same stock, same ledger).</summary>
    public bool Skipped { get; }

    public IReadOnlyList<string> Invalidated { get; }

    public IReadOnlyList<string> Reserved { get; }

    public int StillWaiting { get; }

    public bool Changed => Invalidated.Count + Reserved.Count > 0;
}

/// <summary>A persisted dealer appointment (no reservation: reservations are rebuilt from live stock).</summary>
public sealed class DealerOrderRecord
{
    public DealerOrderRecord(CategoryOrder order, string dealerId, long sequence, bool committed)
    {
        Order = order ?? throw new ArgumentNullException(nameof(order));
        DealerId = dealerId;
        Sequence = sequence;
        Committed = committed;
    }

    public CategoryOrder Order { get; }

    public string DealerId { get; }

    public long Sequence { get; }

    public bool Committed { get; }
}

/// <summary>
/// The single entry point the game hooks call for hired-dealer sales (Phase 04). It owns the managed dealer
/// appointments of the current save and the <see cref="ReservationLedger"/>, and turns game events into Core
/// decisions:
/// <list type="bullet">
/// <item>dealer contract accepted → category snapshot (same factory as player sales) and a stock reservation,
/// or WaitingForEligibleStock,</item>
/// <item>dealer inventory changed → invalidate lost reservations and retry waiting appointments (bounded),</item>
/// <item>dealer handover → the exact products and units to remove, once; never a vanilla fallback for a managed
/// appointment (strict mode: too little eligible stock hands over nothing and the appointment keeps waiting),</item>
/// <item>handover processed / contract ended → settlement observed once, reservation released.</item>
/// </list>
/// It never pays, moves items, awards XP or completes contracts: the game does all of that. Main-thread only.
/// </summary>
public sealed class DealerSaleCoordinator
{
    /// <summary>After this many invalidations an appointment is reported as unstable (it keeps replanning).</summary>
    public const int MaxInvalidations = 5;

    /// <summary>At most this many waiting appointments are retried per dealer reconcile.</summary>
    public const int MaxReservationsPerReconcile = 16;

    private readonly IPolicyProvider _policies;
    private readonly Func<ProductCatalogSnapshot?> _catalog;
    private readonly IModLog? _log;
    private readonly Dictionary<string, DealerAppointment> _appointments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Signature, long LedgerVersion, long Version)> _lastReconcile = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DealerStock> _lastStock = new(StringComparer.Ordinal);
    private long _sequence;
    private long _savedVersion;

    public DealerSaleCoordinator(IPolicyProvider policies, Func<ProductCatalogSnapshot?> catalog, IModLog? log = null)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _log = log;
    }

    public ReservationLedger Ledger { get; } = new();

    /// <summary>Whether the live policy is trustworthy for new orders. Null means always.</summary>
    public Func<bool>? PolicyReady { get; set; }

    /// <summary>The game's unit prices, read at acceptance for the fair-price rule. Null disables substitution.</summary>
    public IProductPriceSource? Prices { get; set; }

    public string? SaveIdentity { get; private set; }

    public bool IsAuthoritative { get; private set; }

    public string AuthorityDetail { get; private set; } = "No session.";

    /// <summary>Bumped on every change that must be persisted.</summary>
    public long Version { get; private set; }

    public bool HasUnsavedChanges => Version != _savedVersion;

    /// <summary>Last problem worth showing in diagnostics (null when none since the save was bound).</summary>
    public string? LastError { get; private set; }

    public IReadOnlyList<DealerAppointment> Appointments => _appointments.Values.OrderBy(a => a.Sequence).ToList();

    public int ActiveAppointments => _appointments.Count;

    public int WaitingAppointments => _appointments.Values.Count(a => a.State == AppointmentState.WaitingForEligibleStock);

    public void SetAuthority(bool isServer, int playerCount)
    {
        var authoritative = isServer && playerCount >= 0 && playerCount <= 1;
        var detail = !isServer
            ? "Not the host: dealers use vanilla rules."
            : playerCount > 1
                ? $"{playerCount} players: dealers use vanilla rules (multiplayer is Phase 07)."
                : "Single player host.";
        if (authoritative != IsAuthoritative || detail != AuthorityDetail)
        {
            IsAuthoritative = authoritative;
            AuthorityDetail = detail;
            _log?.Info($"Dealer sales authority: {detail}");
        }
    }

    /// <summary>
    /// Switches to another save's appointments (or none). Reservations are never restored: they are rebuilt from
    /// the dealers' live inventory, so a reload cannot create phantom stock.
    /// </summary>
    public void BindSave(string? saveIdentity, IEnumerable<DealerOrderRecord>? records)
    {
        _appointments.Clear();
        Ledger.Clear();
        _lastReconcile.Clear();
        _lastStock.Clear();
        _sequence = 0;
        LastError = null;
        SaveIdentity = saveIdentity;
        foreach (var record in (records ?? Array.Empty<DealerOrderRecord>()).OrderBy(r => r.Sequence))
        {
            if (record is null || _appointments.ContainsKey(record.Order.ContractId) || !DealerStock.IsValidDealerId(record.DealerId))
            {
                continue;
            }

            var sequence = Math.Max(record.Sequence, _sequence + 1);
            _sequence = sequence;
            _appointments.Add(record.Order.ContractId, new DealerAppointment(record.Order, record.DealerId, sequence,
                record.Committed ? AppointmentState.CommitObserved : AppointmentState.WaitingForEligibleStock));
        }

        Version++;
        _savedVersion = Version;
    }

    public void MarkPersisted() => _savedVersion = Version;

    /// <summary>The records to persist (committed = the game already processed the handover).</summary>
    public IReadOnlyList<DealerOrderRecord> Records()
        => Appointments.Select(a => new DealerOrderRecord(a.Order, a.DealerId, a.Sequence, a.State == AppointmentState.CommitObserved)).ToList();

    /// <summary>Dealer contract accepted: snapshot it and try to reserve the dealer's eligible stock.</summary>
    public DealerOrderCreation OnContractAccepted(OrderRequest request, string? dealerId, DealerStock? stock)
    {
        if (!IsAuthoritative)
        {
            return new DealerOrderCreation(OrderOutcome.ModDisabled, null, null, AuthorityDetail);
        }

        if (SaveIdentity is null)
        {
            return new DealerOrderCreation(OrderOutcome.CatalogNotReady, null, null, "No active save: vanilla rules.");
        }

        if (PolicyReady is not null && !PolicyReady())
        {
            return new DealerOrderCreation(OrderOutcome.PolicyNotLoaded, null, null, "This save's Take It settings are not loaded or unreadable: vanilla rules.");
        }

        if (!DealerStock.IsValidDealerId(dealerId) || (stock is not null && stock.DealerId != dealerId))
        {
            return new DealerOrderCreation(OrderOutcome.InvalidContract, null, null, "Dealer could not be identified: vanilla rules.");
        }

        var creation = CategoryOrderFactory.CreateForDealer(request, _policies.Current, _catalog(), Prices, dealerId);
        if (!creation.IsManaged)
        {
            return new DealerOrderCreation(creation.Outcome, null, null, creation.Detail);
        }

        var order = creation.Order!;
        if (_appointments.TryGetValue(order.ContractId, out var existing))
        {
            return new DealerOrderCreation(OrderOutcome.Managed, existing, null, "Snapshot already taken; kept the original.");
        }

        var appointment = new DealerAppointment(order, dealerId!, ++_sequence, AppointmentState.WaitingForEligibleStock);
        _appointments.Add(order.ContractId, appointment);
        Version++;

        ReserveResult? reserve = null;
        if (stock is not null)
        {
            // Bring the dealer's existing holds in line with this fresh stock first, and let earlier waiting
            // appointments go first (acceptance order); then report this appointment's own result.
            Reconcile(stock, force: true);
            reserve = Ledger.TryGet(order.ContractId, out var held)
                ? new ReserveResult(ReserveOutcome.Reserved, held, stock.TotalUnits, string.Join(", ", held.Lines))
                : TryReserve(appointment, stock);
        }

        return new DealerOrderCreation(OrderOutcome.Managed, appointment, reserve,
            $"{order} for dealer {appointment.DealerId}: {appointment.State}{(reserve is null ? string.Empty : " (" + reserve.Detail + ")")}");
    }

    /// <summary>The managed appointment for a contract, or null when the game's own rules apply.</summary>
    public DealerAppointment? Find(string? contractId)
        => IsAuthoritative && contractId is not null && _appointments.TryGetValue(contractId, out var appointment) && !appointment.IsTerminal
            ? appointment
            : null;

    public bool HasAppointmentsFor(string? dealerId)
        => dealerId is not null && _appointments.Values.Any(a => a.DealerId == dealerId && !a.IsTerminal && a.State != AppointmentState.CommitObserved);

    /// <summary>Validates delivered items for a managed dealer contract (the customer's view); null for vanilla contracts.</summary>
    public DeliveryValidation? Validate(string? contractId, IReadOnlyList<DeliveredItem?> items)
    {
        var appointment = Find(contractId);
        if (appointment is null)
        {
            return null;
        }

        var catalog = _catalog();
        var live = _policies.Current?.ForScope(SalesScope.Dealer(appointment.DealerId));
        return DeliveryValidator.Validate(appointment.Order, items, catalog,
            product => RuleExplainer.ExplainNotAllowed(appointment.Order, live, catalog, product));
    }

    /// <summary>
    /// Inventory changed (or a periodic check): drops reservations whose stock is gone and gives stock to waiting
    /// appointments in acceptance order. Cheap when nothing changed since the last call for this dealer.
    /// </summary>
    public ReconcileReport Reconcile(DealerStock stock, bool force = false)
    {
        var none = Array.Empty<string>();
        if (stock is null || !IsAuthoritative)
        {
            return new ReconcileReport(true, none, none, 0);
        }

        Remember(stock);
        var key = (stock.Signature, Ledger.Version, Version);
        if (!force && _lastReconcile.TryGetValue(stock.DealerId, out var last) && last == key)
        {
            return new ReconcileReport(true, none, none, CountWaiting(stock.DealerId));
        }

        var invalidated = Ledger.Reconcile(stock);
        foreach (var id in invalidated)
        {
            if (_appointments.TryGetValue(id, out var appointment) && appointment.TryMove(AppointmentState.WaitingForEligibleStock))
            {
                appointment.Invalidations++;
                appointment.LastIssue = "Reserved stock disappeared; replanning.";
                if (appointment.Invalidations == MaxInvalidations + 1)
                {
                    // Strict mode: still managed (never a vanilla fallback); only reported once.
                    Fault(appointment, $"Order {id}: reserved stock was lost {appointment.Invalidations} times; still waiting for eligible stock.");
                }
            }
        }

        var reserved = new List<string>();
        var attempts = 0;
        var capped = false;
        foreach (var appointment in _appointments.Values
                     .Where(a => a.DealerId == stock.DealerId && a.State == AppointmentState.WaitingForEligibleStock)
                     .OrderBy(a => a.Sequence)
                     .ToList())
        {
            if (attempts++ >= MaxReservationsPerReconcile)
            {
                capped = true; // the rest is retried on the next call, which must not be skipped
                break;
            }

            if (TryReserve(appointment, stock).IsHolding)
            {
                reserved.Add(appointment.ContractId);
            }
        }

        if (invalidated.Count > 0 || reserved.Count > 0)
        {
            Version++;
        }

        if (capped)
        {
            _lastReconcile.Remove(stock.DealerId);
        }
        else
        {
            _lastReconcile[stock.DealerId] = (stock.Signature, Ledger.Version, Version);
        }

        return new ReconcileReport(false, invalidated, reserved, CountWaiting(stock.DealerId));
    }

    /// <summary>
    /// The dealer is about to hand a contract over: decide what the game's removal must take. Reconciles with the
    /// fresh stock first, ensures a reservation and marks it Committing. Strict mode: a managed appointment never
    /// falls back to the game's exact-product removal. Without enough eligible stock the decision is
    /// <see cref="HandoverDecision.Withheld"/> (nothing removed, the appointment keeps waiting). A contract the game
    /// moved to another dealer follows that dealer (its old hold is released).
    /// </summary>
    public HandoverPlan PrepareHandover(string? contractId, DealerStock stock)
    {
        var lines = Array.Empty<ReservationLine>();
        var appointment = Find(contractId);
        if (appointment is null || stock is null)
        {
            return new HandoverPlan(HandoverDecision.Vanilla, null, lines, "Not a managed dealer appointment.");
        }

        if (appointment.State is AppointmentState.Committing or AppointmentState.CommitObserved)
        {
            Fault(appointment, $"Order {appointment.ContractId}: handover requested again while {appointment.State}; nothing removed.");
            return new HandoverPlan(HandoverDecision.NothingToGive, appointment, lines, "Units already handed over.");
        }

        if (appointment.DealerId != stock.DealerId)
        {
            Rehome(appointment, stock.DealerId);
        }

        Reconcile(stock, force: true);
        if (appointment.State == AppointmentState.WaitingForEligibleStock)
        {
            var reserve = TryReserve(appointment, stock);
            if (!reserve.IsHolding)
            {
                appointment.WithheldHandovers++;
                Version++;
                var detail = $"not enough eligible stock at handover ({reserve.Detail}); nothing handed over, still waiting";
                appointment.LastIssue = detail;
                _log?.Warning($"Dealer order {appointment.ContractId}: {detail}.");
                return new HandoverPlan(HandoverDecision.Withheld, appointment, lines, detail);
            }
        }

        if (!Ledger.TryGet(appointment.ContractId, out var reservation) || !Ledger.BeginCommit(appointment.ContractId) ||
            !appointment.TryMove(AppointmentState.Committing))
        {
            Fault(appointment, $"Order {appointment.ContractId}: reservation state was inconsistent at handover; nothing handed over.");
            return new HandoverPlan(HandoverDecision.Withheld, appointment, lines, appointment.LastIssue ?? string.Empty);
        }

        Version++;
        return new HandoverPlan(HandoverDecision.Fulfil, appointment, reservation.Lines, string.Join(", ", reservation.Lines));
    }

    /// <summary>
    /// The game's removal ran for a Fulfil plan. Exactly the planned units: the stock left the inventory, so the hold
    /// is released (<see cref="HandoverCompletion.Committed"/>). Any other amount: <see cref="HandoverCompletion.RolledBack"/>,
    /// the caller returns every removed item to the dealer and hands over nothing, and the appointment waits again.
    /// </summary>
    public HandoverCompletion CompleteHandover(string? contractId, long removedUnits)
    {
        if (contractId is null || !_appointments.TryGetValue(contractId, out var appointment) || appointment.State != AppointmentState.Committing)
        {
            return HandoverCompletion.Ignored;
        }

        if (removedUnits != appointment.Order.RequiredUnits)
        {
            Ledger.Release(contractId);
            appointment.TryMove(AppointmentState.WaitingForEligibleStock);
            appointment.WithheldHandovers++;
            Fault(appointment, $"Order {contractId}: the dealer's removal returned {removedUnits} of {appointment.Order.RequiredUnits} planned unit(s); " +
                               "items returned to the dealer, nothing handed over, still waiting.");
            Version++;
            return HandoverCompletion.RolledBack;
        }

        Ledger.ObserveCommit(contractId);
        appointment.RemovedUnits = removedUnits;
        Version++;
        return HandoverCompletion.Committed;
    }

    /// <summary>The removal could not start (error before any item moved): nothing is handed over; the appointment waits again.</summary>
    public void AbortHandover(string? contractId, string reason)
    {
        if (contractId is not null && _appointments.TryGetValue(contractId, out var appointment) && appointment.State == AppointmentState.Committing)
        {
            Ledger.Release(contractId);
            appointment.TryMove(AppointmentState.WaitingForEligibleStock);
            Fault(appointment, $"Order {contractId}: {reason}; nothing handed over, still waiting.");
            Version++;
        }
    }

    /// <summary>
    /// Strict guard for a dealer contract the mod does not manage (accepted before this save's settings were ready,
    /// for example): true when the mod is on and ready and the contract asks for a product the live settings do not
    /// permit for its category, so the dealer must hand over nothing instead of the excluded product. False when
    /// the mod does not apply (off, not the host, not ready): the contract is vanilla.
    /// </summary>
    public bool BlocksUnmanaged(OrderRequest? request, out string reason) => BlocksUnmanaged(request, null, out reason);

    /// <summary>
    /// The same guard for the dealer who holds the contract: that dealer's effective rules (overrides included).
    /// Never blocks while dealer sales are switched off (Phase 05): the contract is then vanilla.
    /// </summary>
    public bool BlocksUnmanaged(OrderRequest? request, string? dealerId, out string reason)
    {
        reason = string.Empty;
        var catalog = _catalog();
        var configured = _policies.Current;
        var scope = SalesScope.Dealer(dealerId);
        if (request is null || !IsAuthoritative || SaveIdentity is null || (PolicyReady is not null && !PolicyReady()) ||
            configured is null || !configured.IsPathEnabled(scope) || catalog is null || Find(request.ContractId) is not null)
        {
            return false;
        }

        var policy = configured.ForScope(scope);

        foreach (var line in request.Lines)
        {
            if (line is null || !ProductKey.TryCreate(line.ProductId, out var key) || !catalog.TryGetProduct(key, out var product))
            {
                reason = $"requested product '{line?.ProductId}' is unknown to the catalog";
                return true;
            }

            var decision = SaleEligibilityPolicy.Evaluate(policy, catalog, product.Category, key);
            if (!decision.IsEligible)
            {
                reason = $"requested product {key} is not permitted ({decision.Reason})";
                return true;
            }
        }

        return false;
    }

    /// <summary>The game processed the dealer's handover (payment, cut, XP): recorded once.</summary>
    public CommitObservation OnHandoverProcessed(string? contractId)
    {
        if (contractId is null || !_appointments.TryGetValue(contractId, out var appointment))
        {
            return CommitObservation.NotManaged;
        }

        appointment.CommitCount++;
        if (appointment.State == AppointmentState.CommitObserved)
        {
            _log?.Warning($"Dealer handover for order {contractId} reported again; ignored. Vanilla stays in charge of the transaction.");
            return CommitObservation.Duplicate;
        }

        if (!appointment.TryMove(AppointmentState.CommitObserved))
        {
            return CommitObservation.AlreadyEnded;
        }

        // A commit the mod did not plan (should not happen): release any hold so nothing stays promised.
        Ledger.ObserveCommit(contractId);
        Version++;
        return CommitObservation.First;
    }

    /// <summary>Contract ended: release any reservation and drop the appointment. Null when none was managed.</summary>
    public DealerAppointment? OnContractEnded(string? contractId, SaleEnd end)
    {
        if (contractId is null || !_appointments.TryGetValue(contractId, out var appointment))
        {
            return null;
        }

        Ledger.Release(contractId, end == SaleEnd.Expired ? ReservationState.Expired : ReservationState.Released);
        appointment.TryMove(end switch
        {
            SaleEnd.Completed => AppointmentState.Completed,
            SaleEnd.Failed => AppointmentState.Failed,
            SaleEnd.Expired => AppointmentState.Expired,
            _ => AppointmentState.Canceled,
        });
        _appointments.Remove(contractId);
        Version++;
        return appointment;
    }

    /// <summary>Drops appointments whose contracts no longer exist and releases their stock. Returns how many.</summary>
    public int Prune(IEnumerable<string?> liveContractIds)
    {
        var live = new HashSet<string>((liveContractIds ?? Array.Empty<string?>()).Where(id => id is not null)!, StringComparer.Ordinal);
        var stale = _appointments.Keys.Where(id => !live.Contains(id)).ToList();
        foreach (var id in stale)
        {
            Ledger.Release(id);
            _appointments.Remove(id);
        }

        if (stale.Count > 0)
        {
            Version++;
        }

        return stale.Count;
    }

    /// <summary>Every broken invariant between appointments and the ledger (empty when consistent).</summary>
    public IReadOnlyList<string> CheckInvariants()
    {
        var problems = Ledger.CheckInvariants(id => _lastStock.TryGetValue(id, out var s) ? s : null).ToList();
        foreach (var reservation in Ledger.Active)
        {
            if (!_appointments.TryGetValue(reservation.ContractId, out var appointment))
            {
                problems.Add($"{reservation.ContractId}: reservation without an appointment.");
            }
            else if (appointment.DealerId != reservation.DealerId)
            {
                problems.Add($"{reservation.ContractId}: reservation owner {reservation.DealerId} != dealer {appointment.DealerId}.");
            }
            else if (appointment.State is not (AppointmentState.Reserved or AppointmentState.Committing))
            {
                problems.Add($"{reservation.ContractId}: holding stock while {appointment.State}.");
            }
        }

        foreach (var appointment in _appointments.Values)
        {
            if (appointment.IsTerminal)
            {
                problems.Add($"{appointment.ContractId}: terminal appointment kept.");
            }

            if (appointment.State == AppointmentState.Reserved && !Ledger.TryGet(appointment.ContractId, out _))
            {
                problems.Add($"{appointment.ContractId}: Reserved without a reservation.");
            }
        }

        return problems;
    }

    private ReserveResult TryReserve(DealerAppointment appointment, DealerStock stock)
    {
        var result = Ledger.TryReserve(appointment.Order, stock, _catalog());
        if (result.IsHolding)
        {
            appointment.Shortfall = 0;
            if (appointment.TryMove(AppointmentState.Reserved))
            {
                appointment.LastIssue = null;
                Version++;
            }
        }
        else
        {
            appointment.Shortfall = Math.Max(0, appointment.Order.RequiredUnits - result.EligibleAvailable);
            appointment.LastIssue = $"Waiting for eligible stock: {result.Detail}";
        }

        return result;
    }

    private void Rehome(DealerAppointment appointment, string dealerId)
    {
        var from = appointment.DealerId;
        Ledger.Release(appointment.ContractId);
        if (appointment.State == AppointmentState.Reserved)
        {
            appointment.TryMove(AppointmentState.WaitingForEligibleStock);
        }

        appointment.DealerId = dealerId;
        appointment.LastIssue = $"Contract moved from dealer {from} to {dealerId}; replanning with the new dealer's stock.";
        Version++;
        _log?.Info($"Dealer order {appointment.ContractId}: {appointment.LastIssue}");
    }

    private void Fault(DealerAppointment appointment, string message)
    {
        appointment.LastIssue = message;
        LastError = message;
        _log?.Warning(message);
    }

    private void Remember(DealerStock stock) => _lastStock[stock.DealerId] = stock;

    private int CountWaiting(string dealerId)
        => _appointments.Values.Count(a => a.DealerId == dealerId && a.State == AppointmentState.WaitingForEligibleStock);
}
