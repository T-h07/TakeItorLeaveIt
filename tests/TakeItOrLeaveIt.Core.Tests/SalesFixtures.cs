using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

internal static class Sales
{
    public const string ContractA = "c0ffee00-0000-4000-8000-00000000000a";
    public const string ContractB = "c0ffee00-0000-4000-8000-00000000000b";

    public static readonly ProductKey Unknown = ProductKey.Create("notinthecatalog");

    public static OrderRequest Request(string id, params (ProductKey Product, int Quantity)[] lines)
        => new(id, true, lines.Select(l => new OrderLine(l.Product.Value, l.Quantity)));

    public static OrderRequest DealerRequest(string id, params (ProductKey Product, int Quantity)[] lines)
        => new(id, false, lines.Select(l => new OrderLine(l.Product.Value, l.Quantity)));

    public static CategoryOrder Order(int units, bool mixed = true, params ProductKey[] allowed)
        => new(ContractA, Weed, units, allowed.Length > 0 ? allowed : new[] { OgKush, SourDiesel, Gdp }, mixed, 1);

    public static DeliveredItem Item(string slot, ProductKey product, int quantity, int unitsPerItem = 1, bool packaged = true)
        => new(slot, product.Value, quantity, unitsPerItem, packaged);

    public static PolicyConfiguration Policy(Func<PolicyConfiguration, PolicyConfiguration>? change = null)
        => change is null ? PolicyConfiguration.Default : change(PolicyConfiguration.Default);
}

/// <summary>In-memory file system with optional write failure, for the order store.</summary>
internal sealed class MemoryFileSystem : ISettingsFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public bool FailWrites { get; set; }

    public int Writes { get; private set; }

    public bool FileExists(string path) => Files.ContainsKey(path);

    public string ReadAllText(string path) => Files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);

    public void WriteAllText(string path, string contents)
    {
        if (FailWrites)
        {
            throw new IOException("disk full (simulated)");
        }

        Writes++;
        Files[path] = contents;
    }

    public void ReplaceFile(string source, string destination)
    {
        Files[destination] = Files[source];
        Files.Remove(source);
    }

    public void CopyFile(string source, string destination) => Files[destination] = Files[source];

    public void DeleteFile(string path) => Files.Remove(path);

    public void CreateDirectory(string path)
    {
    }
}

/// <summary>
/// Deterministic stand-in for the game's authoritative settlement (Customer.ProcessHandover and the server
/// RPC). It models what the call graph shows vanilla doing (validate via the match → transfer items → pay →
/// XP → complete) and counts every side effect. These tests prove the model, not the real game economy:
/// the in-game tests in docs/PHASE_03_TESTING.md check the same invariants.
/// </summary>
internal sealed class SettlementHarness
{
    private readonly Dictionary<string, (ProductKey Product, int Units)> _inventory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Payment, string State, (ProductKey Product, int Quantity)[] Lines)> _contracts = new(StringComparer.Ordinal);

    public SettlementHarness(PolicyConfiguration? policy = null, ProductCatalogSnapshot? catalog = null)
    {
        Policies = new InMemoryPolicyProvider(policy ?? PolicyConfiguration.Default);
        Catalog = catalog ?? AllWeedUnlocked();
        Coordinator = new PlayerSaleCoordinator(Policies, () => Catalog, Log);
        Coordinator.SetAuthority(isServer: true, playerCount: 1);
        Coordinator.BindSave("save-test", null);
    }

    public InMemoryPolicyProvider Policies { get; }

    public ProductCatalogSnapshot? Catalog { get; set; }

    public RecordingLog Log { get; } = new();

    public PlayerSaleCoordinator Coordinator { get; }

    public float Cash { get; private set; }

    public int PaymentCount { get; private set; }

    public int XpAwards { get; private set; }

    public int Completions { get; private set; }

    public int RelationshipChanges { get; private set; }

    public SettlementHarness Stock(string slot, ProductKey product, int units)
    {
        _inventory[slot] = (product, units);
        return this;
    }

    public int UnitsOf(string slot) => _inventory.TryGetValue(slot, out var s) ? s.Units : 0;

    public int TotalUnits => _inventory.Values.Sum(s => s.Units);

    public OrderCreation Accept(string contractId, float payment, params (ProductKey Product, int Quantity)[] lines)
    {
        _contracts[contractId] = (payment, "Active", lines);
        return Coordinator.OnContractAccepted(Sales.Request(contractId, lines));
    }

    public string StateOf(string contractId) => _contracts[contractId].State;

    public void Expire(string contractId) => End(contractId, "Expired", SaleEnd.Expired);

    public void Cancel(string contractId) => End(contractId, "Canceled", SaleEnd.Canceled);

    /// <summary>Player presses Done with these slots (units taken from each slot). Returns true when the sale settled.</summary>
    public bool Submit(string contractId, params (string Slot, int Units)[] handover)
    {
        if (!_contracts.TryGetValue(contractId, out var contract) || contract.State != "Active")
        {
            return false; // vanilla cannot hand over to an ended contract
        }

        var items = new List<DeliveredItem?>();
        foreach (var (slot, units) in handover)
        {
            if (!_inventory.TryGetValue(slot, out var stock) || stock.Units < units)
            {
                return false; // items no longer exist: the handover screen cannot contain them
            }

            items.Add(new DeliveredItem(slot, stock.Product.Value, units, 1, true));
        }

        var validation = Coordinator.Validate(contractId, items);
        bool accepted;
        if (validation is not null)
        {
            // Mod-managed: the gate refuses invalid deliveries before anything moves.
            accepted = validation.IsValid && DeliveryMatch.Compute(validation, items.Select(i => new RatedUnits(i!.Units, 1f))).MatchedUnits == validation.Order.RequiredUnits;
        }
        else
        {
            // Vanilla: exact products and quantities.
            accepted = contract.Lines.All(line => handover.Where(h => _inventory[h.Slot].Product == line.Product).Sum(h => h.Units) >= line.Quantity);
        }

        if (!accepted)
        {
            return false;
        }

        // Vanilla settlement, once: transfer items, pay, XP, relationship, complete.
        foreach (var (slot, units) in handover)
        {
            var stock = _inventory[slot];
            _inventory[slot] = (stock.Product, stock.Units - units);
        }

        Cash += contract.Payment;
        PaymentCount++;
        XpAwards++;
        RelationshipChanges++;
        Coordinator.OnHandoverProcessed(contractId);
        _contracts[contractId] = (contract.Payment, "Completed", contract.Lines);
        Completions++;
        Coordinator.OnContractEnded(contractId, SaleEnd.Completed);
        return true;
    }

    private void End(string contractId, string state, SaleEnd end)
    {
        var contract = _contracts[contractId];
        _contracts[contractId] = (contract.Payment, state, contract.Lines);
        Coordinator.OnContractEnded(contractId, end);
    }
}
