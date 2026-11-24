using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 07 suite F: compatibility modes A–E and the read-only client menu (simulation only).</summary>
public class CompatibilityModeTests
{
    [Fact]
    public void P07_F01_mode_A_both_modded_client_shows_the_host_rules()
    {
        var s = new CoopSessions(21);
        var (client, _) = s.Join();

        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        Assert.NotNull(client.HostRules);
        Assert.Null(s.Host.MenuControlText);
    }

    [Fact]
    public void P07_F02_mode_B_host_only_keeps_host_side_features()
    {
        var host = new MultiplayerSession(Coop.Tokens(), "0.0.1-dev", "0.4.7f12");

        host.Update(Coop.HostReading(players: 3), channel: null, Coop.WeedOnly(Gdp), true, 0);

        Assert.Equal(SessionState.MultiplayerHostReady, host.Decision.State);
        Assert.True(host.Decision.Capabilities.IsAuthoritative);
        Assert.Null(host.Host.Session); // no lobby: nothing published, nothing required from clients
    }

    [Fact]
    public void P07_F03_mode_C_client_only_stays_vanilla_and_says_why()
    {
        var net = new SimNetwork(22);
        var channel = net.AddClient();
        var client = new MultiplayerSession(Coop.Tokens(), "0.0.1-dev", "0.4.7f12", null, clientTimeoutMs: 6_000);

        for (var t = 0; t <= 8_000; t += 2_000)
        {
            client.Update(Coop.ClientReading(), channel, null, false, t);
        }

        Assert.Equal(SessionState.UnsupportedSession, client.Decision.State);
        Assert.False(client.Decision.Capabilities.IsAuthoritative);
        Assert.Contains("does not share Take It rules", client.MenuControlText);
    }

    [Fact]
    public void P07_F04_mode_D_same_protocol_other_mod_version_is_shown_with_a_note()
    {
        var s = new CoopSessions(23);
        var (client, _) = s.Join(modVersion: "0.0.2-dev");

        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        Assert.Contains(client.Describe(), l => l.StartsWith("Different Take It versions", System.StringComparison.Ordinal));
    }

    [Fact]
    public void P07_F05_mode_D_other_protocol_is_a_compatibility_failure()
    {
        var net = new SimNetwork(24);
        var channel = net.AddClient();
        var client = new MultiplayerSession(Coop.Tokens(), "0.0.1-dev", "0.4.7f12");
        channel.Deliver(Coop.Payload(Coop.WeedOnly(Gdp)).Replace("\"p\":1", "\"p\":7"));

        client.Update(Coop.ClientReading(), channel, null, false, 0);

        Assert.Equal(SessionState.CompatibilityFailure, client.Decision.State);
        Assert.Contains("protocol 7", client.MenuControlText);
        Assert.Null(client.HostRules);
    }

    [Fact]
    public void P07_F06_mode_E_different_game_builds_are_flagged_unsupported()
    {
        var s = new CoopSessions(25);
        var (client, _) = s.Join(game: "0.4.8f1");

        s.PollUntil(() => client.Decision.State == SessionState.MultiplayerClientReady);

        Assert.Contains(client.Describe(), l => l.Contains("Not a supported combination"));
    }

    [Fact]
    public void P07_F07_hooks_failing_on_a_client_still_keeps_it_read_only()
    {
        var client = new MultiplayerSession(Coop.Tokens(), "0.0.1-dev", null);

        client.Update(Coop.ClientReading(patches: false), null, null, false, 0);

        Assert.Equal(SessionState.CompatibilityFailure, client.Decision.State);
        Assert.True(client.IsClientRole);
        Assert.NotNull(client.MenuControlText);
    }

    [Fact]
    public void P07_F08_menu_is_read_only_while_host_controlled_and_never_saves()
    {
        var h = new MenuHarness().Load("save-a");
        Assert.True(h.Menu.CanEdit);

        h.Menu.SetHostControlled("Host controls Take It settings.", Coop.WeedOnly(Gdp));

        Assert.False(h.Menu.CanEdit);
        Assert.True(h.Menu.ShowsHostRules);
        Assert.False(h.Menu.SetMode(Weed, PolicyMode.Disabled));
        Assert.False(h.Menu.Save());
        Assert.Contains("host controls", h.Menu.Feedback.Text);
        Assert.False(h.Store.Files.ContainsKey("save-a"));
        Assert.Equal(new[] { Gdp }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void P07_F09_host_rules_never_reach_the_local_profile_or_live_policy()
    {
        var h = new MenuHarness().Load("save-a");
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Weed, SourDiesel, true);
        Assert.True(h.Menu.Save());
        var saved = h.Store.Files["save-a"];

        h.Menu.SetHostControlled("Host controls Take It settings.", Coop.WeedOnly(Gdp));
        h.Menu.Discard();
        h.Menu.RequestReset();
        Assert.False(h.Menu.ConfirmReset());

        Assert.Equal(saved, h.Store.Files["save-a"]);
        Assert.Equal(new[] { SourDiesel }, h.Provider.Current.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.False(h.Menu.HasUnsavedChanges);
    }

    [Fact]
    public void P07_F10_leaving_client_mode_restores_the_local_draft()
    {
        var h = new MenuHarness().Load("save-a");
        h.Menu.SetMode(Weed, PolicyMode.Disabled); // unsaved local change

        h.Menu.SetHostControlled("Waiting for host rules.", null);
        Assert.False(h.Menu.ShowsHostRules);
        h.Menu.SetHostControlled(null, null);

        Assert.True(h.Menu.CanEdit);
        Assert.True(h.Menu.HasUnsavedChanges);
        Assert.Equal(PolicyMode.Disabled, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
    }

    [Fact]
    public void P07_F11_a_save_switch_while_host_controlled_keeps_showing_the_host_rules()
    {
        var h = new MenuHarness().Load("save-a");
        h.Menu.SetHostControlled("Host controls Take It settings.", Coop.WeedOnly(Gdp));

        h.Load("save-b");

        Assert.True(h.Menu.IsHostControlled);
        Assert.Equal(new[] { Gdp }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.False(h.Menu.CanEdit);
    }

    [Fact]
    public void P07_F12_the_published_rules_are_the_saved_ones_not_the_draft()
    {
        var h = new MenuHarness().Load("save-a");
        h.Menu.SetMode(Weed, PolicyMode.Disabled); // unsaved draft
        var host = new MultiplayerSession(Coop.Tokens(), "0.0.1-dev", null);
        var net = new SimNetwork(26);

        host.Update(Coop.HostReading(), net.HostChannel, h.Menu.Saved, h.Menu.PolicyReadyForSales, 0);

        var decoded = PolicySyncCodec.Decode(net.HostValue);
        Assert.Equal(PolicyMode.AnyUnlocked, decoded.Rules!.GetEffectivePolicy(Weed).Mode);
    }
}
