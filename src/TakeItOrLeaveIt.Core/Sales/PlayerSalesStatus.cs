using System.Collections.Generic;

namespace TakeItOrLeaveIt.Core.Sales;

public enum ComponentStatus
{
    Unavailable,
    Inactive,
    Active,
}

/// <summary>
/// What the player-sales feature can actually do right now. "Enabled" is reported only when every hook it needs
/// is applied and the session is supported; nothing is claimed from the policy engine alone.
/// </summary>
public sealed class PlayerSalesStatus
{
    public string Runtime { get; set; } = "unknown";

    /// <summary>Why hooks are missing (build without game references, failed patch, ...); null when applied.</summary>
    public string? HooksUnavailableReason { get; set; }

    public ComponentStatus OfferHook { get; set; }

    public ComponentStatus HandoverValidation { get; set; }

    public ComponentStatus TransactionIntegration { get; set; }

    public bool CatalogReady { get; set; }

    public bool PolicyLoaded { get; set; }

    public bool ModEnabledForSave { get; set; }

    public bool Authoritative { get; set; }

    public string AuthorityDetail { get; set; } = string.Empty;

    public int ActiveOrders { get; set; }

    public bool HooksActive => OfferHook == ComponentStatus.Active && HandoverValidation == ComponentStatus.Active &&
                               TransactionIntegration == ComponentStatus.Active;

    public bool IsEnabled => HooksActive && Authoritative && ModEnabledForSave && CatalogReady && PolicyLoaded;

    public string Headline
        => IsEnabled ? "Enabled"
            : !HooksActive ? "Unavailable"
            : !CatalogReady ? "Waiting for a save and product data"
            : !PolicyLoaded ? "Waiting for this save's settings (vanilla rules until they load)"
            : !ModEnabledForSave ? "Off for this save"
            : "Inactive (unsupported session)";

    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            $"Player Sales: {Headline}",
            $"Runtime: {Runtime}",
            $"Product catalog: {(CatalogReady ? "Ready" : "Not ready")}",
            $"Policy: {(PolicyLoaded ? "Loaded" : "Not loaded")}",
            $"Offer hook: {OfferHook}",
            $"Handover validation: {HandoverValidation}",
            $"Transaction integration: {TransactionIntegration}",
            $"Session: {AuthorityDetail}",
            $"Active category orders: {ActiveOrders}",
        };
        if (HooksUnavailableReason is not null)
        {
            lines.Add($"Hooks: {HooksUnavailableReason}");
        }

        return lines;
    }
}
