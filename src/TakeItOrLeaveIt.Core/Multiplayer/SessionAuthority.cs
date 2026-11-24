using System;
using System.Collections.Generic;

namespace TakeItOrLeaveIt.Core.Multiplayer;

/// <summary>Where this game instance stands in the network session (Phase 07, docs/MULTIPLAYER.md §2).</summary>
public enum SessionState
{
    /// <summary>No save is loaded (main menu, loading screen).</summary>
    NoSession,

    /// <summary>This game is the FishNet server and the only player.</summary>
    SinglePlayerHost,

    /// <summary>This game is the server with other players, but its rules are not ready yet (save or settings still loading).</summary>
    MultiplayerHostInitializing,

    /// <summary>This game is the server with other players and its rules are ready: host-side features are on.</summary>
    MultiplayerHostReady,

    /// <summary>This game joined another player's game and has not yet received valid host rules.</summary>
    MultiplayerClientSynchronizing,

    /// <summary>This game joined another player's game and shows the host's rules (read-only).</summary>
    MultiplayerClientReady,

    /// <summary>The session could not be identified safely (for example the network state could not be read): everything vanilla.</summary>
    UnsupportedSession,

    /// <summary>This game was a client and lost the connection: everything vanilla, cached host rules dropped.</summary>
    NetworkDisconnected,

    /// <summary>The installed game, the mod's hooks or the host's mod version are incompatible: everything vanilla.</summary>
    CompatibilityFailure,
}

/// <summary>What the client knows about the host's Take It rules (from <see cref="PolicySyncClient"/>).</summary>
public enum ClientSyncState
{
    /// <summary>Not a client, or not started.</summary>
    Idle,

    /// <summary>Connected; waiting for the host's published rules.</summary>
    Waiting,

    /// <summary>Valid host rules received.</summary>
    Ready,

    /// <summary>Nothing was published within the timeout: the host does not run Take It (or cannot publish).</summary>
    HostWithoutMod,

    /// <summary>The host runs an incompatible Take It protocol.</summary>
    ProtocolMismatch,

    /// <summary>The host published rules this client could not accept (damaged, too large, not from the host).</summary>
    Rejected,
}

/// <summary>
/// Facts the game adapter reads every couple of seconds (never per frame). Unknown values are null and are
/// treated conservatively (fail closed).
/// </summary>
public sealed class SessionFacts
{
    /// <summary>A save is loaded and its identity is known.</summary>
    public bool SaveLoaded { get; set; }

    /// <summary><c>InstanceFinder.IsServer</c>; null when it could not be read.</summary>
    public bool? IsServer { get; set; }

    /// <summary><c>InstanceFinder.IsClient</c>; null when it could not be read.</summary>
    public bool? IsClient { get; set; }

    /// <summary>Players in the session (<c>Player.PlayerList.Count</c>); negative when unknown.</summary>
    public int PlayerCount { get; set; } = -1;

    /// <summary>All gameplay hooks were applied (<c>PatchManager.Applied</c> with every module).</summary>
    public bool PatchesApplied { get; set; }

    /// <summary>The compatibility check allows gameplay (environment, loader, game build).</summary>
    public bool CompatibilityOk { get; set; } = true;

    /// <summary>Why compatibility failed (shown to the player).</summary>
    public string? CompatibilityProblem { get; set; }

    /// <summary>The host's own settings for this save were read (<c>ModMenuController.PolicyReadyForSales</c>).</summary>
    public bool PolicyReady { get; set; }

    /// <summary>The state decided on the previous evaluation (detects a lost connection).</summary>
    public SessionState Previous { get; set; } = SessionState.NoSession;

    /// <summary>Client only: what the client knows about the host's rules.</summary>
    public ClientSyncState ClientSync { get; set; } = ClientSyncState.Idle;

    /// <summary>Client only: detail of <see cref="ClientSync"/> (shown to the player).</summary>
    public string? ClientSyncDetail { get; set; }
}

/// <summary>
/// What the mod may do in the current session. Every managed feature is off unless the state explicitly allows it.
/// See docs/MULTIPLAYER.md §3 for the evidence behind each host-side capability.
/// </summary>
public sealed class SessionCapabilities
{
    public static readonly SessionCapabilities None = new(false, false, false, false, false, false, false, false);

    public SessionCapabilities(bool playerOrders, bool dealerAppointments, bool offerFilter, bool restockMessages,
        bool editPolicy, bool publishPolicy, bool writeSalesFiles, bool showHostPolicy)
    {
        PlayerOrders = playerOrders;
        DealerAppointments = dealerAppointments;
        OfferFilter = offerFilter;
        RestockMessages = restockMessages;
        EditPolicy = editPolicy;
        PublishPolicy = publishPolicy;
        WriteSalesFiles = writeSalesFiles;
        ShowHostPolicy = showHostPolicy;
    }

    /// <summary>
    /// Category orders for player contracts. They are judged where the handover happens; in multiplayer that is the
    /// host's own handovers only (a client's handover is judged by the game on the client, see MULTIPLAYER.md §3.2).
    /// </summary>
    public bool PlayerOrders { get; }

    /// <summary>Dealer category appointments, reservations and strict removal (dealers run on the server).</summary>
    public bool DealerAppointments { get; }

    /// <summary>Customer offer filter (offers are generated on the server).</summary>
    public bool OfferFilter { get; }

    /// <summary>Dealer out-of-stock texts (sent locally on the host).</summary>
    public bool RestockMessages { get; }

    /// <summary>The phone menu may edit and save this save's rules.</summary>
    public bool EditPolicy { get; }

    /// <summary>This game publishes its rules for clients.</summary>
    public bool PublishPolicy { get; }

    /// <summary>This game may write the save's orders, appointments and alert files.</summary>
    public bool WriteSalesFiles { get; }

    /// <summary>The phone menu shows the host's rules read-only.</summary>
    public bool ShowHostPolicy { get; }

    /// <summary>Any server-side managed feature is on (the coordinators are authoritative).</summary>
    public bool IsAuthoritative => PlayerOrders || DealerAppointments || OfferFilter;
}

/// <summary>Result of <see cref="SessionAuthority.Evaluate"/>. Immutable.</summary>
public sealed class SessionDecision
{
    internal SessionDecision(SessionState state, SessionCapabilities capabilities, string detail, string playerText)
    {
        State = state;
        Capabilities = capabilities;
        Detail = detail;
        PlayerText = playerText;
    }

    /// <summary>The fail-closed decision used before anything was read.</summary>
    public static SessionDecision Initial { get; } = new(SessionState.NoSession, SessionCapabilities.None, "No session.", "No save loaded.");

    public SessionState State { get; }

    public SessionCapabilities Capabilities { get; }

    /// <summary>Log/diagnostics detail.</summary>
    public string Detail { get; }

    /// <summary>Short, actionable text for the phone menu.</summary>
    public string PlayerText { get; }

    public bool IsHost => State is SessionState.SinglePlayerHost or SessionState.MultiplayerHostInitializing or SessionState.MultiplayerHostReady;

    public bool IsClient => State is SessionState.MultiplayerClientSynchronizing or SessionState.MultiplayerClientReady;

    public bool IsMultiplayer => State is SessionState.MultiplayerHostInitializing or SessionState.MultiplayerHostReady || IsClient;

    public override string ToString() => $"{State}: {Detail}";
}

/// <summary>
/// The Phase 07 authority model. Pure: the game adapter supplies <see cref="SessionFacts"/>; no game state is read here.
/// The host is the only authority. Clients never run managed features, never edit or write the host's rules, and
/// never write sales files. Anything unknown fails closed (vanilla).
/// </summary>
public static class SessionAuthority
{
    public static SessionDecision Evaluate(SessionFacts facts)
    {
        if (facts is null)
        {
            throw new ArgumentNullException(nameof(facts));
        }

        var wasClient = facts.Previous is SessionState.MultiplayerClientSynchronizing or SessionState.MultiplayerClientReady
            || (facts.Previous == SessionState.NetworkDisconnected);

        if (facts.IsServer is null || facts.IsClient is null)
        {
            return facts.SaveLoaded
                ? new SessionDecision(SessionState.UnsupportedSession, SessionCapabilities.None,
                    "The network state could not be read; every sale uses vanilla rules.",
                    "Take It is off: the game's network state could not be read.")
                : SessionDecision.Initial;
        }

        var isServer = facts.IsServer.Value;
        var isClient = facts.IsClient.Value;

        if (!isServer && !isClient)
        {
            if (wasClient)
            {
                return new SessionDecision(SessionState.NetworkDisconnected, SessionCapabilities.None,
                    "Disconnected from the host; host rules dropped, nothing is managed.",
                    "Disconnected from the host. Take It does nothing until you load or join a game.");
            }

            return SessionDecision.Initial;
        }

        if (!facts.SaveLoaded)
        {
            return isServer
                ? SessionDecision.Initial
                : new SessionDecision(SessionState.MultiplayerClientSynchronizing, SessionCapabilities.None,
                    "Joining: the host's game is still loading.", "Waiting for host rules.");
        }

        if (!facts.CompatibilityOk || !facts.PatchesApplied)
        {
            var problem = facts.CompatibilityProblem ?? (facts.PatchesApplied ? "incompatible environment" : "the game hooks could not be applied to this game build");
            // The host may still edit its own rules (they take effect once a compatible build is installed); a client never.
            var caps = isServer
                ? new SessionCapabilities(false, false, false, false, editPolicy: true, publishPolicy: false, writeSalesFiles: false, showHostPolicy: false)
                : SessionCapabilities.None;
            return new SessionDecision(SessionState.CompatibilityFailure, caps,
                $"Compatibility failure: {problem}. Every sale uses vanilla rules.",
                $"Take It is off: {problem}.");
        }

        if (isServer)
        {
            return EvaluateHost(facts);
        }

        return EvaluateClient(facts);
    }

    private static SessionDecision EvaluateHost(SessionFacts facts)
    {
        var players = facts.PlayerCount;
        if (players <= 1)
        {
            // Unknown count (<0) is reported but does not change what the host does: every host-side feature runs on the
            // server whatever the number of players (MULTIPLAYER.md §3). Only the label differs.
            var detail = players < 0 ? "Host (player count unknown)." : "Single player host.";
            return new SessionDecision(SessionState.SinglePlayerHost, HostCapabilities,
                facts.PolicyReady ? detail : detail + " This save's settings are not ready: new orders stay vanilla.",
                facts.PolicyReady ? "You are the host. Take It rules apply." : "You are the host. Waiting for this save's settings.");
        }

        if (!facts.PolicyReady)
        {
            // Still authoritative: orders already accepted keep their snapshots, and new ones stay vanilla through the
            // coordinators' own settings-ready gate. Clients are told to wait.
            return new SessionDecision(SessionState.MultiplayerHostInitializing, HostCapabilities,
                $"Multiplayer host ({players} players); this save's settings are not ready, so new orders stay vanilla.",
                "You are the host. Waiting for this save's settings; clients see \"Waiting for host rules\".");
        }

        return new SessionDecision(SessionState.MultiplayerHostReady, HostCapabilities,
            $"Multiplayer host ({players} players). Host-side rules apply: offers, dealer appointments and the host's own handovers. " +
            "A client's handover is judged by the game on the client (vanilla rules).",
            "You are the host. Take It rules apply to offers, dealers and your own handovers.");
    }

    /// <summary>
    /// Every host-side feature, whatever the readiness of the settings: new orders consult the coordinators' own
    /// settings-ready gate, and in-flight appointments must never fall back to the game's exact-product removal.
    /// </summary>
    private static readonly SessionCapabilities HostCapabilities = new(playerOrders: true, dealerAppointments: true, offerFilter: true,
        restockMessages: true, editPolicy: true, publishPolicy: true, writeSalesFiles: true, showHostPolicy: false);

    private static SessionDecision EvaluateClient(SessionFacts facts)
    {
        switch (facts.ClientSync)
        {
            case ClientSyncState.Ready:
                return new SessionDecision(SessionState.MultiplayerClientReady,
                    new SessionCapabilities(false, false, false, false, false, false, false, showHostPolicy: true),
                    "Client: host rules received (read-only). The host decides every managed sale.",
                    "Host controls Take It settings.");
            case ClientSyncState.ProtocolMismatch:
            case ClientSyncState.Rejected:
                return new SessionDecision(SessionState.CompatibilityFailure, SessionCapabilities.None,
                    "Client: " + (facts.ClientSyncDetail ?? "the host's rules could not be used") + ". Nothing is managed on this game.",
                    "Host rules unavailable: " + (facts.ClientSyncDetail ?? "incompatible host version") + ".");
            case ClientSyncState.HostWithoutMod:
                return new SessionDecision(SessionState.UnsupportedSession, SessionCapabilities.None,
                    "Client: the host published no Take It rules (host without the mod, or publishing unavailable). Vanilla rules for this game.",
                    "The host does not share Take It rules. Vanilla rules apply.");
            default:
                return new SessionDecision(SessionState.MultiplayerClientSynchronizing, SessionCapabilities.None,
                    "Client: waiting for the host's rules.", "Waiting for host rules.");
        }
    }

    /// <summary>Diagnostics lines for the phone (no paths, no ids).</summary>
    public static IReadOnlyList<string> Describe(SessionDecision decision)
    {
        var c = decision.Capabilities;
        return new[]
        {
            $"Session: {decision.State}",
            decision.Detail,
            $"Managed here: player orders {OnOff(c.PlayerOrders)}, dealers {OnOff(c.DealerAppointments)}, offers {OnOff(c.OfferFilter)}",
            $"Settings: {(c.EditPolicy ? "editable" : c.ShowHostPolicy ? "host's rules (read-only)" : "not editable")}",
        };
    }

    private static string OnOff(bool value) => value ? "on" : "off";
}
