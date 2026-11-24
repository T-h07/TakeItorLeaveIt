using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 07 stress: long seeded runs with every fault switched on, checking the mandatory invariants after every step.
/// SIMULATION ONLY. A failing seed is reproducible from its number.
/// </summary>
public class MultiplayerStressTests
{
    private static readonly PolicyConfiguration[] RuleSets =
    {
        Coop.WeedOnly(Gdp),
        Coop.WeedOnly(OgKush, SourDiesel),
        Coop.WeedOnly(Gdp, OgKush, SourDiesel),
        PolicyConfiguration.Default,
        PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.Disabled, Array.Empty<ProductKey>())),
    };

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 12).Select(i => new object[] { i * 7919 });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void P07_S01_rules_sync_invariants_hold_under_every_fault(int seed)
    {
        var rng = new Random(seed);
        var s = new CoopSessions(seed, new NetworkFaults
        {
            MinLatencyMs = 0, MaxLatencyMs = 4_000, LossRate = 0.25, DuplicateRate = 0.25, ReplayRate = 0.25,
        });
        var published = new Dictionary<(string Session, long Revision), string>();
        var lastSeen = new Dictionary<int, (string? Session, long Revision)>();
        s.Join();
        s.Join();

        for (var step = 0; step < 250; step++)
        {
            switch (rng.Next(10))
            {
                case 0:
                case 1:
                    s.HostRules = RuleSets[rng.Next(RuleSets.Length)];
                    break;
                case 2:
                    s.HostSave = rng.Next(2) == 0 ? "save-a" : "save-b";
                    break;
                case 3:
                    var c = s.Clients[rng.Next(s.Clients.Count)].Channel;
                    c.Connected = !c.Connected;
                    break;
                case 4:
                    s.Clients[rng.Next(s.Clients.Count)].Channel.Forge(Coop.Payload(RuleSets[rng.Next(RuleSets.Length)], "forgedsess", rng.Next(1, 1000)));
                    break;
                case 5:
                    s.HostPolicyReady = rng.Next(4) != 0;
                    break;
                case 6 when s.Clients.Count < 4:
                    s.Join();
                    break;
            }

            s.Poll(rng.Next(200, 4_000));
            Record(s, published);
            Check(s, published, lastSeen);
        }

        // Faults off: every connected client converges on the host's current rules.
        s.Net.Faults.LossRate = s.Net.Faults.DuplicateRate = s.Net.Faults.ReplayRate = 0;
        s.HostPolicyReady = true;
        foreach (var (_, channel) in s.Clients)
        {
            channel.Connected = true;
        }

        s.PollUntil(() => s.Clients.All(c => c.Session.Client.Session == s.Host.Host.Session && c.Session.Client.Revision == s.Host.Host.Revision), 80);
        Record(s, published);
        Check(s, published, lastSeen);
        Assert.All(s.Clients, c => Assert.Equal(s.Host.Host.Revision, c.Session.Client.Revision));
    }

    private static void Record(CoopSessions s, Dictionary<(string, long), string> published)
    {
        if (s.Net.HostValue is { } value && PolicySyncCodec.Decode(value) is { Snapshot: { } snap } decoded)
        {
            published[(snap.Session, snap.Revision)] = decoded.Rules is null ? "not-ready" : PolicySyncCodec.RulesText(decoded.Rules);
        }
    }

    private static void Check(CoopSessions s, Dictionary<(string, long), string> published, Dictionary<int, (string? Session, long Revision)> lastSeen)
    {
        Assert.True(s.Host.Decision.IsHost);
        for (var i = 0; i < s.Clients.Count; i++)
        {
            var (session, channel) = s.Clients[i];
            var d = session.Decision;

            // 1. A client never manages, edits, publishes or writes.
            Assert.False(d.Capabilities.IsAuthoritative || d.Capabilities.EditPolicy || d.Capabilities.PublishPolicy || d.Capabilities.WriteSalesFiles);
            Assert.Null(session.Host.Session);
            if (channel.Connected)
            {
                Assert.NotNull(session.MenuControlText); // in someone else's game the menu is always host-controlled
            }

            // 2. Within one host session the shown revision never goes back.
            var now = (session.Client.Session, session.Client.Revision);
            if (lastSeen.TryGetValue(i, out var before) && before.Session is not null && before.Session == now.Session)
            {
                Assert.True(now.Revision >= before.Revision, $"client {i} went from r{before.Revision} to r{now.Revision}");
            }

            lastSeen[i] = now;

            // 3. Shown rules are exactly what the host published for that session and revision; never forged ones.
            Assert.NotEqual("forgedsess", session.Client.Session);
            if (session.HostRules is { } rules)
            {
                Assert.True(published.TryGetValue((session.Client.Session!, session.Client.Revision), out var text), "shown rules were never published");
                Assert.Equal(text, PolicySyncCodec.RulesText(rules));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void P07_S02_sales_invariants_hold_with_host_and_client_handovers(int seed)
    {
        var rng = new Random(seed);
        var w = new CoopWorld(Coop.WeedOnly(Gdp, OgKush, SourDiesel));
        w.Host.Stock("gdp", Gdp, 100_000).Stock("og", OgKush, 100_000).Stock("sd", SourDiesel, 100_000);
        var products = new[] { Gdp, OgKush, SourDiesel };
        var contracts = new List<string>();
        var startUnits = w.Host.TotalUnits;
        var hostDelivered = 0;

        for (var step = 0; step < 400; step++)
        {
            var roll = rng.Next(10);
            if (roll < 3 || contracts.Count == 0)
            {
                var id = $"contract-{step}";
                contracts.Add(id);
                w.Accept(id, 40f, (products[rng.Next(3)], rng.Next(1, 5)));
                continue;
            }

            var target = contracts[rng.Next(contracts.Count)];
            var units = rng.Next(1, 5);
            switch (roll)
            {
                case 3:
                case 4:
                case 5:
                    var slot = new[] { "gdp", "og", "sd" }[rng.Next(3)];
                    if (w.Host.Submit(target, (slot, units)))
                    {
                        hostDelivered += units;
                    }

                    break;
                case 6:
                case 7:
                    w.ClientHandover(target, (products[rng.Next(3)], units));
                    break;
                case 8:
                    w.Host.Coordinator.OnHandoverProcessed(target); // a repeated or late callback
                    break;
                default:
                    w.Host.Policies.Update(_ => RuleSets[rng.Next(3)]);
                    break;
            }

            // Invariants after every step.
            var settled = w.Host.PaymentCount + w.ClientSettlements;
            Assert.Equal(w.Host.Completions + w.ClientSettlements, settled);               // one payment per completed contract
            Assert.True(settled <= contracts.Count);                                         // never more payments than contracts
            Assert.Equal(startUnits - hostDelivered, w.Host.TotalUnits);                   // inventory moves only on a host settlement
            Assert.Equal(w.Host.PaymentCount, w.Host.XpAwards);                            // no duplicate XP
            Assert.Equal(0, w.Client.Book.Count);                                            // the client manages nothing
            Assert.All(w.Host.Coordinator.Book.Orders, o => Assert.True(w.Host.IsActive(o.Order.ContractId))); // no snapshot outlives its contract
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void P07_S03_dealer_invariants_hold_with_repeated_and_interleaved_handovers(int seed)
    {
        var rng = new Random(seed);
        var h = new DealerHarness().Stock(Benji, OgKush, 60).Stock(Benji, Gdp, 60).Stock(Molly, SourDiesel, 60);
        h.Coordinator.ApplySession(Coop.MultiplayerHost(3));
        var products = new[] { OgKush, Gdp, SourDiesel };
        var contracts = new List<string>();
        var before = h.TotalUnits(Benji) + h.TotalUnits(Molly);
        var delivered = 0;

        for (var step = 0; step < 300; step++)
        {
            var roll = rng.Next(10);
            if (roll < 3 || contracts.Count == 0)
            {
                var id = Contract(step + 1);
                contracts.Add(id);
                h.Accept(id, rng.Next(2) == 0 ? Benji : Molly, 60f, (products[rng.Next(3)], rng.Next(1, 6)));
            }
            else if (roll < 7)
            {
                var result = h.Handover(contracts[rng.Next(contracts.Count)], customerAccepts: rng.Next(5) != 0);
                if (result.Settled)
                {
                    delivered += result.Items.Sum(i => i.Units);
                }
            }
            else if (roll < 8)
            {
                h.Tick(rng.Next(2) == 0 ? Benji : Molly);
            }
            else if (roll < 9)
            {
                h.Take(Benji, products[rng.Next(3)], rng.Next(0, 4)); // the player takes stock from the dealer
            }
            else
            {
                h.Coordinator.OnHandoverProcessed(contracts[rng.Next(contracts.Count)]); // repeated callback
            }

            Assert.Empty(h.Coordinator.CheckInvariants());
            Assert.Equal(h.Payments, h.Completions);
            Assert.Equal(h.Payments, h.XpAwards);

            // Holds may exceed stock between the player taking stock and the next reconcile (Phase 05 design); after a
            // reconcile they never do.
            foreach (var dealer in new[] { Benji, Molly })
            {
                var stock = h.StockOf(dealer);
                h.Coordinator.Reconcile(stock, force: true);
                foreach (var lot in stock.Lots)
                {
                    Assert.True(h.Coordinator.Ledger.HeldUnits(dealer, lot) <= stock.UnitsOf(lot), $"{dealer} holds more {lot} than it has");
                }
            }
        }

        Assert.True(h.TotalUnits(Benji) + h.TotalUnits(Molly) <= before - delivered);
    }
}
