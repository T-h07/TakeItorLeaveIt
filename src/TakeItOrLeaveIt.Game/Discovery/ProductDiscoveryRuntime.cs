using System;
using System.Diagnostics;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Game.Discovery;

/// <summary>
/// Phase 01 runtime glue: polls the save state about once per second (never per-frame scans), feeds the
/// <see cref="SaveLifecycleCoordinator"/>, and logs catalog diagnostics on change.
/// <para><b>Strictly observational.</b> It reads game state only; it installs no Harmony patches and does
/// not touch orders, customers, dealers, inventory, money, XP or unlocks.</para>
/// </summary>
internal sealed class ProductDiscoveryRuntime
{
    private const long PollIntervalMs = 1000;
    private const int MaxConsecutiveErrors = 3;

    private readonly IModLog _log;
    private readonly IPolicyProvider _policies;
    private readonly GameSaveProbe _probe = new();
    private readonly SaveLifecycleCoordinator _coordinator;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _nextPollMs;
    private int _consecutiveErrors;
    private bool _stopped;
    private long _lastLoggedGeneration = -1;

    public ProductDiscoveryRuntime(IModLog log, IPolicyProvider policies)
    {
        _log = log;
        _policies = policies;
        Catalog = new ProductCatalogService(new GameProductDiscoverySource(), log);
        _coordinator = new SaveLifecycleCoordinator(Catalog);
        Catalog.StateChanged += OnStateChanged;
        _log.Info("Product discovery initialized (read-only; polling save state about once per second).");
    }

    public ProductCatalogService Catalog { get; }

    /// <summary>Called from OnUpdate. Cheap when not due: one Stopwatch read.</summary>
    public void Tick()
    {
        if (_stopped || _clock.ElapsedMilliseconds < _nextPollMs)
        {
            return;
        }

        _nextPollMs = _clock.ElapsedMilliseconds + PollIntervalMs;
        Poll();
    }

    /// <summary>Forces an immediate poll (e.g. after a scene load).</summary>
    public void PollNow()
    {
        if (!_stopped)
        {
            _nextPollMs = _clock.ElapsedMilliseconds + PollIntervalMs;
            Poll();
        }
    }

    private void Poll()
    {
        try
        {
            var observation = _probe.Observe();
            var action = _coordinator.Observe(observation);
            if (action == LifecycleAction.BeganSave)
            {
                _log.Info($"Save detected ({observation.SaveIdentity}); building product catalog.");
            }

            _consecutiveErrors = 0;
        }
        catch (Exception ex)
        {
            _consecutiveErrors++;
            _log.Error($"Product discovery poll failed ({_consecutiveErrors}/{MaxConsecutiveErrors}).", ex);
            if (_consecutiveErrors >= MaxConsecutiveErrors)
            {
                _stopped = true;
                Catalog.MarkUnsupported("Discovery stopped after repeated errors.");
                _log.Error("Product discovery stopped after repeated errors. No products are eligible (fail-closed); the game is unaffected.");
            }
        }
    }

    private void OnStateChanged(CatalogState state)
    {
        // Log the full product list once per new catalog generation; other transitions are one line.
        var generation = state.Snapshot?.Generation ?? -1;
        var includeProducts = state.IsReady && generation != _lastLoggedGeneration;
        if (state.IsReady)
        {
            _lastLoggedGeneration = generation;
        }

        foreach (var line in CatalogReport.Describe(state, _policies.Current, includeProducts))
        {
            if (state.Status is CatalogStatus.Failed or CatalogStatus.Unsupported)
            {
                _log.Warning(line);
            }
            else
            {
                _log.Info(line);
            }
        }
    }
}
