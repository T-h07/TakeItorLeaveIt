using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Multiplayer;

/// <summary>
/// Phase 07 view of a managed transaction (docs/MULTIPLAYER.md §5). The states already exist in the coordinators; this
/// names them uniformly for diagnostics and the multiplayer invariants:
/// Available → Validating → Committing → Committed, or a terminal end. A managed transaction moves forward only;
/// a second commit attempt for the same contract is refused by the coordinator (no second removal, and payment, XP
/// and cut stay with the game, which pays once per handover).
/// </summary>
public enum TransactionPhase
{
    /// <summary>Accepted and waiting (player order pending, dealer appointment waiting or reserved).</summary>
    Available,

    /// <summary>A delivery was checked against the snapshot (player orders only: the handover screen passed).</summary>
    Validating,

    /// <summary>The dealer's removal is running for this appointment (between plan and completion).</summary>
    Committing,

    /// <summary>The game processed the handover; nothing more may be removed or recorded for it.</summary>
    Committed,

    /// <summary>The contract ended (completed, failed, canceled or expired).</summary>
    Ended,
}

public static class TransactionPhases
{
    public static TransactionPhase Of(AppointmentState state) => state switch
    {
        AppointmentState.WaitingForEligibleStock or AppointmentState.Reserved => TransactionPhase.Available,
        AppointmentState.Committing => TransactionPhase.Committing,
        AppointmentState.CommitObserved => TransactionPhase.Committed,
        _ => TransactionPhase.Ended,
    };

    public static TransactionPhase Of(SaleState state) => state switch
    {
        SaleState.Pending or SaleState.Rejected => TransactionPhase.Available,
        SaleState.Validated => TransactionPhase.Validating,
        SaleState.CommitObserved => TransactionPhase.Committed,
        _ => TransactionPhase.Ended,
    };
}
