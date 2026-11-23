using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>
/// What hired-dealer sales can do right now. "Active" is reported only when every hook it needs is applied,
/// the session is supported, and the save's settings are loaded with the mod on.
/// </summary>
public sealed class DealerSalesStatus
{
    /// <summary>Why the dealer hooks are missing; null when applied.</summary>
    public string? HooksUnavailableReason { get; set; }

    /// <summary>Acceptance snapshot and reservation (the shared contract-accepted hook).</summary>
    public ComponentStatus AppointmentHook { get; set; }

    /// <summary>Category fulfilment at the dealer's handover (inventory removal hook).</summary>
    public ComponentStatus FulfilmentHook { get; set; }

    /// <summary>Inventory reconciliation (dealer tick hook).</summary>
    public ComponentStatus ReconcileHook { get; set; }

    public bool CatalogReady { get; set; }

    public bool PolicyLoaded { get; set; }

    public bool ModEnabledForSave { get; set; }

    public bool Authoritative { get; set; }

    public string AuthorityDetail { get; set; } = string.Empty;

    public int ActiveAppointments { get; set; }

    public int WaitingAppointments { get; set; }

    public int Reservations { get; set; }

    public long ReservedUnits { get; set; }

    public string? LastError { get; set; }

    public bool HooksActive => AppointmentHook == ComponentStatus.Active && FulfilmentHook == ComponentStatus.Active &&
                               ReconcileHook == ComponentStatus.Active;

    public bool IsActive => HooksActive && Authoritative && ModEnabledForSave && CatalogReady && PolicyLoaded;

    /// <summary>Active, Unavailable (hooks missing), Disabled (mod off for this save) or a waiting/inactive reason.</summary>
    public string Headline
        => IsActive ? "Active"
            : !HooksActive ? "Unavailable"
            : !CatalogReady ? "Waiting for a save and product data"
            : !PolicyLoaded ? "Waiting for this save's settings (vanilla rules until they load)"
            : !ModEnabledForSave ? "Disabled"
            : "Inactive (unsupported session)";

    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            $"Dealer sales: {Headline}",
            $"Dealer appointment hook: {AppointmentHook}",
            $"Dealer fulfilment hook: {FulfilmentHook}",
            $"Dealer stock reconcile: {ReconcileHook}",
            $"Managed dealer appointments: {ActiveAppointments}",
            $"Waiting for eligible stock: {WaitingAppointments}",
            $"Stock reservations: {Reservations} ({ReservedUnits} unit(s))",
            $"Last dealer error: {LastError ?? "none"}",
        };
        if (HooksUnavailableReason is not null)
        {
            lines.Add($"Dealer hooks: {HooksUnavailableReason}");
        }

        return lines;
    }
}
