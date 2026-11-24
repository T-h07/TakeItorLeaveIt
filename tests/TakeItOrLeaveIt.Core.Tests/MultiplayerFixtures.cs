using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Fault switches of <see cref="SimNetwork"/>. Probabilities are 0..1; all randomness comes from the seed.</summary>
internal sealed class NetworkFaults
{
    public int MinLatencyMs { get; set; }

    public int MaxLatencyMs { get; set; }

    public double LossRate { get; set; }

    public double DuplicateRate { get; set; }

    /// <summary>Chance that an older value is delivered again after a newer one (out-of-order arrival).</summary>
    public double ReplayRate { get; set; }

    public bool FailPublish { get; set; }
}

/// <summary>
/// Deterministic simulation of the rules channel: one host value, per-client delivery queues with latency, loss,
/// duplicates and replays, forged values, and disconnects. SIMULATION ONLY: it models the transport contract the game
/// adapter promises (owner-only writes, latest value wins), not Steam itself. Nothing here is runtime evidence.
/// </summary>
internal sealed class SimNetwork
{
    private readonly Random _rng;
    private readonly List<SimClientChannel> _clients = new();
    private readonly List<string> _history = new();

    public SimNetwork(int seed, NetworkFaults? faults = null)
    {
        _rng = new Random(seed);
        Faults = faults ?? new NetworkFaults();
        HostChannel = new SimHostChannel(this);
    }

    public NetworkFaults Faults { get; }

    public long Now { get; private set; }

    public bool Open { get; set; } = true;

    public string LobbyId { get; set; } = "lobby-1";

    public string? HostValue { get; private set; }

    public SimHostChannel HostChannel { get; }

    public int Writes { get; private set; }

    public SimClientChannel AddClient()
    {
        var client = new SimClientChannel(this);
        _clients.Add(client);
        if (HostValue is not null)
        {
            client.Enqueue(HostValue, Now + Latency(), fromHost: true); // join in progress: the current value arrives
        }

        return client;
    }

    public void Advance(long ms) => Now += ms;

    internal bool Write(string? payload)
    {
        if (!Open || Faults.FailPublish)
        {
            return false;
        }

        HostValue = payload;
        Writes++;
        if (payload is not null)
        {
            _history.Add(payload);
        }

        foreach (var client in _clients.Where(c => c.Connected))
        {
            if (_rng.NextDouble() < Faults.LossRate)
            {
                continue;
            }

            client.Enqueue(payload, Now + Latency(), fromHost: true);
            if (_rng.NextDouble() < Faults.DuplicateRate)
            {
                client.Enqueue(payload, Now + Latency(), fromHost: true);
            }

            if (_history.Count > 1 && _rng.NextDouble() < Faults.ReplayRate)
            {
                client.Enqueue(_history[_rng.Next(_history.Count - 1)], Now + Latency() + Faults.MaxLatencyMs + 1, fromHost: true);
            }
        }

        return true;
    }

    private long Latency() => Faults.MaxLatencyMs <= Faults.MinLatencyMs ? Faults.MinLatencyMs : _rng.Next(Faults.MinLatencyMs, Faults.MaxLatencyMs + 1);
}

internal sealed class SimHostChannel : IRulesChannel
{
    private readonly SimNetwork _net;

    public SimHostChannel(SimNetwork net) => _net = net;

    public bool IsOpen => _net.Open;

    public bool IsOwner { get; set; } = true;

    public string? Identity => _net.Open ? _net.LobbyId : null;

    public bool Publish(string? payload) => IsOwner && _net.Write(payload);

    public string? Read(out bool fromHost)
    {
        fromHost = false; // the host never reads its own rules as host rules
        return _net.HostValue;
    }
}

internal sealed class SimClientChannel : IRulesChannel
{
    private readonly SimNetwork _net;
    private readonly List<(long At, long Order, string? Payload, bool FromHost)> _queue = new();
    private long _order;
    private string? _visible;
    private bool _visibleFromHost;

    public SimClientChannel(SimNetwork net) => _net = net;

    public bool Connected { get; set; } = true;

    public bool IsOpen => _net.Open && Connected;

    public bool IsOwner => false;

    public string? Identity => IsOpen ? _net.LobbyId : null;

    /// <summary>A client cannot write the host's value (the transport refuses non-owners).</summary>
    public bool Publish(string? payload) => false;

    public string? Read(out bool fromHost)
    {
        foreach (var item in _queue.Where(q => q.At <= _net.Now).OrderBy(q => q.At).ThenBy(q => q.Order).ToList())
        {
            _visible = item.Payload;
            _visibleFromHost = item.FromHost;
            _queue.Remove(item);
        }

        fromHost = _visibleFromHost;
        return _visible;
    }

    /// <summary>A value that did not come from the host (another client, a tampered relay).</summary>
    public void Forge(string payload, long delayMs = 0) => Enqueue(payload, _net.Now + delayMs, fromHost: false);

    /// <summary>Raw delivery, for out-of-order and replay tests.</summary>
    public void Deliver(string? payload, bool fromHost = true) => Enqueue(payload, _net.Now, fromHost);

    internal void Enqueue(string? payload, long at, bool fromHost) => _queue.Add((at, _order++, payload, fromHost));
}

/// <summary>Builders for the Phase 07 tests.</summary>
internal static class Coop
{
    private static int _token;

    public static string Token() => "tok" + System.Threading.Interlocked.Increment(ref _token).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);

    public static Func<string> Tokens(string prefix = "t")
    {
        var n = 0;
        return () => (prefix + (++n).ToString("x8", System.Globalization.CultureInfo.InvariantCulture)).ToLowerInvariant();
    }

    public static NetworkReading HostReading(int players = 2, string save = "save-a", bool patches = true)
        => new() { SaveLoaded = true, SaveIdentity = save, IsServer = true, IsClient = true, PlayerCount = players, PatchesApplied = patches };

    public static NetworkReading ClientReading(int players = 2, bool patches = true)
        => new() { SaveLoaded = true, IsServer = false, IsClient = true, PlayerCount = players, PatchesApplied = patches };

    public static NetworkReading Offline(bool saveLoaded = false)
        => new() { SaveLoaded = saveLoaded, IsServer = false, IsClient = false, PlayerCount = 0, PatchesApplied = true };

    public static SessionFacts Facts(bool? server, bool? client, int players = 1, bool save = true, bool patches = true, bool policy = true,
        SessionState previous = SessionState.NoSession, ClientSyncState sync = ClientSyncState.Idle)
        => new()
        {
            SaveLoaded = save,
            IsServer = server,
            IsClient = client,
            PlayerCount = players,
            PatchesApplied = patches,
            PolicyReady = policy,
            Previous = previous,
            ClientSync = sync,
        };

    public static SessionDecision Decide(bool? server, bool? client, int players = 1, bool policy = true, ClientSyncState sync = ClientSyncState.Idle)
        => SessionAuthority.Evaluate(Facts(server, client, players, policy: policy, sync: sync));

    public static SessionDecision MultiplayerHost(int players = 2) => Decide(true, true, players);

    public static SessionDecision ClientReady() => Decide(false, true, 2, sync: ClientSyncState.Ready);

    /// <summary>Weed limited to the given products (Selected Only).</summary>
    public static PolicyConfiguration WeedOnly(params ProductKey[] products)
        => PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, products));

    /// <summary>A rules payload built by a real host, for decoder tests.</summary>
    public static string Payload(PolicyConfiguration? rules, string session = "session01", long revision = 1, string? notReady = null, long epoch = 1)
    {
        var text = rules is null ? null : PolicySyncCodec.RulesText(rules);
        return PolicySyncCodec.Encode(new PolicySnapshot(PolicySyncProtocol.Version, "0.0.1-dev", "0.4.7f12", session, revision,
            text is null ? HostRulesState.NotReady : HostRulesState.Ready, notReady, text, epoch));
    }
}

/// <summary>A host game and any number of client games around one simulated channel, each with its own session model.</summary>
internal sealed class CoopSessions
{
    public CoopSessions(int seed, NetworkFaults? faults = null, long timeoutMs = 10_000)
    {
        Net = new SimNetwork(seed, faults);
        Host = new MultiplayerSession(Coop.Tokens("h"), "0.0.1-dev", "0.4.7f12", HostLog, timeoutMs);
        TimeoutMs = timeoutMs;
    }

    public SimNetwork Net { get; }

    public RecordingLog HostLog { get; } = new();

    public MultiplayerSession Host { get; }

    public long TimeoutMs { get; }

    public PolicyConfiguration HostRules { get; set; } = Coop.WeedOnly(Gdp);

    public bool HostPolicyReady { get; set; } = true;

    public string HostSave { get; set; } = "save-a";

    public List<(MultiplayerSession Session, SimClientChannel Channel)> Clients { get; } = new();

    public (MultiplayerSession Session, SimClientChannel Channel) Join(string modVersion = "0.0.1-dev", string game = "0.4.7f12")
    {
        var client = (new MultiplayerSession(Coop.Tokens("c"), modVersion, game, null, TimeoutMs), Net.AddClient());
        Clients.Add(client);
        return client;
    }

    /// <summary>One two-second poll on every game.</summary>
    public void Poll(long advanceMs = 2_000)
    {
        Net.Advance(advanceMs);
        Host.Update(Coop.HostReading(1 + Clients.Count(c => c.Channel.Connected), HostSave), Net.HostChannel, HostRules, HostPolicyReady, Net.Now);
        foreach (var (session, channel) in Clients)
        {
            session.Update(channel.Connected ? Coop.ClientReading(1 + Clients.Count) : Coop.Offline(), channel, null, false, Net.Now);
        }
    }

    public void PollUntil(Func<bool> done, int maxPolls = 60)
    {
        for (var i = 0; i < maxPolls && !done(); i++)
        {
            Poll();
        }
    }
}
