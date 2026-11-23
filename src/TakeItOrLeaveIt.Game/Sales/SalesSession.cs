using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Quests;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Session-level game reads for player sales: who has authority, which contracts still exist, and the text
/// shown on the handover screen. Polled on a slow timer (never per frame work beyond a stopwatch read).
/// </summary>
internal static class SalesSession
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _nextAuthorityCheck;
    private static long _nextPrune;
    private static long _readySince = -1;

    public static Func<ProductCatalogSnapshot?>? Catalog { get; set; }

    /// <summary>Single player host only (FishNet server with at most one player). See PLAYER_SALES.md.</summary>
    public static void RefreshAuthority()
    {
        var coordinator = GameSales.Coordinator;
        if (coordinator is null)
        {
            return;
        }

        var isServer = Il2CppFishNet.InstanceFinder.IsServer;
        var players = Player.PlayerList?.Count ?? -1;
        coordinator.SetAuthority(isServer, players < 0 ? 0 : players);
        GameDealers.Coordinator?.SetAuthority(isServer, players < 0 ? 0 : players);
    }

    /// <summary>Called from OnUpdate. Cheap unless a timer elapsed.</summary>
    public static void Tick(bool saveReady)
    {
        var coordinator = GameSales.Coordinator;
        if (coordinator is null)
        {
            return;
        }

        var now = Clock.ElapsedMilliseconds;
        if (!saveReady)
        {
            _readySince = -1;
            return;
        }

        if (_readySince < 0)
        {
            _readySince = now;
            _nextPrune = now + 20_000; // let the save finish restoring its contracts first
        }

        if (now >= _nextAuthorityCheck)
        {
            _nextAuthorityCheck = now + 2_000;
            RefreshAuthority();
        }

        if (now >= _nextPrune)
        {
            _nextPrune = now + 60_000;
            Prune(coordinator);
        }
    }

    public static string Describe(CategoryOrder order)
    {
        var catalog = Catalog?.Invoke();
        var category = DeliveryValidator.CategoryName(order.Category, catalog);
        var names = order.AllowedProducts
            .Select(p => catalog is not null && catalog.TryGetProduct(p, out var d) ? d.DisplayName : p.Value)
            .ToList();
        var list = names.Count <= 4 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" +{names.Count - 3} more";
        var mixing = order.MixedDeliveries ? "Mixing products is allowed." : "Use a single product.";
        return $"Deliver exactly {order.RequiredUnits} {category}, any of: {list}. {mixing}";
    }

    private static void Prune(PlayerSaleCoordinator coordinator)
    {
        var dealers = GameDealers.Coordinator;
        if (coordinator.Book.Count == 0 && (dealers is null || dealers.ActiveAppointments == 0))
        {
            return;
        }

        var contracts = Contract.Contracts;
        if (contracts is null)
        {
            return;
        }

        var live = new List<string?>(contracts.Count);
        for (var i = 0; i < contracts.Count; i++)
        {
            live.Add(GameSales.ContractId(contracts[i]));
        }

        var removed = coordinator.Prune(live);
        if (removed > 0)
        {
            GameSales.Log?.Info($"Removed {removed} category order(s) whose contracts no longer exist.");
            GameSales.InvalidateCache();
        }

        var removedAppointments = dealers?.Prune(live) ?? 0;
        if (removedAppointments > 0)
        {
            GameSales.Log?.Info($"Removed {removedAppointments} dealer appointment(s) whose contracts no longer exist; their reservations were released.");
            GameSales.InvalidateCache();
        }
    }
}
