using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Quests;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Sales;
using TakeItOrLeaveIt.Core.Diagnostics;

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
    private static bool _saveReady;

    public static Func<ProductCatalogSnapshot?>? Catalog { get; set; }

    /// <summary>The sales host (set by the mod); polls the Phase 07 session model.</summary>
    public static PlayerSalesHost? Host { get; set; }

    /// <summary>
    /// Phase 07 (docs/MULTIPLAYER.md): reads the network facts and applies the session decision to both coordinators and
    /// the menu. Host-side features run on the host whatever the number of players; a client manages nothing and shows
    /// the host's rules read-only; anything unknown stays vanilla.
    /// </summary>
    public static void RefreshAuthority()
    {
        var host = Host;
        if (host is null || GameSales.Coordinator is null)
        {
            return;
        }

        try
        {
            var reading = GameNetwork.Read(_saveReady, host.SaveIdentity, host.HooksApplied, compatibilityOk: true,
                host.HooksApplied ? null : host.HooksReason);
            host.ApplyNetwork(reading, SteamLobbyRulesChannel.Instance);
        }
        catch (Exception ex)
        {
            host.FailClosed();
            GameSales.Once("session-error", $"Session check failed; Take It manages nothing until the next successful check. {LogText.Describe(ex)}", warning: true);
        }
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
        _saveReady = saveReady;

        // Phase 07: the session is checked even before a save is ready (a client may never have a local catalog).
        if (now >= _nextAuthorityCheck)
        {
            _nextAuthorityCheck = now + 2_000;
            RefreshAuthority();
        }

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
