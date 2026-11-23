using System;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Core.Discovery;

/// <summary>
/// Save-aware owner of the product catalog ("product discovery service").
/// <list type="bullet">
/// <item>Each <see cref="BeginSave"/> starts a new session and drops the previous save's catalog, so
/// unlock lists never merge across saves.</item>
/// <item>A result is only published if it belongs to the current session; results from an older
/// session are discarded (<see cref="RefreshOutcome.StaleDiscarded"/>).</item>
/// <item>A failed refresh moves to <see cref="CatalogStatus.Failed"/> with no snapshot. Stale products
/// are never presented as current, and failure never looks like an empty catalog.</item>
/// </list>
/// State changes are atomic (single immutable <see cref="CatalogState"/> reference).
/// </summary>
public sealed class ProductCatalogService
{
    private readonly IProductDiscoverySource _source;
    private readonly IModLog? _log;
    private readonly object _gate = new();
    private CatalogState _state = new(CatalogStatus.NoSave, default, null, null);
    private long _lastSessionId;
    private long _lastGeneration;

    public ProductCatalogService(IProductDiscoverySource source, IModLog? log = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _log = log;
    }

    /// <summary>Raised after every state change, with the new state.</summary>
    public event Action<CatalogState>? StateChanged;

    public CatalogState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Starts a new save session (status Loading). Any previous save's catalog is discarded.</summary>
    public SaveSession BeginSave(string saveIdentity)
    {
        SaveSession session;
        lock (_gate)
        {
            session = new SaveSession(++_lastSessionId, saveIdentity);
            _state = new CatalogState(CatalogStatus.Loading, session, null, null);
        }

        Raise();
        return session;
    }

    /// <summary>Ends the active session (e.g. returning to the main menu). Status becomes NoSave.</summary>
    public void EndSave()
    {
        lock (_gate)
        {
            if (_state.Status == CatalogStatus.NoSave)
            {
                return;
            }

            _state = new CatalogState(CatalogStatus.NoSave, default, null, null);
        }

        Raise();
    }

    /// <summary>Marks the active session unsupported (game build lacks required data).</summary>
    public void MarkUnsupported(string reason)
    {
        lock (_gate)
        {
            _state = new CatalogState(CatalogStatus.Unsupported, _state.Session, null, reason);
        }

        Raise();
    }

    /// <summary>Runs discovery for <paramref name="session"/> and publishes the result if still current.</summary>
    public RefreshOutcome Refresh(SaveSession session)
    {
        if (session.IsNone)
        {
            return RefreshOutcome.NoActiveSave;
        }

        DiscoveryResult result;
        try
        {
            result = _source.Discover() ?? DiscoveryResult.Failure("Discovery source returned no result.");
        }
        catch (Exception ex)
        {
            _log?.Error("Product discovery threw an exception.", ex);
            result = DiscoveryResult.Failure($"Discovery threw {ex.GetType().Name}: {ex.Message}");
        }

        return Publish(session, result);
    }

    /// <summary>
    /// Applies a discovery result captured for <paramref name="session"/>. Separate from
    /// <see cref="Refresh"/> so that results captured before a save switch can be proven to be discarded.
    /// </summary>
    public RefreshOutcome Publish(SaveSession session, DiscoveryResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        RefreshOutcome outcome;
        lock (_gate)
        {
            if (session.IsNone || _state.Status == CatalogStatus.NoSave)
            {
                return RefreshOutcome.NoActiveSave;
            }

            if (!session.Equals(_state.Session))
            {
                outcome = RefreshOutcome.StaleDiscarded;
            }
            else if (!result.Succeeded)
            {
                var status = result.IsUnsupported ? CatalogStatus.Unsupported : CatalogStatus.Failed;
                _state = new CatalogState(status, session, null, result.FailureReason);
                outcome = RefreshOutcome.Failed;
            }
            else
            {
                var snapshot = ProductCatalogSnapshot.Build(session, ++_lastGeneration, result.Categories, result.Products, result.Issues);
                _state = new CatalogState(CatalogStatus.Ready, session, snapshot, null);
                outcome = RefreshOutcome.Published;
            }
        }

        if (outcome == RefreshOutcome.StaleDiscarded)
        {
            _log?.Warning($"Discarded a catalog refresh for an inactive save ({session}).");
            return outcome;
        }

        Raise();
        return outcome;
    }

    private void Raise()
    {
        var state = State;
        try
        {
            StateChanged?.Invoke(state);
        }
        catch (Exception ex)
        {
            // A faulty subscriber must not break the catalog lifecycle.
            _log?.Error("A catalog StateChanged handler threw.", ex);
        }
    }
}
