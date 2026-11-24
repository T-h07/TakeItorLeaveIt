using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Core.Multiplayer;

/// <summary>
/// Constants of the host → client rules protocol (docs/MULTIPLAYER.md §4). The transport is a single value that only
/// the host can write and every client can read (the game's Steam lobby data, owner-only). Every payload is the
/// complete current state, so a lost or repeated update is harmless: the latest valid revision wins.
/// </summary>
public static class PolicySyncProtocol
{
    /// <summary>Bumped whenever a client of the previous version could misread a payload.</summary>
    public const int Version = 1;

    /// <summary>The lobby data key. Unique to this mod.</summary>
    public const string Key = "takeitorleaveit.rules";

    /// <summary>Upper bound for an encoded payload (UTF-8 bytes); below Steam's 8 KB lobby value limit.</summary>
    public const int MaxPayloadBytes = 7680;

    /// <summary>Upper bound for short text fields (versions, session token, reasons).</summary>
    public const int MaxFieldLength = 64;

    /// <summary>How long a client waits for a first payload before deciding the host does not publish rules.</summary>
    public const long DefaultTimeoutMs = 30_000;
}

/// <summary>Whether the host's rules are usable (the host's own settings were read).</summary>
public enum HostRulesState
{
    Ready,
    NotReady,
}

/// <summary>One decoded host payload. Immutable.</summary>
public sealed class PolicySnapshot
{
    public PolicySnapshot(int protocol, string modVersion, string? gameVersion, string session, long revision, HostRulesState state,
        string? reason, string? rulesJson, long epoch = 1)
    {
        Epoch = epoch;
        Protocol = protocol;
        ModVersion = modVersion;
        GameVersion = gameVersion;
        Session = session;
        Revision = revision;
        State = state;
        Reason = reason;
        RulesJson = rulesJson;
    }

    public int Protocol { get; }

    public string ModVersion { get; }

    public string? GameVersion { get; }

    /// <summary>Random token of the host's current save session; changes when the host loads another save.</summary>
    public string Session { get; }

    /// <summary>Strictly increasing within a session.</summary>
    public long Revision { get; }

    /// <summary>
    /// Increases with every session the host starts (each loaded save). Tokens are random and cannot be ordered, so a
    /// client uses the epoch to refuse a delayed value from an earlier session.
    /// </summary>
    public long Epoch { get; }

    public HostRulesState State { get; }

    /// <summary>Why the rules are not ready (NotReady only).</summary>
    public string? Reason { get; }

    /// <summary>The rules (compact <see cref="PolicySettingsJson"/> text without the save identity); null when not ready.</summary>
    public string? RulesJson { get; }
}

public enum SyncDecodeStatus
{
    Ok,
    Empty,
    TooLarge,
    Malformed,
    ProtocolMismatch,
    HashMismatch,
    InvalidRules,
}

public sealed class SyncDecodeResult
{
    internal SyncDecodeResult(SyncDecodeStatus status, PolicySnapshot? snapshot, PolicyConfiguration? rules, string detail, int hostProtocol = 0)
    {
        Status = status;
        Snapshot = snapshot;
        Rules = rules;
        Detail = detail;
        HostProtocol = hostProtocol;
    }

    public SyncDecodeStatus Status { get; }

    public PolicySnapshot? Snapshot { get; }

    /// <summary>The validated rules (Ok with a Ready snapshot).</summary>
    public PolicyConfiguration? Rules { get; }

    public string Detail { get; }

    /// <summary>The protocol the payload declared (ProtocolMismatch).</summary>
    public int HostProtocol { get; }
}

/// <summary>
/// Deterministic, bounded encoding of <see cref="PolicySnapshot"/>. Decoding never throws: anything unexpected is a
/// typed failure. The rules are validated with the same fail-closed reader the settings file uses.
/// </summary>
public static class PolicySyncCodec
{
    public static string Encode(PolicySnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var root = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["p"] = snapshot.Protocol,
            ["mod"] = Clip(snapshot.ModVersion),
            ["s"] = snapshot.Session,
            ["r"] = snapshot.Revision,
            ["e"] = snapshot.Epoch,
            ["state"] = snapshot.State == HostRulesState.Ready ? "ready" : "not-ready",
        };
        if (snapshot.GameVersion is { Length: > 0 } game)
        {
            root["game"] = Clip(game);
        }

        if (snapshot.State == HostRulesState.Ready && snapshot.RulesJson is { } rules)
        {
            root["rules"] = rules;
            root["h"] = Hash(rules);
        }
        else if (snapshot.Reason is { Length: > 0 } reason)
        {
            root["why"] = Clip(reason);
        }

        return Compact(MiniJson.Write(new Dictionary<string, object?>(root, StringComparer.Ordinal)));
    }

    /// <summary>The compact rules text the host publishes for <paramref name="configuration"/> (no save identity).</summary>
    public static string RulesText(PolicyConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var settings = configuration.ToSettings();
        settings.Save = null;
        return Compact(PolicySettingsJson.ToJson(settings));
    }

    public static SyncDecodeResult Decode(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SyncDecodeResult(SyncDecodeStatus.Empty, null, null, "nothing published");
        }

        if (Encoding.UTF8.GetByteCount(text) > PolicySyncProtocol.MaxPayloadBytes)
        {
            return new SyncDecodeResult(SyncDecodeStatus.TooLarge, null, null, "the host's rules are larger than the protocol allows");
        }

        Dictionary<string, object?> root;
        try
        {
            if (MiniJson.Parse(text!) is not Dictionary<string, object?> map)
            {
                return Malformed("not an object");
            }

            root = map;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            return Malformed("unreadable");
        }

        if (!TryInt(root, "p", out var protocol) || protocol < 1)
        {
            return Malformed("no protocol version");
        }

        if (protocol != PolicySyncProtocol.Version)
        {
            var mod = root.TryGetValue("mod", out var m) && m is string ms ? Clip(ms) : "?";
            return new SyncDecodeResult(SyncDecodeStatus.ProtocolMismatch, null, null,
                $"the host runs Take It {mod} (rules protocol {protocol}; this game understands {PolicySyncProtocol.Version})", (int)protocol);
        }

        if (!TryText(root, "mod", out var modVersion) || !TryText(root, "s", out var session) || !IsToken(session) ||
            !TryInt(root, "r", out var revision) || revision < 1 || !TryInt(root, "e", out var epoch) || epoch < 1 ||
            !TryText(root, "state", out var stateText))
        {
            return Malformed("missing or invalid header fields");
        }

        var game = root.TryGetValue("game", out var g) && g is string gs && gs.Length <= PolicySyncProtocol.MaxFieldLength ? gs : null;
        if (stateText == "not-ready")
        {
            var why = root.TryGetValue("why", out var w) && w is string ws ? Clip(ws) : "the host's settings are not ready";
            return new SyncDecodeResult(SyncDecodeStatus.Ok,
                new PolicySnapshot(protocol: (int)protocol, modVersion, game, session, revision, HostRulesState.NotReady, why, null, epoch), null, why);
        }

        if (stateText != "ready" || !root.TryGetValue("rules", out var rawRules) || rawRules is not string rules ||
            !root.TryGetValue("h", out var rawHash) || rawHash is not string hash)
        {
            return Malformed("missing rules");
        }

        if (!string.Equals(Hash(rules), hash, StringComparison.Ordinal))
        {
            return new SyncDecodeResult(SyncDecodeStatus.HashMismatch, null, null, "the rules do not match their checksum");
        }

        PolicySettings settings;
        try
        {
            settings = PolicySettingsJson.FromJson(rules);
        }
        catch (Exception ex) when (ex is FormatException or System.IO.InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return new SyncDecodeResult(SyncDecodeStatus.InvalidRules, null, null, "the host's rules could not be read");
        }

        if (settings.SchemaVersion > PolicyConfiguration.CurrentSchemaVersion)
        {
            return new SyncDecodeResult(SyncDecodeStatus.ProtocolMismatch, null, null,
                $"the host's rules use settings schema {settings.SchemaVersion}; update Take It on this game", (int)protocol);
        }

        var configuration = PolicyConfiguration.FromSettings(settings, out var issues);
        var detail = issues.Count == 0 ? "rules received" : $"rules received; {issues.Count} entry(ies) shown made safer";
        return new SyncDecodeResult(SyncDecodeStatus.Ok,
            new PolicySnapshot((int)protocol, modVersion, game, session, revision, HostRulesState.Ready, null, rules, epoch), configuration, detail);
    }

    /// <summary>First 16 hex digits of SHA-256 over the UTF-8 text (an integrity check, not a security measure).</summary>
    public static string Hash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>A session token: 8–32 lowercase hex digits or letters.</summary>
    public static bool IsToken(string? token)
        => token is { Length: >= 8 and <= 32 } && token.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z'));

    /// <summary>Removes the whitespace outside strings that <see cref="MiniJson.Write"/> adds for readability.</summary>
    public static string Compact(string json)
    {
        var sb = new StringBuilder(json.Length);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < json.Length)
                {
                    sb.Append(json[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
                sb.Append(c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static SyncDecodeResult Malformed(string why) => new(SyncDecodeStatus.Malformed, null, null, "the host's rules message is damaged (" + why + ")");

    private static string Clip(string text) => text.Length <= PolicySyncProtocol.MaxFieldLength ? text : text.Substring(0, PolicySyncProtocol.MaxFieldLength);

    private static bool TryText(Dictionary<string, object?> root, string key, out string value)
    {
        value = root.TryGetValue(key, out var raw) && raw is string s && s.Length > 0 && s.Length <= PolicySyncProtocol.MaxFieldLength ? s : string.Empty;
        return value.Length > 0;
    }

    private static bool TryInt(Dictionary<string, object?> root, string key, out long value)
    {
        value = 0;
        if (!root.TryGetValue(key, out var raw) || raw is not double d || d != Math.Floor(d) || d < 0 || d > 9_007_199_254_740_991d)
        {
            return false;
        }

        value = (long)d;
        return true;
    }
}

/// <summary>
/// Host side: turns the host's live rules into payloads. A new session token per bound save; the revision rises only
/// when the published content changes, so republishing the same rules is a no-op. Not thread-safe (main thread).
/// </summary>
public sealed class PolicySyncHost
{
    private readonly Func<string> _newToken;
    private readonly string _modVersion;
    private readonly string? _gameVersion;
    private string? _lastContent;

    public PolicySyncHost(Func<string> newToken, string modVersion, string? gameVersion)
    {
        _newToken = newToken ?? throw new ArgumentNullException(nameof(newToken));
        _modVersion = string.IsNullOrEmpty(modVersion) ? "?" : modVersion;
        _gameVersion = gameVersion;
    }

    /// <summary>Null outside a session.</summary>
    public string? Session { get; private set; }

    /// <summary>Sessions started by this host so far (the current one's epoch).</summary>
    public long Epoch { get; private set; }

    public long Revision { get; private set; }

    /// <summary>The payload currently published (null when nothing is).</summary>
    public string? Current { get; private set; }

    public int Published { get; private set; }

    /// <summary>Why the last payload could not be built (too large); null when fine.</summary>
    public string? LastProblem { get; private set; }

    /// <summary>Starts a new session (host loaded a save). Clients treat older sessions as stale.</summary>
    public void BeginSession()
    {
        var token = _newToken();
        if (!PolicySyncCodec.IsToken(token))
        {
            throw new InvalidOperationException("Session tokens must be 8-32 lowercase letters or digits.");
        }

        Session = token;
        Epoch++;
        Revision = 0;
        _lastContent = null;
        Current = null;
        LastProblem = null;
    }

    /// <summary>Ends the session (save unloaded, or no longer the host). The caller clears the published value.</summary>
    public void EndSession()
    {
        Session = null;
        Revision = 0;
        _lastContent = null;
        Current = null;
    }

    /// <summary>
    /// The payload to publish for the current rules, or null when nothing changed (or there is no session).
    /// <paramref name="rules"/> null means not ready, with <paramref name="notReadyReason"/>.
    /// </summary>
    public string? Update(PolicyConfiguration? rules, string? notReadyReason)
    {
        if (Session is null)
        {
            return null;
        }

        var rulesText = rules is null ? null : PolicySyncCodec.RulesText(rules);
        var content = rulesText is null ? "not-ready|" + (notReadyReason ?? string.Empty) : "ready|" + rulesText;
        if (string.Equals(content, _lastContent, StringComparison.Ordinal))
        {
            return null;
        }

        var snapshot = new PolicySnapshot(PolicySyncProtocol.Version, _modVersion, _gameVersion, Session, Revision + 1,
            rulesText is null ? HostRulesState.NotReady : HostRulesState.Ready,
            rulesText is null ? notReadyReason ?? "the host's settings are not ready" : null, rulesText, Epoch);
        var payload = PolicySyncCodec.Encode(snapshot);
        if (Encoding.UTF8.GetByteCount(payload) > PolicySyncProtocol.MaxPayloadBytes)
        {
            // Never publish a truncated rule set: clients are told the rules are too large to show.
            LastProblem = "the rules are too large to share with clients; clients see \"Waiting for host rules\"";
            snapshot = new PolicySnapshot(PolicySyncProtocol.Version, _modVersion, _gameVersion, Session, Revision + 1,
                HostRulesState.NotReady, "the host's rules are too large to share", null, Epoch);
            payload = PolicySyncCodec.Encode(snapshot);
        }
        else
        {
            LastProblem = null;
        }

        _lastContent = content;
        Revision++;
        Current = payload;
        Published++;
        return payload;
    }
}

/// <summary>Handshake progress of <see cref="PolicySyncClient"/> (docs/MULTIPLAYER.md §4.3).</summary>
public enum SyncHandshake
{
    Idle,
    Connected,
    ProtocolCheck,
    SnapshotRequested,
    Received,
    Validated,
    Ready,
}

/// <summary>What one observation did.</summary>
public enum SyncObservation
{
    Ignored,
    Waiting,
    Accepted,
    Duplicate,
    Stale,
    Rejected,
    NotFromHost,
    TimedOut,
}

/// <summary>
/// Client side: validates host payloads. Rejects anything not written by the host, damaged, oversized, from another
/// protocol, from an older session or with a revision not newer than the one shown. Never writes anything to disk
/// and never feeds the host's rules into this game's own settings or sales. Not thread-safe (main thread).
/// </summary>
public sealed class PolicySyncClient
{
    private const int MaxRetiredSessions = 16;
    private readonly long _timeoutMs;
    private readonly Queue<string> _retiredOrder = new();
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);
    private string? _lastRaw;
    private long _waitingSince;

    public PolicySyncClient(long timeoutMs = PolicySyncProtocol.DefaultTimeoutMs)
    {
        _timeoutMs = timeoutMs <= 0 ? PolicySyncProtocol.DefaultTimeoutMs : timeoutMs;
    }

    public SyncHandshake Handshake { get; private set; } = SyncHandshake.Idle;

    public ClientSyncState State { get; private set; } = ClientSyncState.Idle;

    public string Detail { get; private set; } = "not connected";

    /// <summary>The host's validated rules (read-only display); null unless <see cref="ClientSyncState.Ready"/>.</summary>
    public PolicyConfiguration? HostRules { get; private set; }

    public string? Session { get; private set; }

    public long Revision { get; private set; }

    /// <summary>Epoch of the accepted session (0 before any).</summary>
    public long Epoch { get; private set; }

    public string? HostModVersion { get; private set; }

    public string? HostGameVersion { get; private set; }

    public int Accepted { get; private set; }

    public int Duplicates { get; private set; }

    public int Stale { get; private set; }

    public int Rejected { get; private set; }

    /// <summary>The client joined a host: start waiting for its rules.</summary>
    public void Connect(long nowMs)
    {
        Reset();
        Handshake = SyncHandshake.SnapshotRequested;
        State = ClientSyncState.Waiting;
        Detail = "waiting for the host's rules";
        _waitingSince = nowMs;
    }

    /// <summary>The connection ended: drop everything received from the host.</summary>
    public void Disconnect()
    {
        Reset();
        Detail = "disconnected";
    }

    /// <summary>
    /// One read of the published value. <paramref name="fromHost"/> is the transport's sender check (the lobby owner
    /// wrote it); payloads that fail it are never used.
    /// </summary>
    public SyncObservation Observe(string? payload, bool fromHost, long nowMs)
    {
        if (Handshake == SyncHandshake.Idle)
        {
            return SyncObservation.Ignored;
        }

        if (string.IsNullOrEmpty(payload))
        {
            if (HostRules is not null || Session is not null)
            {
                // The host stopped publishing (it unloaded the save or left): its rules no longer apply.
                HostRules = null;
                Session = null;
                Revision = 0;
                _lastRaw = null;
                _waitingSince = nowMs;
                Handshake = SyncHandshake.SnapshotRequested;
            }

            if (State is ClientSyncState.ProtocolMismatch or ClientSyncState.Rejected)
            {
                return SyncObservation.Waiting;
            }

            if (nowMs - _waitingSince >= _timeoutMs)
            {
                var changed = State != ClientSyncState.HostWithoutMod;
                State = ClientSyncState.HostWithoutMod;
                Detail = "the host does not share Take It rules";
                return changed ? SyncObservation.TimedOut : SyncObservation.Waiting;
            }

            State = ClientSyncState.Waiting;
            Detail = "waiting for the host's rules";
            return SyncObservation.Waiting;
        }

        if (string.Equals(payload, _lastRaw, StringComparison.Ordinal))
        {
            Duplicates++;
            return SyncObservation.Duplicate;
        }

        if (!fromHost)
        {
            Rejected++;
            if (HostRules is null)
            {
                Detail = "ignored rules that were not published by the host";
            }

            return SyncObservation.NotFromHost;
        }

        Handshake = SyncHandshake.ProtocolCheck;
        var decoded = PolicySyncCodec.Decode(payload);
        if (decoded.Status == SyncDecodeStatus.ProtocolMismatch)
        {
            Rejected++;
            _lastRaw = payload;
            HostRules = null;
            State = ClientSyncState.ProtocolMismatch;
            Detail = decoded.Detail;
            return SyncObservation.Rejected;
        }

        if (decoded.Status != SyncDecodeStatus.Ok || decoded.Snapshot is null)
        {
            Rejected++;
            _lastRaw = payload;
            Handshake = HostRules is null ? SyncHandshake.SnapshotRequested : SyncHandshake.Ready;
            if (HostRules is null)
            {
                State = ClientSyncState.Rejected;
                Detail = decoded.Detail;
            }

            return SyncObservation.Rejected;
        }

        Handshake = SyncHandshake.Received;
        var snapshot = decoded.Snapshot;
        if (_retired.Contains(snapshot.Session))
        {
            Stale++;
            Handshake = HostRules is null ? SyncHandshake.SnapshotRequested : SyncHandshake.Ready;
            return SyncObservation.Stale;
        }

        if (Session is not null && string.Equals(Session, snapshot.Session, StringComparison.Ordinal) && snapshot.Revision <= Revision)
        {
            Stale++;
            Handshake = HostRules is null ? SyncHandshake.SnapshotRequested : SyncHandshake.Ready;
            return SyncObservation.Stale;
        }

        if (Session is null && snapshot.Epoch < Epoch)
        {
            // After the host cleared its value: a delayed value from an older session is still refused.
            Stale++;
            return SyncObservation.Stale;
        }

        if (Session is not null && !string.Equals(Session, snapshot.Session, StringComparison.Ordinal))
        {
            if (snapshot.Epoch <= Epoch)
            {
                // A delayed value from an earlier (or the same-numbered) host session: never replaces the current one.
                Stale++;
                Handshake = HostRules is null ? SyncHandshake.SnapshotRequested : SyncHandshake.Ready;
                return SyncObservation.Stale;
            }

            Retire(Session);
        }

        Handshake = SyncHandshake.Validated;
        _lastRaw = payload;
        Session = snapshot.Session;
        Revision = snapshot.Revision;
        Epoch = snapshot.Epoch;
        HostModVersion = snapshot.ModVersion;
        HostGameVersion = snapshot.GameVersion;
        Accepted++;
        if (snapshot.State == HostRulesState.NotReady)
        {
            HostRules = null;
            State = ClientSyncState.Waiting;
            Detail = "waiting for host rules: " + (snapshot.Reason ?? "not ready");
            Handshake = SyncHandshake.SnapshotRequested;
            _waitingSince = nowMs;
            return SyncObservation.Accepted;
        }

        HostRules = decoded.Rules;
        State = ClientSyncState.Ready;
        Detail = $"host rules r{snapshot.Revision} ({decoded.Detail})";
        Handshake = SyncHandshake.Ready;
        return SyncObservation.Accepted;
    }

    /// <summary>Diagnostics lines (no tokens, no paths).</summary>
    public IReadOnlyList<string> Describe(string? localModVersion, string? localGameVersion)
    {
        var lines = new List<string>
        {
            $"Host rules: {State} ({Detail})",
            $"Sync: {Handshake}, revision {Revision}, accepted {Accepted}, duplicates {Duplicates}, stale {Stale}, rejected {Rejected}",
        };
        if (HostModVersion is not null && localModVersion is not null && !string.Equals(HostModVersion, localModVersion, StringComparison.Ordinal))
        {
            lines.Add($"Different Take It versions: host {HostModVersion}, this game {localModVersion} (same rules protocol, so the host's rules are shown).");
        }

        if (HostGameVersion is not null && localGameVersion is not null && !string.Equals(HostGameVersion, localGameVersion, StringComparison.Ordinal))
        {
            lines.Add($"Different game versions: host {HostGameVersion}, this game {localGameVersion}. Not a supported combination.");
        }

        return lines;
    }

    private void Retire(string session)
    {
        if (_retired.Add(session))
        {
            _retiredOrder.Enqueue(session);
            while (_retiredOrder.Count > MaxRetiredSessions)
            {
                _retired.Remove(_retiredOrder.Dequeue());
            }
        }
    }

    private void Reset()
    {
        Handshake = SyncHandshake.Idle;
        State = ClientSyncState.Idle;
        HostRules = null;
        Session = null;
        Revision = 0;
        Epoch = 0;
        HostModVersion = null;
        HostGameVersion = null;
        _lastRaw = null;
        _retired.Clear();
        _retiredOrder.Clear();
    }
}
