using System;
using System.Security.Cryptography;
using System.Text;

namespace TakeItOrLeaveIt.Core.Discovery;

/// <summary>One read of the game's save state, taken by a game adapter. Pure data.</summary>
public readonly struct SaveObservation
{
    private SaveObservation(bool supported, bool isLoaded, bool isLoading, string? saveIdentity, int? productCount, string? unsupportedReason)
    {
        IsSupported = supported;
        IsLoaded = isLoaded;
        IsLoading = isLoading;
        SaveIdentity = string.IsNullOrWhiteSpace(saveIdentity) ? null : saveIdentity;
        ProductCount = productCount;
        UnsupportedReason = unsupportedReason;
    }

    /// <summary>False when the game build lacks the members needed to observe saves.</summary>
    public bool IsSupported { get; }

    public bool IsLoaded { get; }

    public bool IsLoading { get; }

    /// <summary>Anonymised save identity (see <see cref="SaveIdentity.FromFolderPath"/>), or null if unknown.</summary>
    public string? SaveIdentity { get; }

    /// <summary>Cheap change hint (e.g. number of discovered products); null when not read.</summary>
    public int? ProductCount { get; }

    public string? UnsupportedReason { get; }

    public static SaveObservation Of(bool isLoaded, bool isLoading, string? saveIdentity, int? productCount = null)
        => new(true, isLoaded, isLoading, saveIdentity, productCount, null);

    public static SaveObservation NoGame() => new(true, false, false, null, null, null);

    public static SaveObservation Unsupported(string reason) => new(false, false, false, null, null, reason);
}

/// <summary>What <see cref="SaveLifecycleCoordinator.Observe"/> did.</summary>
public enum LifecycleAction
{
    None,
    BeganSave,
    Refreshed,
    EndedSave,
    MarkedUnsupported,
}

/// <summary>
/// Turns periodic <see cref="SaveObservation"/>s into catalog lifecycle calls. Pure and deterministic,
/// so the save-transition rules are unit-tested without the game:
/// <list type="bullet">
/// <item>no save / no identity → <see cref="ProductCatalogService.EndSave"/>;</item>
/// <item>a loading edge or a different save identity → new session (<see cref="ProductCatalogService.BeginSave"/>);</item>
/// <item>discovery runs only once loading has finished, and again when the product-count hint changes;</item>
/// <item>unsupported game data → <see cref="ProductCatalogService.MarkUnsupported"/> (fail closed).</item>
/// </list>
/// </summary>
public sealed class SaveLifecycleCoordinator
{
    private readonly ProductCatalogService _service;
    private bool _wasLoading;
    private int? _lastProductCount;

    public SaveLifecycleCoordinator(ProductCatalogService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public LifecycleAction Observe(SaveObservation observation)
    {
        var state = _service.State;

        if (!observation.IsSupported)
        {
            _wasLoading = false;
            if (state.Status == CatalogStatus.Unsupported)
            {
                return LifecycleAction.None;
            }

            _service.MarkUnsupported(observation.UnsupportedReason ?? "Save state cannot be observed in this game build.");
            return LifecycleAction.MarkedUnsupported;
        }

        var inSave = observation.IsLoaded || observation.IsLoading;
        if (!inSave || observation.SaveIdentity is null)
        {
            _wasLoading = observation.IsLoading;
            _lastProductCount = null;
            if (state.Status == CatalogStatus.NoSave)
            {
                return LifecycleAction.None;
            }

            _service.EndSave();
            return LifecycleAction.EndedSave;
        }

        var loadingEdge = observation.IsLoading && !_wasLoading;
        _wasLoading = observation.IsLoading;
        var differentSave = state.Session.IsNone || !string.Equals(state.Session.SaveIdentity, observation.SaveIdentity, StringComparison.Ordinal);

        if (loadingEdge || differentSave)
        {
            var session = _service.BeginSave(observation.SaveIdentity);
            _lastProductCount = null;
            if (!observation.IsLoading)
            {
                RefreshNow(session, observation.ProductCount);
            }

            return LifecycleAction.BeganSave;
        }

        if (observation.IsLoading)
        {
            return LifecycleAction.None; // same save still loading: wait until it finishes
        }

        var finishedLoading = state.Status == CatalogStatus.Loading;
        var countChanged = observation.ProductCount.HasValue && observation.ProductCount != _lastProductCount;
        if (state.Status == CatalogStatus.Unsupported || (!finishedLoading && !countChanged))
        {
            return LifecycleAction.None;
        }

        RefreshNow(state.Session, observation.ProductCount);
        return LifecycleAction.Refreshed;
    }

    private void RefreshNow(SaveSession session, int? productCount)
    {
        _lastProductCount = productCount;
        _service.Refresh(session);
    }
}

/// <summary>Turns a save folder path into a stable, anonymised identity. Raw paths are never logged or stored.</summary>
public static class SaveIdentity
{
    /// <summary>Returns e.g. <c>save-3fa2c19b04de</c>, or null when the path is missing.</summary>
    public static string? FromFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        // Windows paths are case-insensitive and may use either separator.
        var normalized = folderPath!.Trim().Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var sb = new StringBuilder("save-", 17);
        for (var i = 0; i < 6; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        return sb.ToString();
    }
}
