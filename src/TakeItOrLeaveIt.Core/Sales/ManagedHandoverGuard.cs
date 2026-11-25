namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>What a delivery hook does for one contract.</summary>
public enum HandoverHookAction
{
    /// <summary>The mod knows nothing about this contract: the game's own rules run.</summary>
    Vanilla,

    /// <summary>The mod answers with its validated result.</summary>
    UseValidation,

    /// <summary>The mod refuses the delivery (no match, nothing handed over). The game's exact-product rule is not run.</summary>
    Refuse,
}

/// <summary>
/// Phase 08 hardening: the fail-closed rule for delivery hooks. A contract the mod holds a snapshot for (a category
/// order or a dealer appointment that has not ended) must never fall back to the game's exact-product judgement, which
/// knows nothing about the snapshot's permitted products, quality floor or mixing rules. When such a contract cannot be
/// validated right now (authority briefly lost, catalog missing, an exception in the hook) the delivery is refused; the
/// contract stays open and the next attempt is judged normally. Contracts without a snapshot keep the vanilla path.
/// </summary>
public static class ManagedHandoverGuard
{
    /// <param name="holdsSnapshot">The coordinator still holds a live snapshot for the contract (authority ignored).</param>
    /// <param name="validation">The mod's validation, or null when it could not be produced.</param>
    public static HandoverHookAction Decide(bool holdsSnapshot, DeliveryValidation? validation)
    {
        if (validation is not null)
        {
            return validation.IsValid ? HandoverHookAction.UseValidation : HandoverHookAction.Refuse;
        }

        return holdsSnapshot ? HandoverHookAction.Refuse : HandoverHookAction.Vanilla;
    }

    /// <summary>The hook threw: refuse for a held contract, vanilla for any other.</summary>
    public static HandoverHookAction OnError(bool holdsSnapshot)
        => holdsSnapshot ? HandoverHookAction.Refuse : HandoverHookAction.Vanilla;

    /// <summary>Text for the handover screen when a held contract cannot be checked.</summary>
    public const string CannotCheckMessage = "Take It cannot check this delivery right now. Try again in a moment.";
}
