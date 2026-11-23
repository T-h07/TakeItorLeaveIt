using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Game;

/// <summary>
/// Owns the sales coordinators for the running game: player sales (Phase 03) and hired-dealer sales (Phase 04).
/// Binds both to the active save (loading that save's snapshots), writes them off the main thread when they
/// change, and builds the status report. Compiled in every build; the game hooks themselves exist only in
/// builds with local game references.
/// </summary>
internal sealed class PlayerSalesHost
{
    private readonly IModLog _log;
    private readonly ProductCatalogService _catalog;
    private readonly ActiveOrderStore _store;
    private readonly DealerOrderStore _dealerStore;
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
        _dealerStore = new DealerOrderStore(root);
        Menu = menu;
        Coordinator = new PlayerSaleCoordinator(policies, () => _catalog.State.Snapshot, log)
        {
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Dealers = new DealerSaleCoordinator(policies, () => _catalog.State.Snapshot, log)
        {
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Status.Runtime = BuildTarget.Runtime.ToString();
        _catalog.StateChanged += OnCatalogState;
        OnCatalogState(_catalog.State);
    }

    public PlayerSaleCoordinator Coordinator { get; }

    public DealerSaleCoordinator Dealers { get; }

    public PlayerSalesStatus Status { get; } = new();

    public DealerSalesStatus DealerStatus { get; } = new();

    private Func<ModMenuController?> Menu { get; }

    /// <summary>Called by the mod after patch registration.</summary>
    public void SetHooks(bool applied, bool dealersApplied, string? unavailableReason)
    {
        var state = applied ? ComponentStatus.Active : ComponentStatus.Unavailable;
        Status.OfferHook = state;
        Status.HandoverValidation = state;
        Status.TransactionIntegration = state;
        Status.HooksUnavailableReason = applied ? null : unavailableReason;

        // Dealer appointments also need the shared acceptance/settlement hooks of player sales.
        var dealerState = applied && dealersApplied ? ComponentStatus.Active : ComponentStatus.Unavailable;
        DealerStatus.AppointmentHook = dealerState;
        DealerStatus.FulfilmentHook = dealerState;
        DealerStatus.ReconcileHook = dealerState;
        DealerStatus.HooksUnavailableReason = dealerState == ComponentStatus.Active ? null : unavailableReason ?? "not applied";
        RefreshStatus();
        foreach (var line in Status.Lines().Concat(DealerStatus.Lines()))
        {
            _log.Info(line);
        }
    }

    public IReadOnlyList<string> StatusLines()
    {
        RefreshStatus();
        return Status.Lines().Concat(DealerStatus.Lines()).ToList();
    }

    /// <summary>Per frame: one stopwatch read and two version compares unless something changed.</summary>
    public void Tick()
    {
        if (Coordinator.HasUnsavedOrders || Dealers.HasUnsavedChanges)
        {
            Persist(synchronous: false);
        }

        var now = _clock.ElapsedMilliseconds;
        if (now >= _nextStatusRefresh)
        {
            _nextStatusRefresh = now + 5_000;
            var wasEnabled = Status.IsEnabled;
            var headline = Status.Headline;
            var dealerHeadline = DealerStatus.Headline;
            RefreshStatus();
            if (wasEnabled != Status.IsEnabled || headline != Status.Headline)
            {
                _log.Info($"Player Sales: {Status.Headline}.");
            }

            if (dealerHeadline != DealerStatus.Headline)
            {
                _log.Info($"Dealer sales: {DealerStatus.Headline}.");
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
        IEnumerable<DealerOrderRecord>? appointments = null;
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

            var dealerLoaded = SafeLoadDealers(identity);
            appointments = dealerLoaded.Records;
            foreach (var issue in dealerLoaded.Issues)
            {
                _log.Warning($"Dealer appointments for {identity}: {issue}");
            }

            if (dealerLoaded.Records.Count > 0)
            {
                _log.Info($"Restored {dealerLoaded.Records.Count} dealer appointment(s) for {identity}; reservations are rebuilt from the dealers' inventory.");
            }
        }

        Coordinator.BindSave(identity, orders);
        Dealers.BindSave(identity, appointments);
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

    private DealerOrdersLoadResult SafeLoadDealers(string identity)
    {
        try
        {
            return _dealerStore.Load(identity);
        }
        catch (Exception ex)
        {
            _log.Error("Loading dealer appointments failed; they use vanilla rules.", ex);
            return new DealerOrdersLoadResult(OrdersLoadStatus.Unreadable, Array.Empty<DealerOrderRecord>(), Array.Empty<string>());
        }
    }

    private void Persist(bool synchronous)
    {
        var identity = Coordinator.SaveIdentity;
        if (identity is null || (!Coordinator.HasUnsavedOrders && !Dealers.HasUnsavedChanges))
        {
            return;
        }

        // Serialise both files on the main thread (the books are not thread-safe) and write them on a worker.
        // Both are always written together, so skipping an older queued write ("newest wins") never loses one.
        var orders = ActiveOrderStore.ToJson(Coordinator.Book.Orders);
        Coordinator.MarkPersisted();
        string? appointments = null;
        if (string.Equals(Dealers.SaveIdentity, identity, StringComparison.Ordinal))
        {
            appointments = DealerOrderStore.ToJson(Dealers.Records());
            Dealers.MarkPersisted();
        }

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
                    _store.SaveText(identity, orders);
                    if (appointments is not null)
                    {
                        _dealerStore.SaveText(identity, appointments);
                    }
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

        DealerStatus.CatalogReady = Status.CatalogReady;
        DealerStatus.PolicyLoaded = Status.PolicyLoaded;
        DealerStatus.ModEnabledForSave = Status.ModEnabledForSave;
        DealerStatus.Authoritative = Dealers.IsAuthoritative;
        DealerStatus.AuthorityDetail = Dealers.AuthorityDetail;
        DealerStatus.ActiveAppointments = Dealers.ActiveAppointments;
        DealerStatus.WaitingAppointments = Dealers.WaitingAppointments;
        DealerStatus.Reservations = Dealers.Ledger.ActiveCount;
        DealerStatus.ReservedUnits = Dealers.Ledger.HeldUnitsTotal;
        DealerStatus.LastError = Dealers.LastError;
    }
}
