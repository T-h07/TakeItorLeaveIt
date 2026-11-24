using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

internal static class Dealers
{
    public const string Benji = "benji_coleman";
    public const string Molly = "molly_presley";
    public const string ContractC = "c0ffee00-0000-4000-8000-00000000000c";
    public const string ContractD = "c0ffee00-0000-4000-8000-00000000000d";

    public static string Contract(int n) => $"c0ffee00-0000-4000-8000-{n:x12}";

    public static DealerStock StockOf(string dealer, params (ProductKey Product, int Units)[] stacks)
        => new(dealer, stacks.Select((s, i) => new InventoryStack($"slot:{i}", s.Product, s.Units)));

    public static CategoryOrder Order(string contract, int units, bool mixed = true, params ProductKey[] allowed)
        => new(contract, Weed, units, allowed.Length > 0 ? allowed : new[] { OgKush, SourDiesel, Gdp }, mixed, 1);
}

/// <summary>
/// A simulated vanilla dealer for the Phase 04 model: per-dealer inventory slots, the dealer's handover routine
/// (the D1 decision, then removal of exact product ids from slots), the customer's judgement, and one vanilla
/// settlement (player share, dealer cut, XP, completion). It mirrors the call flow in
/// docs/PHASE_04_PATCH_ANALYSIS.md; the in-game tests in docs/PHASE_04_TESTING.md check the same invariants.
/// </summary>
internal sealed class DealerHarness
{
    public const float CutRate = 0.2f;

    private readonly Dictionary<string, SortedDictionary<string, (ProductKey Product, int Units)>> _inventory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Dealer, float Payment, string State, (ProductKey Product, int Quantity)[] Lines)> _contracts = new(StringComparer.Ordinal);
    private int _slot;

    public DealerHarness(PolicyConfiguration? policy = null, ProductCatalogSnapshot? catalog = null)
    {
        Policies = new InMemoryPolicyProvider(policy ?? PolicyConfiguration.Default);
        Catalog = catalog ?? AllWeedUnlocked();
        Coordinator = new DealerSaleCoordinator(Policies, () => Catalog, Log) { Prices = Sales.Flat };
        Coordinator.SetAuthority(isServer: true, playerCount: 1);
        Coordinator.BindSave("save-test", null);
    }

    public InMemoryPolicyProvider Policies { get; }

    public ProductCatalogSnapshot? Catalog { get; set; }

    public RecordingLog Log { get; } = new();

    public DealerSaleCoordinator Coordinator { get; }

    public float PlayerCash { get; private set; }

    public float DealerCash { get; private set; }

    public int Payments { get; private set; }

    public int XpAwards { get; private set; }

    public int Completions { get; private set; }

    public int RemovalCalls { get; private set; }

    /// <summary>Simulates the game's removal returning this many units fewer than asked (strict rollback tests).</summary>
    public int RemovalShortfall { get; set; }

    /// <summary>Handovers where the customer received nothing because the mod withheld the delivery.</summary>
    public int WithheldHandovers { get; private set; }

    /// <summary>Every unit that left a dealer's inventory through a handover removal.</summary>
    public long UnitsRemoved { get; private set; }

    public DealerHarness Stock(string dealer, ProductKey product, int units)
    {
        Slots(dealer)[$"slot:{_slot++:D4}"] = (product, units);
        return this;
    }

    /// <summary>Stock leaves outside any appointment (the player takes it back, another sale, ...).</summary>
    public int Take(string dealer, ProductKey product, int units) => Remove(dealer, product, units, countAsHandover: false).Sum(i => i.Units);

    public int UnitsOf(string dealer, ProductKey product) => Slots(dealer).Values.Where(s => s.Product == product).Sum(s => s.Units);

    public int TotalUnits(string dealer) => Slots(dealer).Values.Sum(s => s.Units);

    public DealerStock StockOf(string dealer)
        => new(dealer, Slots(dealer).Where(s => s.Value.Units > 0).Select(s => new InventoryStack(s.Key, s.Value.Product, s.Value.Units)));

    public string StateOf(string contract) => _contracts[contract].State;

    /// <summary>A contract accepted by Benji.</summary>
    public DealerOrderCreation Accept(string contract, float payment, params (ProductKey Product, int Quantity)[] lines)
        => Accept(contract, Dealers.Benji, payment, lines);

    public DealerOrderCreation Accept(string contract, string dealer, float payment, params (ProductKey Product, int Quantity)[] lines)
    {
        _contracts[contract] = (dealer, payment, "Active", lines);
        return Coordinator.OnContractAccepted(Sales.DealerRequest(contract, lines), dealer, StockOf(dealer));
    }

    /// <summary>The dealer's periodic tick (D2).</summary>
    public ReconcileReport Tick(string dealer) => Coordinator.Reconcile(StockOf(dealer));

    /// <summary>Moves a contract to another dealer (the game's SetDealer).</summary>
    public void Reassign(string contract, string dealer)
    {
        var c = _contracts[contract];
        _contracts[contract] = (dealer, c.Payment, c.State, c.Lines);
    }

    /// <summary>
    /// The dealer's handover routine for one contract: D1 decides, the dealer removes, the customer judges, and
    /// vanilla settles once or hands the items back. Returns the units the customer received.
    /// </summary>
    public (bool Settled, List<(ProductKey Product, int Units)> Items, HandoverDecision Decision) Handover(string contract, bool customerAccepts = true)
    {
        var c = _contracts[contract];
        var empty = new List<(ProductKey, int)>();
        if (c.State != "Active")
        {
            return (false, empty, HandoverDecision.Vanilla);
        }

        // D1: Dealer.RemoveContractItems (strict: a managed appointment never runs the exact-product removal).
        var items = new List<(ProductKey Product, int Units)>();
        var plan = Coordinator.Find(contract) is null
            ? null
            : Coordinator.PrepareHandover(contract, StockOf(c.Dealer));
        var decision = plan?.Decision
                       ?? (Coordinator.BlocksUnmanaged(Sales.DealerRequest(contract, c.Lines), out _) ? HandoverDecision.Withheld : HandoverDecision.Vanilla);
        switch (decision)
        {
            case HandoverDecision.Vanilla:
                foreach (var line in c.Lines)
                {
                    items.AddRange(Remove(c.Dealer, line.Product, line.Quantity, countAsHandover: true));
                }

                break;
            case HandoverDecision.Fulfil:
                var shortBy = RemovalShortfall;
                foreach (var line in plan!.Lines)
                {
                    var ask = Math.Max(0, line.Units - shortBy);
                    shortBy -= line.Units - ask;
                    items.AddRange(Remove(c.Dealer, line.Product, ask, countAsHandover: true));
                }

                if (Coordinator.CompleteHandover(contract, items.Sum(i => i.Units)) != HandoverCompletion.Committed)
                {
                    foreach (var item in items)
                    {
                        Stock(c.Dealer, item.Product, item.Units); // AddItemToInventory: rolled back, nothing handed over
                    }

                    items.Clear();
                    WithheldHandovers++;
                }

                break;
            default:
                WithheldHandovers++;
                break;
        }

        // The customer judges the delivery (GetProductListMatch: the snapshot for managed orders, exact products otherwise).
        var delivered = items.Select((i, n) => (DeliveredItem?)new DeliveredItem(n.ToString(), i.Product.Value, i.Units, 1, true)).ToList();
        var validation = Coordinator.Validate(contract, delivered);
        var ok = validation is not null
            ? validation.IsValid
            : c.Lines.All(line => items.Where(i => i.Product == line.Product).Sum(i => i.Units) >= line.Quantity);
        if (!ok || !customerAccepts || items.Count == 0)
        {
            foreach (var item in items)
            {
                Stock(c.Dealer, item.Product, item.Units); // AddItemToInventory: handed back
            }

            return (false, items, decision);
        }

        // Vanilla settlement, once: customer pays, dealer keeps the cut, XP, contract completes.
        PlayerCash += c.Payment * (1 - CutRate);
        DealerCash += c.Payment * CutRate;
        Payments++;
        XpAwards++;
        Coordinator.OnHandoverProcessed(contract);
        _contracts[contract] = (c.Dealer, c.Payment, "Completed", c.Lines);
        Completions++;
        Coordinator.OnContractEnded(contract, SaleEnd.Completed);
        return (true, items, decision);
    }

    public void End(string contract, SaleEnd end)
    {
        var c = _contracts[contract];
        _contracts[contract] = (c.Dealer, c.Payment, end.ToString(), c.Lines);
        Coordinator.OnContractEnded(contract, end);
    }

    private SortedDictionary<string, (ProductKey Product, int Units)> Slots(string dealer)
    {
        if (!_inventory.TryGetValue(dealer, out var slots))
        {
            slots = new SortedDictionary<string, (ProductKey, int)>(StringComparer.Ordinal);
            _inventory[dealer] = slots;
        }

        return slots;
    }

    /// <summary>The game's RemoveAndReturnProductFromInventory: exact product id, drains slots, returns what it found.</summary>
    private List<(ProductKey Product, int Units)> Remove(string dealer, ProductKey product, int units, bool countAsHandover)
    {
        if (countAsHandover)
        {
            RemovalCalls++;
        }

        var removed = new List<(ProductKey, int)>();
        var slots = Slots(dealer);
        foreach (var key in slots.Keys.ToList())
        {
            var slot = slots[key];
            if (units == 0)
            {
                break;
            }

            if (slot.Product != product || slot.Units == 0)
            {
                continue;
            }

            var take = Math.Min(units, slot.Units);
            slots[key] = (slot.Product, slot.Units - take);
            units -= take;
            removed.Add((product, take));
            if (countAsHandover)
            {
                UnitsRemoved += take;
            }
        }

        return removed;
    }
}
