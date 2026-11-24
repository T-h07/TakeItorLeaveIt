using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>Lifecycle of a managed dealer appointment. Terminal states are final.</summary>
public enum AppointmentState
{
    /// <summary>Accepted; the dealer does not have enough eligible, unreserved stock yet.</summary>
    WaitingForEligibleStock,

    /// <summary>Stock is reserved for this appointment.</summary>
    Reserved,

    /// <summary>The planned units were removed by the game and handed to the customer's evaluation.</summary>
    Committing,

    /// <summary>The game processed the handover (payment, cut, XP are vanilla's). Observed exactly once.</summary>
    CommitObserved,

    Completed,
    Failed,
    Canceled,
    Expired,
}

/// <summary>Mutable state of one managed dealer appointment. Only <see cref="DealerSaleCoordinator"/> changes it.</summary>
public sealed class DealerAppointment
{
    internal DealerAppointment(CategoryOrder order, string dealerId, long sequence, AppointmentState state)
    {
        Order = order;
        DealerId = dealerId;
        Sequence = sequence;
        State = state;
    }

    public CategoryOrder Order { get; }

    public string ContractId => Order.ContractId;

    /// <summary>The hired dealer that fulfils it (owner of the reserved stock). Changes only when the game moves the contract.</summary>
    public string DealerId { get; internal set; }

    /// <summary>Acceptance order; earlier appointments get stock first when several wait.</summary>
    public long Sequence { get; }

    public AppointmentState State { get; private set; }

    /// <summary>How often its reservation was invalidated because the stock disappeared.</summary>
    public int Invalidations { get; internal set; }

    public int CommitCount { get; internal set; }

    /// <summary>Units the game's removal returned for this appointment (diagnostics).</summary>
    public long RemovedUnits { get; internal set; }

    public string? LastIssue { get; internal set; }

    /// <summary>Handovers that found too little eligible stock and handed over nothing (strict mode).</summary>
    public int WithheldHandovers { get; internal set; }

    /// <summary>Units still missing at the last reservation attempt (0 when reserved).</summary>
    public long Shortfall { get; internal set; }

    public bool IsTerminal => IsTerminalState(State);

    public static bool IsTerminalState(AppointmentState state)
        => state is AppointmentState.Completed or AppointmentState.Failed or AppointmentState.Canceled
            or AppointmentState.Expired;

    /// <summary>Allowed transitions; anything else is refused and leaves the state unchanged.</summary>
    public static bool CanTransition(AppointmentState from, AppointmentState to)
    {
        if (IsTerminalState(from))
        {
            return false;
        }

        return to switch
        {
            // From Committing only when the removal was rolled back (nothing stays with the customer).
            AppointmentState.WaitingForEligibleStock => from is AppointmentState.Reserved or AppointmentState.Committing,
            AppointmentState.Reserved => from is AppointmentState.WaitingForEligibleStock,
            AppointmentState.Committing => from is AppointmentState.Reserved,
            AppointmentState.CommitObserved => from is AppointmentState.Committing or AppointmentState.Reserved or AppointmentState.WaitingForEligibleStock,
            _ => true, // any non-terminal state may end with the contract
        };
    }

    internal bool TryMove(AppointmentState to)
    {
        if (!CanTransition(State, to))
        {
            return false;
        }

        State = to;
        return true;
    }

    public override string ToString() => $"{ContractId}@{DealerId} [{State}]";
}
