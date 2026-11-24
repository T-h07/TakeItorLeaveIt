using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Game;

/// <summary>
/// Owns the sales coordinators for the running game: player sales (Phase 03) and hired-dealer sales (Phase 04).
/// Binds them to the active save session (loading that save's snapshots), serialises them on the main thread when
/// they change and hands the text to <see cref="SalesPersistence"/> (ordered, coalescing writes on one background
/// runner; Phase 06), and builds the status report. Compiled in every build; the game hooks themselves exist only
/// in builds with local game references.
/// </summary>
internal sealed class PlayerSalesHost
{
    private readonly IModLog _log;
    private readonly ProductCatalogService _catalog;
    /// <summary>How long a save switch may wait for pending writes (main thread, loading screen).</summary>
    private const int SwitchFlushMs = 2_000;

    /// <summary>How long quitting may wait for pending writes.</summary>
    private const int ShutdownFlushMs = 3_000;

    private readonly SalesPersistence _files;
    private bool _offersActive;
    private bool _messagingActive;
    private int _runnerActive;
    private long _sessionId;
    private long _nextStatusRefresh;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public PlayerSalesHost(IModLog log, IPolicyProvider policies, ProductCatalogService catalog, string root, Func<ModMenuController?> menu)
    {
        _log = log;
        _catalog = catalog;
        _files = new SalesPersistence(root);
        Menu = menu;
        Coordinator = new PlayerSaleCoordinator(policies, () => _catalog.State.Snapshot, log)
        {
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Dealers = new DealerSaleCoordinator(policies, () => _catalog.State.Snapshot, log)
        {
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Offers = new OfferFilter(policies, () => _catalog.State.Snapshot)
        {
            Authoritative = () => Coordinator.IsAuthoritative,
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Notifier = new DealerRestockNotificationCoordinator(policies, () => _catalog.State.Snapshot, Dealers, log)
        {
            PolicyReady = () => Menu()?.PolicyReadyForSales ?? false,
        };
        Status.Runtime = BuildTarget.Runtime.ToString();
        _catalog.StateChanged += OnCatalogState;
        OnCatalogState(_catalog.State);
    }

    public PlayerSaleCoordinator Coordinator { get; }

    public DealerSaleCoordinator Dealers { get; }

    /// <summary>Customer offer filter (policy applied before the game picks and prices an offer).</summary>
    public OfferFilter Offers { get; }

    /// <summary>Dealer restock messages (dedup state persisted per save).</summary>
    public DealerRestockNotificationCoordinator Notifier { get; }

    public PlayerSalesStatus Status { get; } = new();

    public DealerSalesStatus DealerStatus { get; } = new();

    private Func<ModMenuController?> Menu { get; }

    /// <summary>Called on the main thread whenever a different save session is bound (game-side caches are reset).</summary>
    public Action? SessionChanged { get; set; }

    /// <summary>Called by the mod after patch registration.</summary>
    public void SetHooks(bool applied, bool dealersApplied, bool offersApplied, bool messagingAvailable, string? unavailableReason)
    {
        _offersActive = applied && offersApplied;
        _messagingActive = applied && dealersApplied && messagingAvailable;
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
        foreach (var line in Status.Lines().Concat(DealerStatus.Lines()).Concat(EnhancementLines()))
        {
            _log.Info(line);
        }
    }

    public IReadOnlyList<string> StatusLines()
    {
        RefreshStatus();
        return Status.Lines().Concat(DealerStatus.Lines()).Concat(EnhancementLines()).Concat(_files.StatusLines()).ToList();
    }

    /// <summary>Offer filter, fair pricing, strict dealer fulfilment and restock messages.</summary>
    private IEnumerable<string> EnhancementLines()
    {
        foreach (var line in Offers.StatusLines(_offersActive))
        {
            yield return line;
        }

        var hooks = Status.OfferHook == ComponentStatus.Active;
        yield return hooks
            ? "Fair pricing: Active (substitutes must be worth at least the quoted unit price)"
            : "Fair pricing: Unavailable";
        yield return DealerStatus.FulfilmentHook == ComponentStatus.Active
            ? "Strict dealer fulfilment: On (managed appointments never fall back to the requested product)"
            : "Strict dealer fulfilment: Unavailable";
        foreach (var line in Notifier.StatusLines(_messagingActive))
        {
            yield return line;
        }
    }

    /// <summary>Per frame: one stopwatch read and two version compares unless something changed.</summary>
    public void Tick()
    {
        if (Coordinator.HasUnsavedOrders || Dealers.HasUnsavedChanges || Notifier.HasUnsavedChanges)
        {
            Persist();
        }

        if (_files.Queue.HasDue)
        {
            StartRunner();
        }

        LogQueueMessages();

        var now = _clock.ElapsedMilliseconds;
        if (now >= _nextStatusRefresh)
        {
            _nextStatusRefresh = now + 5_000;
            Notifier.Prune();
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

    /// <summary>Queues the last state and waits (bounded) for every pending write. Never throws.</summary>
    public void Shutdown()
    {
        _catalog.StateChanged -= OnCatalogState;
        try
        {
            Persist();
            if (!_files.Queue.Flush(ShutdownFlushMs))
            {
                _log.Warning($"{_files.Queue.PendingCount} runtime file write(s) could not be completed before quitting; the previous files are intact.");
            }

            LogQueueMessages();
        }
        catch (Exception ex)
        {
            _log.Error("Writing runtime files at shutdown failed; the previous files are intact.", ex);
        }
    }

    private void OnCatalogState(CatalogState state)
    {
        var sessionId = state.Session.IsNone ? 0 : state.Session.Id;
        if (sessionId == _sessionId)
        {
            return;
        }

        _sessionId = sessionId;
        var identity = state.Session.IsNone ? null : state.Session.SaveIdentity;
        var previous = Coordinator.SaveIdentity;

        // 1. Finish the outgoing save: queue its last state and write it before anything of the next save is read.
        Persist();
        if (!_files.Queue.Flush(SwitchFlushMs))
        {
            _log.Warning("Some runtime file writes of the previous save are still pending; they are retried in the background and only ever go to that save's own files.");
        }

        LogQueueMessages();
        try
        {
            SessionChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error("Resetting game-side caches for the new save failed.", ex);
        }

        // 2. Same save again (reload) while its files could not be written: keep the newer in-memory state.
        if (identity is not null && string.Equals(identity, previous, StringComparison.Ordinal) && _files.HasPending(identity))
        {
            _log.Warning($"Runtime files for {identity} still have unwritten changes; the in-memory state is kept instead of re-reading older files.");
            RefreshStatus();
            return;
        }

        // 3. Bind the new save (or none). Old references are dropped by BindSave.
        var loaded = _files.Bind(identity);
        if (loaded is not null)
        {
            foreach (var issue in loaded.Issues)
            {
                _log.Warning($"Save {identity}: {issue}");
            }

            if (loaded.Orders.Count > 0)
            {
                _log.Info($"Restored {loaded.Orders.Count} active category order(s) for {identity}.");
            }

            if (loaded.Appointments.Count > 0)
            {
                _log.Info($"Restored {loaded.Appointments.Count} dealer appointment(s) for {identity}; reservations are rebuilt from the dealers' inventory.");
            }
        }

        Coordinator.BindSave(identity, loaded?.Orders);
        Dealers.BindSave(identity, loaded?.Appointments);
        Notifier.BindSave(identity, loaded?.Alerts);
        RefreshStatus();
    }

    /// <summary>
    /// Serialises the changed books on the main thread (they are not thread-safe) and queues them for the bound save.
    /// Each queued document stays in the queue until written, so marking the books persisted here never loses a change.
    /// </summary>
    private void Persist()
    {
        var identity = Coordinator.SaveIdentity;
        if (identity is null || (!Coordinator.HasUnsavedOrders && !Dealers.HasUnsavedChanges && !Notifier.HasUnsavedChanges))
        {
            return;
        }

        string? orders = null;
        if (Coordinator.HasUnsavedOrders)
        {
            orders = ActiveOrderStore.ToJson(Coordinator.Book.Orders, identity);
            Coordinator.MarkPersisted();
        }

        string? appointments = null;
        if (Dealers.HasUnsavedChanges && string.Equals(Dealers.SaveIdentity, identity, StringComparison.Ordinal))
        {
            appointments = DealerOrderStore.ToJson(Dealers.Records(), identity);
            Dealers.MarkPersisted();
        }

        string? alerts = null;
        if (Notifier.HasUnsavedChanges && string.Equals(Notifier.SaveIdentity, identity, StringComparison.Ordinal))
        {
            alerts = Notifier.State.ToJson(identity);
            Notifier.MarkPersisted();
        }

        if (_files.Submit(identity, orders, appointments, alerts) > 0)
        {
            StartRunner();
        }
    }

    /// <summary>Starts one background runner unless one is active. The runner only writes text to files.</summary>
    private void StartRunner()
    {
        if (Interlocked.CompareExchange(ref _runnerActive, 1, 0) != 0)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                _files.Queue.RunPending();
            }
            catch (Exception)
            {
                // RunPending reports write errors itself; nothing else can throw here
            }
            finally
            {
                Interlocked.Exchange(ref _runnerActive, 0);
            }
        });
    }

    private void LogQueueMessages()
    {
        foreach (var message in _files.Queue.DrainMessages())
        {
            _log.Warning("Runtime files: " + message);
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
