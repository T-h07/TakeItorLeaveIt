using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Discovery;

/// <summary>Identifies one loaded save for the lifetime of the session (not persisted).</summary>
public readonly struct SaveSession : IEquatable<SaveSession>
{
    public SaveSession(long id, string saveIdentity)
    {
        Id = id;
        SaveIdentity = saveIdentity ?? string.Empty;
    }

    /// <summary>Unique, increasing per save load within this game run. 0 = no session.</summary>
    public long Id { get; }

    /// <summary>Opaque identity of the save (e.g. a hash of its folder). Never a raw path in logs.</summary>
    public string SaveIdentity { get; }

    public bool IsNone => Id == 0;

    public bool Equals(SaveSession other) => Id == other.Id;

    public override bool Equals(object? obj) => obj is SaveSession other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString() => IsNone ? "<no save>" : $"session {Id} (save {SaveIdentity})";
}

/// <summary>Lifecycle status of the product catalog.</summary>
public enum CatalogStatus
{
    /// <summary>No save is loaded (main menu / startup).</summary>
    NoSave = 0,

    /// <summary>A save is loading or discovery has not completed yet. An empty list here means nothing.</summary>
    Loading = 1,

    /// <summary>A complete snapshot for the active save is available.</summary>
    Ready = 2,

    /// <summary>Discovery failed for the active save; no products are eligible.</summary>
    Failed = 3,

    /// <summary>The installed game does not expose the data discovery needs.</summary>
    Unsupported = 4,
}

/// <summary>Immutable view of the catalog lifecycle. <see cref="Snapshot"/> is non-null only when Ready.</summary>
public sealed class CatalogState
{
    internal CatalogState(CatalogStatus status, SaveSession session, ProductCatalogSnapshot? snapshot, string? message)
    {
        Status = status;
        Session = session;
        Snapshot = status == CatalogStatus.Ready ? snapshot : null;
        Message = message;
    }

    public CatalogStatus Status { get; }

    public SaveSession Session { get; }

    public ProductCatalogSnapshot? Snapshot { get; }

    /// <summary>Human-readable detail for diagnostics (failure reason etc.). Never used for decisions.</summary>
    public string? Message { get; }

    public bool IsReady => Status == CatalogStatus.Ready;

    public override string ToString() => Message is null ? $"{Status} ({Session})" : $"{Status} ({Session}): {Message}";
}

/// <summary>Output of one discovery pass from a game adapter (or a test fixture).</summary>
public sealed class DiscoveryResult
{
    private DiscoveryResult(
        bool succeeded,
        bool unsupported,
        IReadOnlyList<CategoryDescriptor> categories,
        IReadOnlyList<ProductDescriptor> products,
        IReadOnlyList<string> issues,
        string? failureReason)
    {
        Succeeded = succeeded;
        IsUnsupported = unsupported;
        Categories = categories;
        Products = products;
        Issues = issues;
        FailureReason = failureReason;
    }

    public bool Succeeded { get; }

    /// <summary>True when the failure is caused by an unsupported/changed game build rather than a transient error.</summary>
    public bool IsUnsupported { get; }

    public IReadOnlyList<CategoryDescriptor> Categories { get; }

    public IReadOnlyList<ProductDescriptor> Products { get; }

    public IReadOnlyList<string> Issues { get; }

    public string? FailureReason { get; }

    public static DiscoveryResult Success(
        IReadOnlyList<CategoryDescriptor> categories,
        IReadOnlyList<ProductDescriptor> products,
        IReadOnlyList<string>? issues = null)
        => new(true, false,
            categories ?? throw new ArgumentNullException(nameof(categories)),
            products ?? throw new ArgumentNullException(nameof(products)),
            issues ?? Array.Empty<string>(),
            null);

    public static DiscoveryResult Failure(string reason, bool unsupported = false)
        => new(false, unsupported, Array.Empty<CategoryDescriptor>(), Array.Empty<ProductDescriptor>(), Array.Empty<string>(),
            string.IsNullOrWhiteSpace(reason) ? "Unspecified discovery failure." : reason);
}

/// <summary>
/// Reads categories and products for the currently loaded save. Implemented by the game adapter;
/// must be read-only (never unlock, grant or modify anything) and called on the thread that owns the
/// game objects (Unity main thread).
/// </summary>
public interface IProductDiscoverySource
{
    DiscoveryResult Discover();
}

/// <summary>Outcome of <see cref="ProductCatalogService.Refresh"/> / <see cref="ProductCatalogService.Publish"/>.</summary>
public enum RefreshOutcome
{
    Published = 0,
    Failed = 1,

    /// <summary>The result belonged to a save session that is no longer active; it was discarded.</summary>
    StaleDiscarded = 2,

    /// <summary>No save is active; nothing to refresh.</summary>
    NoActiveSave = 3,
}
