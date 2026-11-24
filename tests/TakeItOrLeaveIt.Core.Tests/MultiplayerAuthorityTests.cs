using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 07 suite A: the session and authority model (simulation only; see docs/PHASE_07_TESTING.md for game checks).</summary>
public class MultiplayerAuthorityTests
{
    [Fact]
    public void P07_A01_single_player_host_keeps_every_feature()
    {
        var d = Coop.Decide(true, true, players: 1);

        Assert.Equal(SessionState.SinglePlayerHost, d.State);
        var c = d.Capabilities;
        Assert.True(c.PlayerOrders && c.DealerAppointments && c.OfferFilter && c.RestockMessages && c.EditPolicy && c.WriteSalesFiles);
        Assert.False(c.ShowHostPolicy);
        Assert.True(d.IsHost);
        Assert.False(d.IsMultiplayer);
    }

    [Fact]
    public void P07_A02_multiplayer_host_runs_host_side_features_and_says_client_handovers_are_vanilla()
    {
        var d = Coop.MultiplayerHost(3);

        Assert.Equal(SessionState.MultiplayerHostReady, d.State);
        Assert.True(d.Capabilities.IsAuthoritative);
        Assert.True(d.Capabilities.PublishPolicy);
        Assert.Contains("client's handover is judged by the game on the client", d.Detail);
    }

    [Fact]
    public void P07_A03_multiplayer_host_with_unready_settings_is_initializing_but_keeps_in_flight_orders_managed()
    {
        var d = Coop.Decide(true, true, players: 2, policy: false);

        Assert.Equal(SessionState.MultiplayerHostInitializing, d.State);
        Assert.True(d.Capabilities.DealerAppointments); // in-flight appointments must never fall back to vanilla removal
        Assert.Contains("Waiting for host rules", d.PlayerText);
    }

    [Theory]
    [InlineData(ClientSyncState.Idle, SessionState.MultiplayerClientSynchronizing)]
    [InlineData(ClientSyncState.Waiting, SessionState.MultiplayerClientSynchronizing)]
    [InlineData(ClientSyncState.Ready, SessionState.MultiplayerClientReady)]
    [InlineData(ClientSyncState.HostWithoutMod, SessionState.UnsupportedSession)]
    [InlineData(ClientSyncState.ProtocolMismatch, SessionState.CompatibilityFailure)]
    [InlineData(ClientSyncState.Rejected, SessionState.CompatibilityFailure)]
    public void P07_A04_a_client_never_gets_a_managed_capability(ClientSyncState sync, SessionState expected)
    {
        var d = Coop.Decide(false, true, players: 2, sync: sync);

        Assert.Equal(expected, d.State);
        var c = d.Capabilities;
        Assert.False(c.IsAuthoritative);
        Assert.False(c.PlayerOrders || c.DealerAppointments || c.OfferFilter || c.RestockMessages);
        Assert.False(c.EditPolicy || c.PublishPolicy || c.WriteSalesFiles);
        Assert.Equal(sync == ClientSyncState.Ready, c.ShowHostPolicy);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, null)]
    [InlineData(null, null)]
    public void P07_A05_unreadable_network_state_fails_closed(bool? server, bool? client)
    {
        var d = Coop.Decide(server, client, players: 1);

        Assert.Equal(SessionState.UnsupportedSession, d.State);
        Assert.Same(SessionCapabilities.None, d.Capabilities);
    }

    [Fact]
    public void P07_A06_losing_the_connection_as_client_is_network_disconnected()
    {
        var d = SessionAuthority.Evaluate(Coop.Facts(false, false, players: 0, previous: SessionState.MultiplayerClientReady));

        Assert.Equal(SessionState.NetworkDisconnected, d.State);
        Assert.False(d.Capabilities.IsAuthoritative);
        Assert.Contains("Disconnected", d.PlayerText);
    }

    [Fact]
    public void P07_A07_hooks_missing_is_a_compatibility_failure_everywhere()
    {
        var host = SessionAuthority.Evaluate(Coop.Facts(true, true, players: 2, patches: false));
        var client = SessionAuthority.Evaluate(Coop.Facts(false, true, players: 2, patches: false, sync: ClientSyncState.Ready));

        Assert.Equal(SessionState.CompatibilityFailure, host.State);
        Assert.False(host.Capabilities.IsAuthoritative);
        Assert.True(host.Capabilities.EditPolicy); // the host may still prepare its rules
        Assert.Equal(SessionState.CompatibilityFailure, client.State);
        Assert.False(client.Capabilities.EditPolicy);
    }

    [Fact]
    public void P07_A08_no_save_means_no_session_for_the_host()
    {
        var d = SessionAuthority.Evaluate(Coop.Facts(true, true, players: 1, save: false));

        Assert.Equal(SessionState.NoSession, d.State);
        Assert.False(d.Capabilities.IsAuthoritative);
    }

    [Fact]
    public void P07_A09_coordinators_follow_the_session_decision()
    {
        var player = new PlayerSaleCoordinator(new InMemoryPolicyProvider(PolicyConfiguration.Default), AllWeedUnlocked);
        var dealers = new DealerSaleCoordinator(new InMemoryPolicyProvider(PolicyConfiguration.Default), AllWeedUnlocked);

        player.ApplySession(Coop.MultiplayerHost());
        dealers.ApplySession(Coop.MultiplayerHost());
        Assert.True(player.IsAuthoritative && dealers.IsAuthoritative);

        player.ApplySession(Coop.ClientReady());
        dealers.ApplySession(Coop.ClientReady());
        Assert.False(player.IsAuthoritative || dealers.IsAuthoritative);
        Assert.Contains("Client", player.AuthorityDetail);
    }

    [Fact]
    public void P07_A10_a_client_creates_no_order_and_finds_none_even_if_it_had_one()
    {
        var h = new SettlementHarness();
        h.Accept("c1", 100f, (OgKush, 3));
        Assert.NotNull(h.Coordinator.Find("c1"));

        h.Coordinator.ApplySession(Coop.ClientReady());

        Assert.Null(h.Coordinator.Find("c1"));
        Assert.Null(h.Coordinator.Validate("c1", new DeliveredItem?[] { Sales.Item("s", Gdp, 3) }));
        Assert.Equal(OrderOutcome.ModDisabled, h.Coordinator.OnContractAccepted(Sales.Request("c2", (OgKush, 3))).Outcome);
    }

    [Fact]
    public void P07_A11_the_legacy_raw_gate_stays_single_player_only()
    {
        var h = new SettlementHarness();
        h.Coordinator.SetAuthority(isServer: true, playerCount: 2);

        Assert.False(h.Coordinator.IsAuthoritative);
        Assert.Contains("legacy single-player gate", h.Coordinator.AuthorityDetail);
    }

    [Fact]
    public void P07_A12_offer_filter_and_restock_texts_follow_the_coordinators()
    {
        var policies = new InMemoryPolicyProvider(Coop.WeedOnly(Gdp));
        var player = new PlayerSaleCoordinator(policies, AllWeedUnlocked);
        var filter = new OfferFilter(policies, AllWeedUnlocked) { Authoritative = () => player.IsAuthoritative };
        var offers = new System.Collections.Generic.List<string> { OgKush.Value, Gdp.Value };

        player.ApplySession(Coop.ClientReady());
        Assert.Equal(0, filter.Filter(offers.Count, i => offers[i], i => offers.RemoveAt(i)));

        player.ApplySession(Coop.MultiplayerHost());
        Assert.Equal(1, filter.Filter(offers.Count, i => offers[i], i => offers.RemoveAt(i)));
        Assert.Equal(new[] { Gdp.Value }, offers);
    }

    [Fact]
    public void P07_A13_describe_lists_state_and_capabilities_without_ids()
    {
        var lines = SessionAuthority.Describe(Coop.MultiplayerHost());

        Assert.Contains(lines, l => l.Contains("MultiplayerHostReady"));
        Assert.Contains(lines, l => l.Contains("player orders on"));
        Assert.DoesNotContain(lines, l => l.Contains("save-"));
    }

    [Fact]
    public void P07_A14_session_changes_are_logged_once()
    {
        var s = new CoopSessions(1);
        s.Poll();
        s.Poll();

        Assert.Single(s.HostLog.Infos, i => i.StartsWith("Take It session:", System.StringComparison.Ordinal));
        Assert.Equal(SessionState.SinglePlayerHost, s.Host.Decision.State);
    }
}
