using System.Linq;
using TakeItOrLeaveIt.Core.Multiplayer;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 07 suite E: the rules channel under latency, loss, duplicates, replays, forged values, disconnects, host save
/// switches and publish failures. Deterministic seeds. SIMULATION ONLY: this is not multiplayer verification.
/// </summary>
public class NetworkFaultTests
{
    private static bool Shows(MultiplayerSession client, params Products.ProductKey[] products)
        => client.HostRules is { } rules && rules.GetEffectivePolicy(Weed).SelectedProducts.OrderBy(p => p.Value).SequenceEqual(products.OrderBy(p => p.Value));

    [Fact]
    public void P07_E01_a_joining_client_reaches_ready_with_the_host_rules()
    {
        var s = new CoopSessions(1);
        var (client, _) = s.Join();

        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        Assert.Equal(SessionState.MultiplayerHostReady, s.Host.Decision.State);
        Assert.True(Shows(client, Gdp));
        Assert.Equal("Host controls Take It settings.", client.MenuControlText);
    }

    [Fact]
    public void P07_E02_high_latency_only_delays_the_handshake()
    {
        var s = new CoopSessions(2, new NetworkFaults { MinLatencyMs = 1_000, MaxLatencyMs = 7_000 }, timeoutMs: 60_000);
        var (client, _) = s.Join();

        s.Poll();
        Assert.Equal(SessionState.MultiplayerClientSynchronizing, client.Decision.State);
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        Assert.True(Shows(client, Gdp));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(99)]
    public void P07_E03_lost_updates_never_make_the_client_go_backwards_and_it_converges_on_the_next(int seed)
    {
        var s = new CoopSessions(seed, new NetworkFaults { LossRate = 0.6, MaxLatencyMs = 3_000 });
        var (client, _) = s.Join();
        long last = 0;
        for (var round = 0; round < 30; round++)
        {
            s.HostRules = round % 2 == 0 ? Coop.WeedOnly(Gdp) : Coop.WeedOnly(OgKush, SourDiesel);
            s.Poll();
            Assert.True(client.Client.Revision >= last);
            Assert.True(client.Client.Revision <= s.Host.Host.Revision);
            last = client.Client.Revision;
        }

        s.Net.Faults.LossRate = 0;
        s.HostRules = Coop.WeedOnly(SourDiesel);
        s.PollUntil(() => client.Client.Revision == s.Host.Host.Revision);
        Assert.True(Shows(client, SourDiesel));
    }

    [Fact]
    public void P07_E04_duplicates_change_nothing()
    {
        var s = new CoopSessions(4, new NetworkFaults { DuplicateRate = 1.0 });
        var (client, channel) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        var revision = client.Client.Revision;

        channel.Deliver(s.Net.HostValue);
        s.Poll();

        Assert.Equal(revision, client.Client.Revision);
        Assert.True(client.Client.Duplicates > 0);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void P07_E05_replayed_older_values_are_rejected_as_stale(int seed)
    {
        var s = new CoopSessions(seed, new NetworkFaults { ReplayRate = 1.0, MaxLatencyMs = 1_000 });
        var (client, channel) = s.Join();
        long highest = 0;
        string? first = null;
        for (var i = 0; i < 12; i++)
        {
            s.HostRules = i % 2 == 0 ? Coop.WeedOnly(Gdp) : Coop.WeedOnly(OgKush);
            s.Poll(500); // short polls so replays are observed between updates
            first ??= s.Net.HostValue;
            Assert.True(client.Client.Revision >= highest);
            highest = client.Client.Revision;
        }

        // The last delivery may have been an old value; once replays stop, the host's heartbeat brings the current one.
        s.Net.Faults.ReplayRate = 0;
        s.PollUntil(() => client.Client.Revision == s.Host.Host.Revision);
        var stale = client.Client.Stale;
        channel.Deliver(first);  // the oldest revision arrives last
        s.Poll();

        Assert.Equal(stale + 1, client.Client.Stale);
        Assert.Equal(s.Host.Host.Revision, client.Client.Revision);
        Assert.True(Shows(client, OgKush));
    }

    [Fact]
    public void P07_E06_forged_values_are_never_shown()
    {
        var s = new CoopSessions(7);
        var (client, channel) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        channel.Forge(Coop.Payload(Coop.WeedOnly(OgKush), session: "forgedsess", revision: 999));
        s.Poll();

        Assert.True(Shows(client, Gdp));
        Assert.True(client.Client.Rejected > 0);
    }

    [Fact]
    public void P07_E07_disconnect_drops_the_host_rules()
    {
        var s = new CoopSessions(8);
        var (client, channel) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        channel.Connected = false;
        s.Poll();

        Assert.Equal(SessionState.NetworkDisconnected, client.Decision.State);
        Assert.Null(client.HostRules);
        Assert.True(client.IsClientRole);
        Assert.Contains("Disconnected", client.MenuControlText);
    }

    [Fact]
    public void P07_E08_reconnect_synchronizes_again()
    {
        var s = new CoopSessions(9);
        var (client, channel) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        channel.Connected = false;
        s.Poll();

        channel.Connected = true;
        channel.Deliver(s.Net.HostValue); // the lobby's current value on rejoin
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        Assert.True(Shows(client, Gdp));
    }

    [Fact]
    public void P07_E09_host_save_switch_starts_a_new_session_and_old_values_are_stale()
    {
        var s = new CoopSessions(10);
        var (client, channel) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        var oldValue = s.Net.HostValue!;
        var oldSession = client.Client.Session;

        s.HostSave = "save-b";
        s.HostRules = Coop.WeedOnly(OgKush);
        s.PollUntil(() => client.Client.Session != oldSession && client.HostRules is not null);
        channel.Deliver(oldValue);
        s.Poll();

        Assert.True(Shows(client, OgKush));
        Assert.True(client.Client.Stale > 0);
    }

    [Fact]
    public void P07_E10_host_closing_the_lobby_never_leaves_old_rules_on_the_client()
    {
        var s = new CoopSessions(11, timeoutMs: 6_000);
        var (client, _) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        s.Net.Open = false;
        s.Poll();
        Assert.Null(client.HostRules);
        s.PollUntil(() => client.Decision.State == SessionState.UnsupportedSession);

        Assert.Equal(SessionState.UnsupportedSession, client.Decision.State);
        Assert.False(client.Decision.Capabilities.IsAuthoritative);
    }

    [Fact]
    public void P07_E11_publish_failures_are_counted_and_retried()
    {
        var s = new CoopSessions(12, new NetworkFaults { FailPublish = true });
        var (client, _) = s.Join();
        s.Poll();
        s.Poll();
        Assert.True(s.Host.PublishFailures > 0);
        Assert.Null(client.HostRules);

        s.Net.Faults.FailPublish = false;
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        Assert.True(Shows(client, Gdp));
    }

    [Fact]
    public void P07_E12_a_new_lobby_gets_the_current_rules_again()
    {
        var s = new CoopSessions(13);
        s.Poll();
        var writes = s.Net.Writes;

        s.Net.LobbyId = "lobby-2";
        s.Poll();

        Assert.True(s.Net.Writes > writes);
        Assert.Equal(1, s.Host.Host.Revision); // same rules: republished, not a new revision
    }

    [Fact]
    public void P07_E13_join_in_progress_gets_the_latest_revision()
    {
        var s = new CoopSessions(14);
        for (var i = 0; i < 5; i++)
        {
            s.HostRules = i % 2 == 0 ? Coop.WeedOnly(Gdp, OgKush) : Coop.WeedOnly(SourDiesel);
            s.Poll();
        }

        var (client, _) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        Assert.Equal(s.Host.Host.Revision, client.Client.Revision);
        Assert.True(Shows(client, Gdp, OgKush));
    }

    [Fact]
    public void P07_E14_host_settings_not_ready_then_ready()
    {
        var s = new CoopSessions(15) { HostPolicyReady = false };
        var (client, _) = s.Join();
        s.Poll();
        s.Poll();

        Assert.Equal(SessionState.MultiplayerHostInitializing, s.Host.Decision.State);
        Assert.Equal(SessionState.MultiplayerClientSynchronizing, client.Decision.State);
        Assert.Contains("not loaded", client.Client.Detail);

        s.HostPolicyReady = true;
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        Assert.True(Shows(client, Gdp));
    }

    [Fact]
    public void P07_E15_a_client_can_never_publish()
    {
        var s = new CoopSessions(16);
        var (client, channel) = s.Join();
        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);
        var hostValue = s.Net.HostValue;

        Assert.False(channel.Publish(Coop.Payload(Coop.WeedOnly(OgKush), revision: 50)));
        Assert.Equal(hostValue, s.Net.HostValue);
        Assert.Null(client.Host.Session); // the client's own publisher never started
    }

    [Fact]
    public void P07_E16_the_host_rewrites_its_value_on_a_heartbeat_only()
    {
        var s = new CoopSessions(17);
        s.Poll();
        var writes = s.Net.Writes;

        for (var t = 0; t < 14; t++)
        {
            s.Poll(); // 28 s
        }

        Assert.Equal(writes, s.Net.Writes);
        s.Poll(4_000);
        Assert.Equal(writes + 1, s.Net.Writes);
        Assert.Equal(1, s.Host.Host.Revision); // a heartbeat is not a new revision
    }
}
