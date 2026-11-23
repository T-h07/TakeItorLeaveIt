using System;
using System.Collections.Generic;
using System.Linq;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>Lifecycle of one managed order. Terminal states are final.</summary>
public enum SaleState
{
    /// <summary>Accepted; nothing handed over yet.</summary>
    Pending,

    /// <summary>The items currently in the handover slots pass validation.</summary>
    Validated,

    /// <summary>The items currently in the handover slots fail validation.</summary>
    Rejected,

    /// <summary>Vanilla processed the handover (items transferred, payment/XP in vanilla's hands).</summary>
    CommitObserved,

    Completed,
    Failed,
    Canceled,
    Expired,
}

/// <summary>How a contract ended, as observed on the game's contract.</summary>
public enum SaleEnd
{
    Completed,
    Failed,
    Canceled,
    Expired,
}

public enum CommitObservation
{
    /// <summary>No managed order with that id.</summary>
    NotManaged,

    /// <summary>First commit: recorded.</summary>
    First,

    /// <summary>Already committed: ignored (idempotent).</summary>
    Duplicate,

    /// <summary>The order already ended: ignored.</summary>
    AlreadyEnded,
}

/// <summary>Mutable state of one managed order. Only <see cref="ActiveOrderBook"/> changes it.</summary>
public sealed class SaleTransaction
{
    internal SaleTransaction(CategoryOrder order, SaleState state)
    {
        Order = order;
        State = state;
    }

    public CategoryOrder Order { get; }

    public SaleState State { get; private set; }

    public DeliveryRejection LastRejection { get; private set; }

    public int CommitCount { get; private set; }

    public bool IsTerminal => IsTerminalState(State);

    public static bool IsTerminalState(SaleState state)
        => state is SaleState.Completed or SaleState.Failed or SaleState.Canceled or SaleState.Expired;

    /// <summary>Allowed transitions. Anything else is refused and leaves the state unchanged.</summary>
    public static bool CanTransition(SaleState from, SaleState to)
    {
        if (IsTerminalState(from))
        {
            return false;
        }

        return to switch
        {
            SaleState.Pending => false,
            SaleState.Validated or SaleState.Rejected => from is SaleState.Pending or SaleState.Validated or SaleState.Rejected,
            SaleState.CommitObserved => from is SaleState.Pending or SaleState.Validated or SaleState.Rejected,
            _ => true, // any non-terminal state may end
        };
    }

    internal bool TryMove(SaleState to)
    {
        if (!CanTransition(State, to))
        {
            return false;
        }

        State = to;
        return true;
    }

    internal void NoteValidation(DeliveryValidation validation)
    {
        LastRejection = validation.Rejection;
    }

    internal void NoteCommit() => CommitCount++;

    public override string ToString() => $"{Order.ContractId} [{State}]";
}

/// <summary>
/// The managed orders of the active save, keyed by contract id. Holds each order's acceptance-time snapshot
/// and its transaction state; enforces exactly-once commit observation and cleanup on every end state.
/// Not thread-safe by design: the game calls it on the main thread only.
/// </summary>
public sealed class ActiveOrderBook
{
    private readonly Dictionary<string, SaleTransaction> _orders = new(StringComparer.Ordinal);

    public int Count => _orders.Count;

    /// <summary>Bumped on every change that must be persisted (add, commit, end, prune).</summary>
    public long Version { get; private set; }

    public IReadOnlyList<SaleTransaction> Orders => _orders.Values.OrderBy(t => t.Order.ContractId, StringComparer.Ordinal).ToList();

    /// <summary>Adds the snapshot unless one exists for the contract: the first snapshot always wins.</summary>
    public bool TryAdd(CategoryOrder order, SaleState state = SaleState.Pending)
    {
        if (order is null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        if (_orders.ContainsKey(order.ContractId) || SaleTransaction.IsTerminalState(state))
        {
            return false;
        }

        _orders.Add(order.ContractId, new SaleTransaction(order, state is SaleState.CommitObserved ? state : SaleState.Pending));
        Version++;
        return true;
    }

    public bool TryGet(string? contractId, out SaleTransaction transaction)
    {
        if (contractId is not null && _orders.TryGetValue(contractId, out var found))
        {
            transaction = found;
            return true;
        }

        transaction = null!;
        return false;
    }

    /// <summary>Records the latest pre-commit validation. Never changes a committed order.</summary>
    public SaleState RecordValidation(DeliveryValidation validation)
    {
        if (validation is null)
        {
            throw new ArgumentNullException(nameof(validation));
        }

        if (!_orders.TryGetValue(validation.Order.ContractId, out var transaction))
        {
            throw new InvalidOperationException($"Order {validation.Order.ContractId} is not active.");
        }

        if (transaction.TryMove(validation.IsValid ? SaleState.Validated : SaleState.Rejected))
        {
            transaction.NoteValidation(validation);
        }

        return transaction.State;
    }

    public CommitObservation ObserveCommit(string? contractId)
    {
        if (!TryGet(contractId, out var transaction))
        {
            return CommitObservation.NotManaged;
        }

        transaction.NoteCommit();
        if (transaction.State == SaleState.CommitObserved)
        {
            return CommitObservation.Duplicate;
        }

        if (!transaction.TryMove(SaleState.CommitObserved))
        {
            return CommitObservation.AlreadyEnded;
        }

        Version++;
        return CommitObservation.First;
    }

    /// <summary>Ends the order and drops its snapshot. Returns the transaction that ended, or null if none was active.</summary>
    public SaleTransaction? End(string? contractId, SaleEnd end)
    {
        if (!TryGet(contractId, out var transaction))
        {
            return null;
        }

        transaction.TryMove(end switch
        {
            SaleEnd.Completed => SaleState.Completed,
            SaleEnd.Failed => SaleState.Failed,
            SaleEnd.Expired => SaleState.Expired,
            _ => SaleState.Canceled,
        });
        _orders.Remove(transaction.Order.ContractId);
        Version++;
        return transaction;
    }

    /// <summary>Drops snapshots whose contracts no longer exist (e.g. after loading a save). Returns how many.</summary>
    public int Prune(IEnumerable<string?> liveContractIds)
    {
        var live = new HashSet<string>((liveContractIds ?? Array.Empty<string?>()).Where(id => id is not null)!, StringComparer.Ordinal);
        var stale = _orders.Keys.Where(id => !live.Contains(id)).ToList();
        foreach (var id in stale)
        {
            _orders.Remove(id);
        }

        if (stale.Count > 0)
        {
            Version++;
        }

        return stale.Count;
    }

    public void Clear()
    {
        if (_orders.Count > 0)
        {
            _orders.Clear();
            Version++;
        }
    }
}
