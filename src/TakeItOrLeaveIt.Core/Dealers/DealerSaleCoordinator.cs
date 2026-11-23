using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
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
    /// <summary>Not a managed appointment (or it just returned to vanilla): run the game's own removal.</summary>
    Vanilla,

    /// <summary>Remove exactly <see cref="HandoverPlan.Lines"/> with the game's removal method, nothing else.</summary>
    Fulfil,

    /// <summary>The appointment already handed its units over: remove nothing (no double removal).</summary>
    NothingToGive,
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
    internal ReconcileReport(bool skipped, IReadOnlyList<string> invalidated, IReadOnlyList<string> reserved, IReadOnlyList<string> returnedToVanilla, int stillWaiting)
    {
        Skipped = skipped;
        Invalidated = invalidated;
        Reserved = reserved;
        ReturnedToVanilla = returnedToVanilla;
        StillWaiting = stillWaiting;
    }

    /// <summary>Nothing changed since the last reconcile of this dealer (same stock, same ledger).</summary>
    public bool Skipped { get; }

    public IReadOnlyList<string> Invalidated { get; }

    public IReadOnlyList<string> Reserved { get; }

    public IReadOnlyList<string> ReturnedToVanilla { get; }

    public int StillWaiting { get; }

    public bool Changed => Invalidated.Count + Reserved.Count + ReturnedToVanilla.Count > 0;
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
/// <item>dealer handover → the exact products and units to remove, once,</item>
/// <item>handover processed / contract ended → settlement observed once, reservation released.</item>
/// </list>
/// It never pays, moves items, awards XP or completes contracts: the game does all of that. Main-thread only.
/// </summary>
public sealed class DealerSaleCoordinator
{
    /// <summary>After this many invalidations an appointment stops replanning and returns to vanilla.</summary>
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

        var creation = CategoryOrderFactory.CreateForDealer(request, _policies.Current, _catalog());
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
        return appointment is null ? null : DeliveryValidator.Validate(appointment.Order, items, _catalog());
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
            return new ReconcileReport(true, none, none, none, 0);
        }

        Remember(stock);
        var key = (stock.Signature, Ledger.Version, Version);
        if (!force && _lastReconcile.TryGetValue(stock.DealerId, out var last) && last == key)
        {
            return new ReconcileReport(true, none, none, none, CountWaiting(stock.DealerId));
        }

        var invalidated = Ledger.Reconcile(stock);
        var returned = new List<string>();
        foreach (var id in invalidated)
        {
            if (_appointments.TryGetValue(id, out var appointment) && appointment.TryMove(AppointmentState.WaitingForEligibleStock))
            {
                appointment.Invalidations++;
                appointment.LastIssue = "Reserved stock disappeared; replanning.";
                if (appointment.Invalidations > MaxInvalidations)
                {
                    ReturnToVanilla(appointment, $"stock was lost {appointment.Invalidations} times");
                    returned.Add(id);
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

        if (invalidated.Count > 0 || reserved.Count > 0 || returned.Count > 0)
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

        return new ReconcileReport(false, invalidated, reserved, returned, CountWaiting(stock.DealerId));
    }

    /// <summary>
    /// The dealer is about to hand a contract over: decide what the game's removal must take. Reconciles with the
    /// fresh stock first, ensures a reservation and marks it Committing. When no eligible stock can be reserved,
    /// the appointment returns to vanilla before anything is removed.
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
            ReturnToVanilla(appointment, $"contract moved from dealer {appointment.DealerId} to {stock.DealerId}");
            return new HandoverPlan(HandoverDecision.Vanilla, appointment, lines, appointment.LastIssue ?? string.Empty);
        }

        Reconcile(stock, force: true);
        if (appointment.IsTerminal)
        {
            return new HandoverPlan(HandoverDecision.Vanilla, appointment, lines, appointment.LastIssue ?? string.Empty);
        }

        if (appointment.State == AppointmentState.WaitingForEligibleStock)
        {
            var reserve = TryReserve(appointment, stock);
            if (!reserve.IsHolding)
            {
                ReturnToVanilla(appointment, $"not enough eligible stock at handover ({reserve.Detail})");
                return new HandoverPlan(HandoverDecision.Vanilla, appointment, lines, appointment.LastIssue ?? string.Empty);
            }
        }

        if (!Ledger.TryGet(appointment.ContractId, out var reservation) || !Ledger.BeginCommit(appointment.ContractId) ||
            !appointment.TryMove(AppointmentState.Committing))
        {
            ReturnToVanilla(appointment, "reservation state was inconsistent");
            return new HandoverPlan(HandoverDecision.Vanilla, appointment, lines, appointment.LastIssue ?? string.Empty);
        }

        Version++;
        return new HandoverPlan(HandoverDecision.Fulfil, appointment, reservation.Lines, string.Join(", ", reservation.Lines));
    }

    /// <summary>
    /// The game's removal ran for a Fulfil plan. The stock left the inventory, so the hold is released now.
    /// <paramref name="removedUnits"/> is what the removal actually returned (diagnostics and shortfall check).
    /// </summary>
    public bool CompleteHandover(string? contractId, long removedUnits)
    {
        if (contractId is null || !_appointments.TryGetValue(contractId, out var appointment) || appointment.State != AppointmentState.Committing)
        {
            return false;
        }

        Ledger.ObserveCommit(contractId);
        appointment.RemovedUnits = removedUnits;
        if (removedUnits != appointment.Order.RequiredUnits)
        {
            Fault(appointment, $"Order {contractId}: the dealer's removal returned {removedUnits} of {appointment.Order.RequiredUnits} planned unit(s).");
        }

        Version++;
        return true;
    }

    /// <summary>The removal could not start (error before any item moved): the game's own removal takes over.</summary>
    public void AbortHandover(string? contractId, string reason)
    {
        if (contractId is not null && _appointments.TryGetValue(contractId, out var appointment) && appointment.State == AppointmentState.Committing)
        {
            ReturnToVanilla(appointment, reason);
        }
    }

    /// <summary>Stops managing an appointment before anything was removed; the game's own rules handle the contract.</summary>
    public bool ReturnToVanilla(string? contractId, string reason)
    {
        if (contractId is null || !_appointments.TryGetValue(contractId, out var appointment) ||
            !DealerAppointment.CanTransition(appointment.State, AppointmentState.ReturnedToVanilla))
        {
            return false;
        }

        ReturnToVanilla(appointment, reason);
        return true;
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
            if (appointment.TryMove(AppointmentState.Reserved))
            {
                appointment.LastIssue = null;
                Version++;
            }
        }
        else
        {
            appointment.LastIssue = $"Waiting for eligible stock: {result.Detail}";
        }

        return result;
    }

    private void ReturnToVanilla(DealerAppointment appointment, string reason)
    {
        Ledger.Release(appointment.ContractId);
        appointment.LastIssue = $"Returned to vanilla rules: {reason}.";
        appointment.TryMove(AppointmentState.ReturnedToVanilla);
        _appointments.Remove(appointment.ContractId);
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
