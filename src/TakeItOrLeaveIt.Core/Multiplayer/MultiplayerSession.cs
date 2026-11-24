using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Core.Multiplayer;

/// <summary>
/// The one value the host writes and clients read (the game's Steam lobby data in the game build; a simulated
/// channel in tests). Implementations must only report <c>fromHost</c> = true when the transport itself
/// guarantees the session owner wrote the value. Main thread only.
/// </summary>
public interface IRulesChannel
{
    /// <summary>A channel exists (the game is in a lobby).</summary>
    bool IsOpen { get; }

    /// <summary>This game owns the channel (only the owner can publish).</summary>
    bool IsOwner { get; }

    /// <summary>Changes when the underlying lobby changes (so the host republishes into a new lobby).</summary>
    string? Identity { get; }

    /// <summary>Writes (or with null clears) the value. False when it could not be written.</summary>
    bool Publish(string? payload);

    /// <summary>The latest value, and whether the transport guarantees the session owner wrote it.</summary>
    string? Read(out bool fromHost);
}

/// <summary>Network facts the game adapter reads; the rest of <see cref="SessionFacts"/> is filled in here.</summary>
public sealed class NetworkReading
{
    public bool SaveLoaded { get; set; }

    /// <summary>The host's save identity (host only; used to start a new rules session per save).</summary>
    public string? SaveIdentity { get; set; }

    public bool? IsServer { get; set; }

    public bool? IsClient { get; set; }

    public int PlayerCount { get; set; } = -1;

    public bool PatchesApplied { get; set; }

    public bool CompatibilityOk { get; set; } = true;

    public string? CompatibilityProblem { get; set; }
}

/// <summary>
/// Phase 07 orchestration, polled every couple of seconds on the main thread: evaluates the session, keeps the host's
/// published rules current, validates what a client receives, and tells the caller how to configure the coordinators
/// and the menu. Pure Core logic: the game supplies <see cref="NetworkReading"/> and an <see cref="IRulesChannel"/>.
/// </summary>
public sealed class MultiplayerSession
{
    /// <summary>
    /// The host rewrites its current value this often. For lobby data an unchanged write is a no-op; for any transport
    /// that can deliver an older value last, it guarantees clients converge on the current revision.
    /// </summary>
    public const long HeartbeatMs = 30_000;

    private readonly IModLog? _log;
    private string? _hostSave;
    private long _lastWriteAt = long.MinValue;
    private bool _failing;
    private string? _publishedTo;
    private string? _localModVersion;
    private string? _localGameVersion;

    public MultiplayerSession(Func<string> newToken, string modVersion, string? gameVersion, IModLog? log = null,
        long clientTimeoutMs = PolicySyncProtocol.DefaultTimeoutMs)
    {
        _log = log;
        _localModVersion = modVersion;
        _localGameVersion = gameVersion;
        Host = new PolicySyncHost(newToken, modVersion, gameVersion);
        Client = new PolicySyncClient(clientTimeoutMs);
    }

    public SessionDecision Decision { get; private set; } = SessionDecision.Initial;

    public PolicySyncHost Host { get; }

    public PolicySyncClient Client { get; }

    /// <summary>
    /// This game is (or just was) a client in someone else's game: it must not write sales files or edit rules, even
    /// while the decision is a failure state.
    /// </summary>
    public bool IsClientRole { get; private set; }

    /// <summary>Publishing failures (channel refused the write).</summary>
    public int PublishFailures { get; private set; }

    /// <summary>The text the menu shows while it is host-controlled; null when this game edits its own rules.</summary>
    public string? MenuControlText { get; private set; }

    /// <summary>The host's validated rules for the read-only menu (client, Ready only).</summary>
    public PolicyConfiguration? HostRules => Decision.State == SessionState.MultiplayerClientReady ? Client.HostRules : null;

    /// <summary>
    /// One poll. <paramref name="liveRules"/> are the host's saved rules (the ones its coordinators use) and
    /// <paramref name="policyReady"/> whether they were read. Returns true when the decision changed.
    /// </summary>
    public bool Update(NetworkReading reading, IRulesChannel? channel, PolicyConfiguration? liveRules, bool policyReady, long nowMs)
    {
        if (reading is null)
        {
            throw new ArgumentNullException(nameof(reading));
        }

        var asClient = reading.IsServer == false && reading.IsClient == true;
        IsClientRole = asClient;
        if (asClient)
        {
            if (Client.Handshake == SyncHandshake.Idle)
            {
                Client.Connect(nowMs);
                _log?.Info("Joined another player's game: waiting for the host's Take It rules.");
            }

            var observation = channel is { IsOpen: true }
                ? Client.Observe(channel.Read(out var fromHost), fromHost, nowMs)
                : Client.Observe(null, false, nowMs);
            LogObservation(observation);
        }
        else if (Client.Handshake != SyncHandshake.Idle)
        {
            Client.Disconnect();
            _log?.Info("Left the host's game: host rules dropped.");
        }

        var facts = new SessionFacts
        {
            SaveLoaded = reading.SaveLoaded,
            IsServer = reading.IsServer,
            IsClient = reading.IsClient,
            PlayerCount = reading.PlayerCount,
            PatchesApplied = reading.PatchesApplied,
            CompatibilityOk = reading.CompatibilityOk,
            CompatibilityProblem = reading.CompatibilityProblem,
            PolicyReady = policyReady,
            Previous = Decision.State,
            ClientSync = asClient ? Client.State : ClientSyncState.Idle,
            ClientSyncDetail = asClient ? Client.Detail : null,
        };
        var next = SessionAuthority.Evaluate(facts);
        var changed = next.State != Decision.State || !string.Equals(next.Detail, Decision.Detail, StringComparison.Ordinal);
        Decision = next;
        if (changed)
        {
            _log?.Info($"Take It session: {next}");
        }

        IsClientRole |= next.State == SessionState.NetworkDisconnected;
        Publish(reading, channel, liveRules, policyReady, nowMs);
        MenuControlText = reading.IsServer == false && (reading.IsClient == true || next.State == SessionState.NetworkDisconnected)
            ? next.PlayerText
            : null;
        return changed;
    }

    /// <summary>Diagnostics lines for the phone (no ids, tokens or paths).</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string>(SessionAuthority.Describe(Decision));
        if (Decision.IsHost)
        {
            lines.Add(Host.Session is null
                ? "Rules sharing: not active (no lobby, or nothing to share yet)"
                : $"Rules sharing: revision {Host.Revision}, {Host.Published} update(s) published" + (PublishFailures > 0 ? $", {PublishFailures} failed" : string.Empty));
            if (Host.LastProblem is not null)
            {
                lines.Add("Rules sharing problem: " + Host.LastProblem);
            }
        }
        else if (Client.Handshake != SyncHandshake.Idle || Decision.IsClient)
        {
            lines.AddRange(Client.Describe(_localModVersion, _localGameVersion));
        }

        return lines;
    }

    private void Publish(NetworkReading reading, IRulesChannel? channel, PolicyConfiguration? liveRules, bool policyReady, long nowMs)
    {
        var canPublish = Decision.Capabilities.PublishPolicy && channel is { IsOpen: true, IsOwner: true } && reading.SaveIdentity is not null;
        if (!canPublish)
        {
            if (Host.Session is not null)
            {
                Host.EndSession();
                if (channel is { IsOpen: true, IsOwner: true } && !channel.Publish(null))
                {
                    PublishFailures++;
                }

                _publishedTo = null;
                _hostSave = null;
            }

            return;
        }

        if (Host.Session is null || !string.Equals(_hostSave, reading.SaveIdentity, StringComparison.Ordinal))
        {
            Host.BeginSession();
            _hostSave = reading.SaveIdentity;
            _publishedTo = null;
        }

        var payload = Host.Update(policyReady ? liveRules : null, policyReady ? null : "the host's settings for this save are not loaded");
        var lobbyChanged = !string.Equals(_publishedTo, channel!.Identity, StringComparison.Ordinal);
        var heartbeat = _lastWriteAt != long.MinValue && nowMs - _lastWriteAt >= HeartbeatMs;
        var toWrite = payload ?? (lobbyChanged || heartbeat ? Host.Current : null);
        if (toWrite is null)
        {
            return;
        }

        if (channel.Publish(toWrite))
        {
            _publishedTo = channel.Identity;
            _lastWriteAt = nowMs;
            _failing = false;
            if (payload is not null)
            {
                _log?.Info($"Take It rules shared with clients (revision {Host.Revision}).");
            }
        }
        else
        {
            PublishFailures++;
            _publishedTo = null;
            if (!_failing)
            {
                _log?.Warning("Take It rules could not be shared with clients; they are retried on every check (clients see \"Waiting for host rules\").");
            }

            _failing = true;
        }
    }

    private void LogObservation(SyncObservation observation)
    {
        switch (observation)
        {
            case SyncObservation.Accepted:
                _log?.Info($"Host rules: {Client.Detail}.");
                break;
            case SyncObservation.Rejected:
            case SyncObservation.NotFromHost:
                _log?.Warning($"Host rules message ignored: {Client.Detail}.");
                break;
            case SyncObservation.TimedOut:
                _log?.Info("No Take It rules from the host: this game keeps vanilla rules (host without the mod, or sharing unavailable).");
                break;
        }
    }
}
