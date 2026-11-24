using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// A host game (<see cref="SettlementHarness"/>, authoritative through the session model) and one client game whose
/// handovers are judged where the game judges them: on the client (docs/PHASE_07_NETWORK_ANALYSIS.md §2.2). The
/// client runs the mod with its own, more permissive local rules, which must never matter. SIMULATION ONLY.
/// </summary>
internal sealed class CoopWorld
{
    private readonly Dictionary<string, (ProductKey Product, int Quantity)[]> _lines = new();

    public CoopWorld(PolicyConfiguration hostRules, int players = 2)
    {
        Host = new SettlementHarness(hostRules);
        Host.Coordinator.ApplySession(Coop.MultiplayerHost(players));
        ClientPolicies = new InMemoryPolicyProvider(PolicyConfiguration.Default); // permissive: everything allowed
        Client = new PlayerSaleCoordinator(ClientPolicies, AllWeedUnlocked);
        Client.ApplySession(Coop.ClientReady());
        Client.BindSave("client-local", null);
    }

    public SettlementHarness Host { get; }

    public InMemoryPolicyProvider ClientPolicies { get; }

    public PlayerSaleCoordinator Client { get; }

    /// <summary>The game's settlement for client handovers (ProcessHandoverServerSide on the host): count of payments.</summary>
    public int ClientSettlements { get; private set; }

    public OrderCreation Accept(string contract, float payment, params (ProductKey Product, int Quantity)[] lines)
    {
        _lines[contract] = lines;
        return Host.Accept(contract, payment, lines);
    }

    /// <summary>
    /// The client presses Done. The client's GetProductListMatch runs the client's mod (not authoritative: no snapshot), so
    /// the game's exact-product rule judges; the host then settles once and the contract completes on the host.
    /// </summary>
    public bool ClientHandover(string contract, params (ProductKey Product, int Units)[] items)
    {
        if (!Host.IsActive(contract))
        {
            return false; // the contract already ended for everyone
        }

        var delivered = items.Select((i, n) => (DeliveredItem?)Item("c" + n, i.Product, i.Units)).ToList();
        if (Client.Validate(contract, delivered) is not null)
        {
            throw new Xunit.Sdk.XunitException("A client coordinator validated a handover: clients must never manage sales.");
        }

        var exact = _lines[contract].All(line => items.Where(i => i.Product == line.Product).Sum(i => i.Units) >= line.Quantity);
        if (!exact)
        {
            return false;
        }

        ClientSettlements++;
        Host.CompletedByClient(contract);
        return true;
    }
}

/// <summary>Phase 07 suite C: player sales in co-op (simulation only).</summary>
public class CoopPlayerSalesTests
{
    private static readonly PolicyConfiguration GdpOrOg = Coop.WeedOnly(Gdp, OgKush);

    [Fact]
    public void P07_C01_host_handover_uses_the_category_order_in_multiplayer()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Stock("gdp", Gdp, 5);

        Assert.True(w.Accept(ContractA, 100f, (OgKush, 3)).IsManaged);
        Assert.True(w.Host.Submit(ContractA, ("gdp", 3)));
        Assert.Equal((1, 1), (w.Host.PaymentCount, w.Host.XpAwards));
    }

    [Fact]
    public void P07_C02_host_handover_never_accepts_an_excluded_product()
    {
        var w = new CoopWorld(Coop.WeedOnly(Gdp));
        w.Host.Stock("og", OgKush, 5);
        w.Accept(ContractA, 100f, (Gdp, 3));

        var validation = w.Host.Coordinator.Validate(ContractA, new DeliveredItem?[] { Item("og", OgKush, 3) });

        Assert.False(validation!.IsValid);
        Assert.False(w.Host.Submit(ContractA, ("og", 3)));
        Assert.Equal(5, w.Host.UnitsOf("og"));
        Assert.Equal(0, w.Host.PaymentCount);
    }

    [Fact]
    public void P07_C03_client_handover_follows_the_game_exact_product_rule()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Accept(ContractA, 100f, (OgKush, 3));

        Assert.False(w.ClientHandover(ContractA, (Gdp, 3)));    // no category substitution on a client
        Assert.False(w.ClientHandover(ContractA, (SourDiesel, 3)));
        Assert.True(w.ClientHandover(ContractA, (OgKush, 3)));
        Assert.Equal(1, w.ClientSettlements);
    }

    [Fact]
    public void P07_C04_client_local_rules_never_create_or_change_orders()
    {
        var w = new CoopWorld(Coop.WeedOnly(Gdp));
        w.ClientPolicies.Update(_ => PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, new[] { SourDiesel })));

        var creation = w.Client.OnContractAccepted(Request(ContractA, (Gdp, 3)));

        Assert.Equal(OrderOutcome.ModDisabled, creation.Outcome);
        Assert.Equal(0, w.Client.Book.Count);
        Assert.False(w.Client.HasUnsavedOrders);
    }

    [Fact]
    public void P07_C05_client_completion_removes_the_host_snapshot_without_a_host_commit()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Accept(ContractA, 100f, (OgKush, 3));

        w.ClientHandover(ContractA, (OgKush, 3));

        Assert.Null(w.Host.Coordinator.Find(ContractA));
        Assert.Equal(0, w.Host.Coordinator.Book.Count);
        Assert.Equal(0, w.Host.PaymentCount); // the host's own handover path never ran
    }

    [Fact]
    public void P07_C06_after_a_client_completes_the_host_cannot_settle_again()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Stock("gdp", Gdp, 5);
        w.Accept(ContractA, 100f, (OgKush, 3));
        w.ClientHandover(ContractA, (OgKush, 3));

        Assert.False(w.Host.Submit(ContractA, ("gdp", 3)));
        Assert.Equal(5, w.Host.UnitsOf("gdp"));
        Assert.Equal(1, w.ClientSettlements + w.Host.PaymentCount);
    }

    [Fact]
    public void P07_C07_after_the_host_completes_a_client_cannot_settle_again()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Stock("gdp", Gdp, 5);
        w.Accept(ContractA, 100f, (OgKush, 3));
        w.Host.Submit(ContractA, ("gdp", 3));

        Assert.False(w.ClientHandover(ContractA, (OgKush, 3)));
        Assert.Equal((1, 0), (w.Host.PaymentCount, w.ClientSettlements));
    }

    [Fact]
    public void P07_C08_repeated_commit_callbacks_count_once()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Accept(ContractA, 100f, (OgKush, 3));

        Assert.Equal(CommitObservation.First, w.Host.Coordinator.OnHandoverProcessed(ContractA));
        Assert.Equal(CommitObservation.Duplicate, w.Host.Coordinator.OnHandoverProcessed(ContractA));
        var t = w.Host.Coordinator.Find(ContractA)!;
        Assert.Equal(SaleState.CommitObserved, t.State);       // acted on once
        Assert.Equal(TransactionPhase.Committed, TransactionPhases.Of(t.State));
        Assert.Equal(2, t.CommitCount);                         // both callbacks are recorded for diagnostics
    }

    [Fact]
    public void P07_C09_accepted_orders_keep_their_snapshot_when_the_host_changes_rules()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Stock("gdp", Gdp, 5);
        w.Accept(ContractA, 100f, (OgKush, 3));

        w.Host.Policies.Update(_ => Coop.WeedOnly(OgKush));

        Assert.True(w.Host.Submit(ContractA, ("gdp", 3))); // the snapshot taken at acceptance still allows GDP
    }

    [Fact]
    public void P07_C10_new_orders_use_the_host_rules_after_a_change()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Policies.Update(_ => Coop.WeedOnly(OgKush));

        var order = w.Accept(ContractA, 100f, (OgKush, 3)).Order!;

        Assert.Equal(new[] { OgKush }, order.AllowedProducts);
    }

    [Fact]
    public void P07_C11_the_cheap_swap_stays_refused_for_the_multiplayer_host()
    {
        var w = new CoopWorld(Coop.WeedOnly(OgKush, SourDiesel, Gdp));
        w.Host.Coordinator.Prices = Tiered;
        w.Host.Stock("og", OgKush, 10).Stock("gdp", Gdp, 10);
        w.Accept(ContractA, 225f, (SourDiesel, 5));

        Assert.False(w.Host.Submit(ContractA, ("og", 5)));
        Assert.True(w.Host.Submit(ContractA, ("gdp", 5)));
        Assert.Equal((225f, 1), (w.Host.Cash, w.Host.PaymentCount));
    }

    [Fact]
    public void P07_C12_host_with_unready_settings_keeps_new_orders_vanilla()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Coordinator.PolicyReady = () => false;

        var creation = w.Accept(ContractA, 100f, (OgKush, 3));

        Assert.Equal(OrderOutcome.PolicyNotLoaded, creation.Outcome);
    }

    [Fact]
    public void P07_C13_unknown_player_count_on_the_host_still_manages()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Coordinator.ApplySession(Coop.Decide(true, true, players: -1));

        Assert.True(w.Accept(ContractA, 100f, (OgKush, 3)).IsManaged);
    }

    [Fact]
    public void P07_C14_host_becoming_a_client_stops_managing_immediately()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Stock("gdp", Gdp, 5);
        w.Accept(ContractA, 100f, (OgKush, 3));

        w.Host.Coordinator.ApplySession(Coop.ClientReady());

        Assert.Null(w.Host.Coordinator.Validate(ContractA, new DeliveredItem?[] { Item("gdp", Gdp, 3) }));
        Assert.False(w.Host.Submit(ContractA, ("gdp", 3))); // vanilla exact product now
    }

    [Fact]
    public void P07_C15_many_contracts_split_between_host_and_clients_settle_exactly_once_each()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Host.Stock("gdp", Gdp, 1000);
        for (var i = 0; i < 40; i++)
        {
            w.Accept("contract-" + i, 50f, (OgKush, 2));
        }

        for (var i = 0; i < 40; i++)
        {
            var id = "contract-" + i;
            if (i % 2 == 0)
            {
                w.Host.Submit(id, ("gdp", 2));
                w.ClientHandover(id, (OgKush, 2)); // too late: already completed
            }
            else
            {
                w.ClientHandover(id, (OgKush, 2));
                w.Host.Submit(id, ("gdp", 2));     // too late
            }
        }

        Assert.Equal(40, w.Host.PaymentCount + w.ClientSettlements);
        Assert.Equal(20, w.Host.PaymentCount);
        Assert.Equal(1000 - 40, w.Host.UnitsOf("gdp"));
        Assert.Equal(0, w.Host.Coordinator.Book.Count);
    }

    [Fact]
    public void P07_C16_handover_text_and_orders_never_leak_to_the_client_book()
    {
        var w = new CoopWorld(GdpOrOg);
        w.Accept(ContractA, 100f, (OgKush, 3));

        Assert.Null(w.Client.Find(ContractA));
        Assert.Empty(w.Client.Book.Orders);
    }
}
