using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Game;

/// <summary>
/// Owns the player-sales coordinator for the running game: binds it to the active save (loading that save's
/// order snapshots), writes snapshots off the main thread when they change, and builds the status report.
/// Compiled in every build; the game hooks themselves exist only in builds with local game references.
/// </summary>
internal sealed class PlayerSalesHost
{
    private readonly IModLog _log;
    private readonly ProductCatalogService _catalog;
    private readonly ActiveOrderStore _store;
    private readonly object _writeGate = new();
    private Task _lastWrite = Task.CompletedTask;
    private long _writeSequence;
    private long _nextStatusRefresh;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public PlayerSalesHost(IModLog log, IPolicyProvider policies, ProductCatalogService catalog, string root, Func<ModMenuController?> menu)
    {
        _log = log;
        _catalog = catalog;
        _store = new ActiveOrderStore(root);
        Menu = menu;
        Coordinator = new PlayerSaleCoordinator(policies, () => _catalog.State.Snapshot, log)
        {
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Status.Runtime = BuildTarget.Runtime.ToString();
        _catalog.StateChanged += OnCatalogState;
        OnCatalogState(_catalog.State);
    }

    public PlayerSaleCoordinator Coordinator { get; }

    public PlayerSalesStatus Status { get; } = new();

    private Func<ModMenuController?> Menu { get; }

    /// <summary>Called by the mod after patch registration.</summary>
    public void SetHooks(bool applied, string? unavailableReason)
    {
        var state = applied ? ComponentStatus.Active : ComponentStatus.Unavailable;
        Status.OfferHook = state;
        Status.HandoverValidation = state;
        Status.TransactionIntegration = state;
        Status.HooksUnavailableReason = applied ? null : unavailableReason;
        RefreshStatus();
        foreach (var line in Status.Lines())
        {
            _log.Info(line);
        }
    }

    public IReadOnlyList<string> StatusLines()
    {
        RefreshStatus();
        return Status.Lines();
    }

    /// <summary>Per frame: one stopwatch read and a version compare unless something changed.</summary>
    public void Tick()
    {
        if (Coordinator.HasUnsavedOrders)
        {
            Persist(synchronous: false);
        }

        var now = _clock.ElapsedMilliseconds;
        if (now >= _nextStatusRefresh)
        {
            _nextStatusRefresh = now + 5_000;
            var wasEnabled = Status.IsEnabled;
            var headline = Status.Headline;
            RefreshStatus();
            if (wasEnabled != Status.IsEnabled || headline != Status.Headline)
            {
                _log.Info($"Player Sales: {Status.Headline}.");
            }
        }
    }

    public void Shutdown()
    {
        _catalog.StateChanged -= OnCatalogState;
        Persist(synchronous: true);
    }

    private void OnCatalogState(CatalogState state)
    {
        var identity = state.Session.IsNone ? null : state.Session.SaveIdentity;
        if (string.Equals(identity, Coordinator.SaveIdentity, StringComparison.Ordinal))
        {
            return;
        }

        Persist(synchronous: true);
        IEnumerable<(CategoryOrder, SaleState)>? orders = null;
        if (identity is not null)
        {
            var loaded = SafeLoad(identity);
            orders = loaded.Orders;
            foreach (var issue in loaded.Issues)
            {
                _log.Warning($"Active orders for {identity}: {issue}");
            }

            if (loaded.Orders.Count > 0)
            {
                _log.Info($"Restored {loaded.Orders.Count} active category order(s) for {identity}.");
            }
        }

        Coordinator.BindSave(identity, orders);
        RefreshStatus();
    }

    private OrdersLoadResult SafeLoad(string identity)
    {
        try
        {
            return _store.Load(identity);
        }
        catch (Exception ex)
        {
            _log.Error("Loading active orders failed; they use vanilla rules.", ex);
            return new OrdersLoadResult(OrdersLoadStatus.Unreadable, Array.Empty<(CategoryOrder, SaleState)>(), Array.Empty<string>());
        }
    }

    private void Persist(bool synchronous)
    {
        var identity = Coordinator.SaveIdentity;
        if (identity is null || !Coordinator.HasUnsavedOrders)
        {
            return;
        }

        // Serialise on the main thread (the book is not thread-safe); write on a worker, newest wins.
        var json = ActiveOrderStore.ToJson(Coordinator.Book.Orders);
        Coordinator.MarkPersisted();
        long sequence;
        lock (_writeGate)
        {
            sequence = ++_writeSequence;
        }

        void Write()
        {
            lock (_writeGate)
            {
                if (sequence != _writeSequence && !synchronous)
                {
                    return; // a newer snapshot is queued
                }

                try
                {
                    _store.SaveText(identity, json);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Could not write active orders for {identity} ({ex.GetType().Name}: {ex.Message}). They stay in memory and are retried on the next change.");
                }
            }
        }

        if (synchronous)
        {
            Write();
        }
        else
        {
            _lastWrite = _lastWrite.ContinueWith(_ => Write(), TaskScheduler.Default);
        }
    }

    private void RefreshStatus()
    {
        var menu = Menu();
        Status.CatalogReady = _catalog.State.IsReady;
        Status.PolicyLoaded = menu?.PolicyReadyForSales ?? false;
        Status.ModEnabledForSave = menu?.Saved.Enabled ?? false;
        Status.Authoritative = Coordinator.IsAuthoritative;
        Status.AuthorityDetail = Coordinator.AuthorityDetail;
        Status.ActiveOrders = Coordinator.Book.Count;
    }
}
