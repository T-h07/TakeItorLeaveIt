using System;
using System.Linq;
using System.Text;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 07 suite B: host rules protocol, handshake and client validation (simulation only).</summary>
public class PolicySyncTests
{
    [Fact]
    public void P07_B01_round_trip_keeps_the_rules()
    {
        var rules = Coop.WeedOnly(Gdp, SourDiesel);
        var decoded = PolicySyncCodec.Decode(Coop.Payload(rules, revision: 7));

        Assert.Equal(SyncDecodeStatus.Ok, decoded.Status);
        Assert.Equal(7, decoded.Snapshot!.Revision);
        Assert.Equal(PolicySyncCodec.RulesText(rules), PolicySyncCodec.RulesText(decoded.Rules!));
        Assert.Equal(new[] { Gdp, SourDiesel }.OrderBy(k => k.Value), decoded.Rules!.GetEffectivePolicy(Weed).SelectedProducts.OrderBy(k => k.Value));
    }

    [Fact]
    public void P07_B02_encoding_is_deterministic_and_never_contains_the_save_identity()
    {
        var rules = Coop.WeedOnly(Gdp);
        var a = Coop.Payload(rules);
        var b = Coop.Payload(PolicyConfiguration.FromSettings(rules.ToSettings(), out _));

        Assert.Equal(a, b);
        Assert.DoesNotContain("\"save\"", a);
        Assert.DoesNotContain("\n", a);
    }

    [Fact]
    public void P07_B03_payload_stays_under_the_transport_limit_for_large_rule_sets()
    {
        var products = Enumerable.Range(0, 120).Select(i => ProductKey.Create("product_" + i.ToString("d3", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var host = new PolicySyncHost(Coop.Tokens(), "0.0.1-dev", null);
        host.BeginSession();

        var payload = host.Update(Coop.WeedOnly(products), null)!;

        Assert.True(Encoding.UTF8.GetByteCount(payload) <= PolicySyncProtocol.MaxPayloadBytes);
        Assert.Equal(SyncDecodeStatus.Ok, PolicySyncCodec.Decode(payload).Status);
    }

    [Fact]
    public void P07_B04_rules_too_large_are_never_truncated()
    {
        var products = Enumerable.Range(0, 900).Select(i => ProductKey.Create("very_long_product_identifier_" + i.ToString("d4", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var host = new PolicySyncHost(Coop.Tokens(), "0.0.1-dev", null);
        host.BeginSession();

        var payload = host.Update(Coop.WeedOnly(products), null)!;
        var decoded = PolicySyncCodec.Decode(payload);

        Assert.Equal(HostRulesState.NotReady, decoded.Snapshot!.State);
        Assert.Contains("too large", decoded.Snapshot.Reason);
        Assert.NotNull(host.LastProblem);
    }

    [Fact]
    public void P07_B05_oversized_input_is_rejected_before_parsing()
    {
        var decoded = PolicySyncCodec.Decode(new string('x', PolicySyncProtocol.MaxPayloadBytes + 1));

        Assert.Equal(SyncDecodeStatus.TooLarge, decoded.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"p\":1}")]
    [InlineData("{\"p\":1,\"mod\":\"x\",\"s\":\"UPPERCASE1\",\"r\":1,\"state\":\"ready\"}")]
    [InlineData("{\"p\":1,\"mod\":\"x\",\"s\":\"session01\",\"r\":0,\"state\":\"ready\"}")]
    [InlineData("{\"p\":1,\"mod\":\"x\",\"s\":\"session01\",\"r\":1.5,\"state\":\"ready\"}")]
    [InlineData("{\"p\":1,\"mod\":\"x\",\"s\":\"session01\",\"r\":1,\"state\":\"ready\"}")]
    [InlineData("{\"p\":1,\"mod\":\"x\",\"s\":\"session01\",\"r\":1,\"state\":\"maybe\",\"rules\":\"{}\",\"h\":\"0\"}")]
    public void P07_B06_damaged_payloads_are_typed_failures(string text)
    {
        var decoded = PolicySyncCodec.Decode(text);

        Assert.NotEqual(SyncDecodeStatus.Ok, decoded.Status);
        Assert.Null(decoded.Rules);
    }

    [Fact]
    public void P07_B07_checksum_detects_altered_rules()
    {
        var payload = Coop.Payload(Coop.WeedOnly(Gdp));
        var tampered = payload.Replace("granddaddypurple", "ogkush");

        Assert.Equal(SyncDecodeStatus.HashMismatch, PolicySyncCodec.Decode(tampered).Status);
    }

    [Fact]
    public void P07_B08_another_protocol_version_is_a_mismatch_not_a_guess()
    {
        var payload = Coop.Payload(Coop.WeedOnly(Gdp)).Replace("\"p\":1", "\"p\":2");
        var decoded = PolicySyncCodec.Decode(payload);

        Assert.Equal(SyncDecodeStatus.ProtocolMismatch, decoded.Status);
        Assert.Equal(2, decoded.HostProtocol);
        Assert.Contains("protocol 2", decoded.Detail);
    }

    [Fact]
    public void P07_B09_host_revision_rises_only_when_the_rules_change()
    {
        var host = new PolicySyncHost(Coop.Tokens(), "0.0.1-dev", null);
        host.BeginSession();

        Assert.NotNull(host.Update(Coop.WeedOnly(Gdp), null));
        Assert.Null(host.Update(Coop.WeedOnly(Gdp), null));
        Assert.NotNull(host.Update(Coop.WeedOnly(OgKush), null));
        Assert.Equal(2, host.Revision);
    }

    [Fact]
    public void P07_B10_a_new_host_session_starts_a_new_token()
    {
        var host = new PolicySyncHost(Coop.Tokens(), "0.0.1-dev", null);
        host.BeginSession();
        var first = host.Session;
        host.Update(Coop.WeedOnly(Gdp), null);

        host.BeginSession();

        Assert.NotEqual(first, host.Session);
        Assert.Equal(0, host.Revision);
        Assert.NotNull(host.Update(Coop.WeedOnly(Gdp), null)); // same rules, new session: published again
    }

    [Fact]
    public void P07_B11_client_handshake_reaches_ready()
    {
        var client = new PolicySyncClient(10_000);
        client.Connect(0);
        Assert.Equal(SyncHandshake.SnapshotRequested, client.Handshake);

        Assert.Equal(SyncObservation.Accepted, client.Observe(Coop.Payload(Coop.WeedOnly(Gdp)), true, 100));

        Assert.Equal(SyncHandshake.Ready, client.Handshake);
        Assert.Equal(ClientSyncState.Ready, client.State);
        Assert.NotNull(client.HostRules);
    }

    [Fact]
    public void P07_B12_duplicates_and_older_revisions_are_ignored()
    {
        var client = new PolicySyncClient();
        client.Connect(0);
        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp), revision: 3), true, 1);

        Assert.Equal(SyncObservation.Duplicate, client.Observe(Coop.Payload(Coop.WeedOnly(Gdp), revision: 3), true, 2));
        Assert.Equal(SyncObservation.Stale, client.Observe(Coop.Payload(Coop.WeedOnly(OgKush), revision: 2), true, 3));
        Assert.Equal(SyncObservation.Stale, client.Observe(Coop.Payload(Coop.WeedOnly(OgKush), revision: 3), true, 4));
        Assert.Equal(3, client.Revision);
        Assert.Equal(new[] { Gdp }, client.HostRules!.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(2, client.Stale);
        Assert.Equal(1, client.Duplicates);
    }

    [Fact]
    public void P07_B13_a_retired_session_is_never_accepted_again()
    {
        var client = new PolicySyncClient();
        client.Connect(0);
        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp), session: "oldsession", revision: 9), true, 1);
        client.Observe(Coop.Payload(Coop.WeedOnly(OgKush), session: "newsession", revision: 1, epoch: 2), true, 2);

        Assert.Equal(SyncObservation.Stale, client.Observe(Coop.Payload(Coop.WeedOnly(SourDiesel), session: "oldsession", revision: 10), true, 3));
        Assert.Equal("newsession", client.Session);
        Assert.Equal(new[] { OgKush }, client.HostRules!.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void P07_B14_values_not_written_by_the_host_are_never_used()
    {
        var client = new PolicySyncClient();
        client.Connect(0);

        Assert.Equal(SyncObservation.NotFromHost, client.Observe(Coop.Payload(Coop.WeedOnly(OgKush)), false, 1));
        Assert.Null(client.HostRules);
        Assert.Equal(1, client.Rejected);

        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp)), true, 2);
        Assert.Equal(SyncObservation.NotFromHost, client.Observe(Coop.Payload(Coop.WeedOnly(OgKush), revision: 99), false, 3));
        Assert.Equal(new[] { Gdp }, client.HostRules!.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void P07_B15_not_ready_host_means_waiting_with_the_reason()
    {
        var client = new PolicySyncClient();
        client.Connect(0);

        client.Observe(Coop.Payload(null, notReady: "the host's settings for this save are not loaded"), true, 1);

        Assert.Equal(ClientSyncState.Waiting, client.State);
        Assert.Contains("not loaded", client.Detail);
        Assert.Null(client.HostRules);
    }

    [Fact]
    public void P07_B16_no_payload_within_the_timeout_means_host_without_mod()
    {
        var client = new PolicySyncClient(5_000);
        client.Connect(0);

        Assert.Equal(SyncObservation.Waiting, client.Observe(null, false, 4_999));
        Assert.Equal(SyncObservation.TimedOut, client.Observe(null, false, 5_000));
        Assert.Equal(ClientSyncState.HostWithoutMod, client.State);
        Assert.Equal(SyncObservation.Accepted, client.Observe(Coop.Payload(Coop.WeedOnly(Gdp)), true, 9_000)); // late host still works
    }

    [Fact]
    public void P07_B17_cleared_value_drops_the_host_rules()
    {
        var client = new PolicySyncClient();
        client.Connect(0);
        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp)), true, 1);

        client.Observe(null, true, 2);

        Assert.Null(client.HostRules);
        Assert.Equal(ClientSyncState.Waiting, client.State);
    }

    [Fact]
    public void P07_B18_disconnect_forgets_everything()
    {
        var client = new PolicySyncClient();
        client.Connect(0);
        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp)), true, 1);

        client.Disconnect();

        Assert.Equal(SyncHandshake.Idle, client.Handshake);
        Assert.Null(client.HostRules);
        Assert.Null(client.Session);
        Assert.Equal(SyncObservation.Ignored, client.Observe(Coop.Payload(Coop.WeedOnly(Gdp), revision: 2), true, 2));
    }

    [Fact]
    public void P07_B19_a_damaged_update_keeps_the_last_valid_rules()
    {
        var client = new PolicySyncClient();
        client.Connect(0);
        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp)), true, 1);

        Assert.Equal(SyncObservation.Rejected, client.Observe("{not json", true, 2));

        Assert.Equal(ClientSyncState.Ready, client.State);
        Assert.Equal(new[] { Gdp }, client.HostRules!.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void P07_B20_newer_settings_schema_is_reported_as_an_update_needed()
    {
        var rules = PolicySyncCodec.RulesText(Coop.WeedOnly(Gdp)).Replace("\"schemaVersion\":2", "\"schemaVersion\":3");
        var payload = PolicySyncCodec.Encode(new PolicySnapshot(1, "9.9.9", null, "session01", 1, HostRulesState.Ready, null, rules));

        var decoded = PolicySyncCodec.Decode(payload);

        Assert.Equal(SyncDecodeStatus.ProtocolMismatch, decoded.Status);
        Assert.Contains("update Take It", decoded.Detail);
    }

    [Fact]
    public void P07_B21_tokens_must_be_short_lowercase_text()
    {
        Assert.True(PolicySyncCodec.IsToken("abcdef12"));
        Assert.False(PolicySyncCodec.IsToken("short"));
        Assert.False(PolicySyncCodec.IsToken("HasUpper123"));
        Assert.False(PolicySyncCodec.IsToken(new string('a', 33)));
        var host = new PolicySyncHost(() => "BAD", "x", null);
        Assert.Throws<InvalidOperationException>(() => host.BeginSession());
    }

    [Fact]
    public void P07_B22_a_delayed_value_from_an_earlier_host_session_never_replaces_the_current_one()
    {
        var client = new PolicySyncClient();
        client.Connect(0);
        client.Observe(Coop.Payload(Coop.WeedOnly(Gdp), session: "sessionaa", epoch: 3), true, 1);

        // Never seen before, so not retired, but from an earlier session of the same host (lower epoch).
        Assert.Equal(SyncObservation.Stale, client.Observe(Coop.Payload(Coop.WeedOnly(OgKush), session: "sessionbb", revision: 40, epoch: 2), true, 2));
        client.Observe(null, true, 3); // the host cleared its value
        Assert.Equal(SyncObservation.Stale, client.Observe(Coop.Payload(Coop.WeedOnly(OgKush), session: "sessioncc", epoch: 1), true, 4));
        Assert.Equal(SyncObservation.Accepted, client.Observe(Coop.Payload(Coop.WeedOnly(SourDiesel), session: "sessiondd", epoch: 4), true, 5));
        Assert.Equal(4, client.Epoch);
    }

    [Fact]
    public void P07_B23_each_host_session_has_a_higher_epoch()
    {
        var host = new PolicySyncHost(Coop.Tokens(), "0.0.1-dev", null);
        host.BeginSession();
        var first = PolicySyncCodec.Decode(host.Update(Coop.WeedOnly(Gdp), null)).Snapshot!.Epoch;
        host.EndSession();
        host.BeginSession();
        var second = PolicySyncCodec.Decode(host.Update(Coop.WeedOnly(Gdp), null)).Snapshot!.Epoch;

        Assert.Equal((1, 2), (first, second));
    }

    [Fact]
    public void P07_B24_payload_without_an_epoch_is_damaged()
    {
        var payload = Coop.Payload(Coop.WeedOnly(Gdp)).Replace("\"e\":1,", string.Empty);

        Assert.Equal(SyncDecodeStatus.Malformed, PolicySyncCodec.Decode(payload).Status);
    }
}
