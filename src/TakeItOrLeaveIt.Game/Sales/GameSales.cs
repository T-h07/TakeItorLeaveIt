using System;
using System.Collections.Generic;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Game-facing adapter for player sales (IL2CPP, built only with local game references). Converts the game's
/// contracts and handover items into Core inputs and hands every decision to <see cref="PlayerSaleCoordinator"/>.
/// It reads game state only; it never pays, moves items, awards XP or completes contracts.
/// Every member runs on the Unity main thread (Harmony callbacks of game methods).
/// </summary>
internal static class GameSales
{
    private static readonly HashSet<string> LoggedOnce = new(StringComparer.Ordinal);

    // Last validation, keyed by contract, catalog generation and the identity/quantity of each handover item.
    // The handover screen asks the same question many times per second; this keeps that path allocation-free.
    private static string? _cacheContract;
    private static long _cacheSignature;
    private static DeliveryValidation? _cacheValidation;
    private static MatchResult? _cacheMatch;

    // GUID text of the last contract seen (GUID.ToString allocates).
    private static IntPtr _lastContractPointer;
    private static string? _lastContractId;

    public static PlayerSaleCoordinator? Coordinator { get; set; }

    public static IModLog? Log { get; set; }

    public static Func<long>? CatalogGeneration { get; set; }

    /// <summary>The contract's GUID text, or null when it cannot be read.</summary>
    public static string? ContractId(Contract? contract)
    {
        if (contract is null)
        {
            return null;
        }

        var pointer = contract.Pointer;
        if (pointer == _lastContractPointer && _lastContractId is not null)
        {
            return _lastContractId;
        }

        var id = contract.GUID.ToString();
        _lastContractPointer = pointer;
        _lastContractId = id;
        return id;
    }

    /// <summary>The managed order for this contract, or null when vanilla rules apply.</summary>
    public static SaleTransaction? Managed(Contract? contract, out string? contractId)
    {
        contractId = null;
        var coordinator = Coordinator;
        if (coordinator is null || coordinator.Book.Count == 0 || contract is null)
        {
            return null;
        }

        contractId = ContractId(contract);
        return coordinator.Find(contractId);
    }

    /// <summary>True when the contract is a managed player order or a managed dealer appointment.</summary>
    public static bool ManagedAny(Contract? contract, out string? contractId)
    {
        if (Managed(contract, out contractId) is not null)
        {
            return true;
        }

        var dealers = GameDealers.Coordinator;
        if (dealers is null || dealers.ActiveAppointments == 0 || contract is null)
        {
            return false;
        }

        contractId ??= ContractId(contract);
        return dealers.Find(contractId) is not null;
    }

    /// <summary>Contract accepted: build the Core request from the vanilla product list.</summary>
    public static OrderRequest ToRequest(Contract contract, bool isPlayerContract)
    {
        var lines = new List<OrderLine?>();
        var entries = contract.ProductList?.entries;
        if (entries is not null)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                lines.Add(entry is null ? null : new OrderLine(entry.ProductID, entry.Quantity));
            }
        }

        return new OrderRequest(ContractId(contract), isPlayerContract, lines);
    }

    /// <summary>Validates handover items (cached while they do not change).</summary>
    public static DeliveryValidation? Validate(string contractId, IReadOnlyList<ItemInstance?> items)
    {
        var signature = Signature(items);
        if (_cacheValidation is not null && string.Equals(_cacheContract, contractId, StringComparison.Ordinal) && _cacheSignature == signature)
        {
            return _cacheValidation;
        }

        var delivered = new List<DeliveredItem?>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            delivered.Add(ToDelivered(i, items[i]));
        }

        var validation = Coordinator?.Validate(contractId, delivered) ?? GameDealers.Coordinator?.Validate(contractId, delivered);
        _cacheContract = contractId;
        _cacheSignature = signature;
        _cacheValidation = validation;
        _cacheMatch = null;
        if (validation is not null && !validation.IsValid)
        {
            Once($"reject:{contractId}:{validation.Rejection}:{validation.DeliveredUnits}",
                $"Handover for order {contractId} refused before transfer: {validation.Rejection} ({validation.DeliveredUnits}/{validation.Order.RequiredUnits} units).");
        }

        return validation;
    }

    /// <summary>
    /// Match for a validated delivery: each item's similarity to its own product at the requested quality
    /// (the game's quality term without the recipe term), weighted by units.
    /// </summary>
    public static MatchResult Match(Contract contract, DeliveryValidation validation, IReadOnlyList<ItemInstance?> items)
    {
        if (_cacheMatch is { } cached && ReferenceEquals(validation, _cacheValidation))
        {
            return cached;
        }

        var quality = RequiredQuality(contract);
        var ratings = new List<RatedUnits>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var product = items[i]?.TryCast<ProductItemInstance>();
            var definition = product?.Definition?.TryCast<ProductDefinition>();
            if (product is null || definition is null)
            {
                throw new InvalidOperationException("A validated handover item is no longer a product.");
            }

            ratings.Add(new RatedUnits((long)product.Quantity * product.Amount, product.GetSimilarity(definition, quality)));
        }

        var match = DeliveryMatch.Compute(validation, ratings);
        if (ReferenceEquals(validation, _cacheValidation))
        {
            _cacheMatch = match;
        }

        Once($"match:{validation.Order.ContractId}:{match.Match:0.000}",
            $"Order {validation.Order.ContractId}: {validation.DeliveredUnits} {validation.Order.Category} accepted (match {match.Match:0.00}, recipe not compared).");
        return match;
    }

    public static List<ItemInstance?> Items(Il2CppSystem.Collections.Generic.List<ItemInstance>? list)
    {
        var items = new List<ItemInstance?>(list?.Count ?? 0);
        if (list is not null)
        {
            for (var i = 0; i < list.Count; i++)
            {
                items.Add(list[i]);
            }
        }

        return items;
    }

    public static List<ItemInstance?> Items(Il2CppSystem.Collections.Generic.List<ProductItemInstance>? list)
    {
        var items = new List<ItemInstance?>(list?.Count ?? 0);
        if (list is not null)
        {
            for (var i = 0; i < list.Count; i++)
            {
                items.Add(list[i]);
            }
        }

        return items;
    }

    public static void InvalidateCache()
    {
        _cacheContract = null;
        _cacheValidation = null;
        _cacheMatch = null;
        _lastContractPointer = IntPtr.Zero;
        _lastContractId = null;
    }

    /// <summary>Logs a message once per key (bounded), so per-frame callbacks cannot flood the log.</summary>
    public static void Once(string key, string message, bool warning = false)
    {
        if (LoggedOnce.Count > 500 || !LoggedOnce.Add(key))
        {
            return;
        }

        if (warning)
        {
            Log?.Warning(message);
        }
        else
        {
            Log?.Info(message);
        }
    }

    private static DeliveredItem? ToDelivered(int index, ItemInstance? item)
    {
        if (item is null)
        {
            return null;
        }

        var slot = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var product = item.TryCast<ProductItemInstance>();
        if (product is null)
        {
            // Not a product: counted as one unit of an id the catalog does not know, so it is refused.
            return new DeliveredItem(slot, item.ID, item.Quantity, 1, true);
        }

        return new DeliveredItem(slot, product.ID, product.Quantity, product.Amount, product.AppliedPackaging != null);
    }

    private static EQuality RequiredQuality(Contract contract)
    {
        var quality = EQuality.Trash;
        var entries = contract.ProductList?.entries;
        if (entries is not null)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry is not null && entry.Quality > quality)
                {
                    quality = entry.Quality;
                }
            }
        }

        return quality;
    }

    private static long Signature(IReadOnlyList<ItemInstance?> items)
    {
        unchecked
        {
            long hash = 1469598103934665603L ^ (CatalogGeneration?.Invoke() ?? 0);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                hash = (hash ^ (item is null ? 0 : item.Pointer.ToInt64())) * 1099511628211L;
                hash = (hash ^ (item is null ? 0 : item.Quantity)) * 1099511628211L;
            }

            return (hash ^ items.Count) * 1099511628211L;
        }
    }
}
