using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>What the offer filter does with one candidate product.</summary>
public enum OfferDecision
{
    /// <summary>The mod does not apply (disabled, settings or catalog not ready): the game's candidate pool is untouched.</summary>
    Vanilla,

    /// <summary>The product may be offered: the live policy permits it for its own category.</summary>
    Offerable,

    /// <summary>The product must not be offered (category Disabled, not selected, locked, unknown).</summary>
    Excluded,
}

/// <summary>
/// Decides which products customers may ask for, before the game picks and prices an offer. It is the same rule
/// as a delivery (<see cref="SaleEligibilityPolicy"/>, live settings, the product's own category): there is no
/// second eligibility engine. Any Selected Only, Disabled or locked rule therefore removes a product from offers,
/// and a Disabled category produces no new offers at all. Pure.
/// </summary>
public static class OfferPolicy
{
    public static OfferDecision Evaluate(PolicyConfiguration? policy, ProductCatalogSnapshot? catalog, string? productId)
        => Evaluate(policy, catalog, productId, SalesScope.Player);

    /// <summary>
    /// Phase 05: the offer is judged with the rules of its path: the player's (global) rules for offers to the player,
    /// the dealer's effective rules (overrides included) for offers to a hired dealer. A path that is switched off
    /// stays vanilla.
    /// </summary>
    public static OfferDecision Evaluate(PolicyConfiguration? policy, ProductCatalogSnapshot? catalog, string? productId, SalesScope scope)
    {
        if (policy is null || !policy.IsPathEnabled(scope) || catalog is null)
        {
            return OfferDecision.Vanilla;
        }

        return Resolved(policy.ForScope(scope), catalog, productId);
    }

    /// <summary>Decision with a policy already resolved for its scope (no switch checks).</summary>
    internal static OfferDecision Resolved(PolicyConfiguration policy, ProductCatalogSnapshot catalog, string? productId)
    {
        if (!ProductKey.TryCreate(productId, out var key) || !catalog.TryGetProduct(key, out var product))
        {
            return OfferDecision.Excluded; // unknown to the catalog: never offered while the mod is on (fail closed)
        }

        return SaleEligibilityPolicy.Evaluate(policy, catalog, product.Category, key).IsEligible
            ? OfferDecision.Offerable
            : OfferDecision.Excluded;
    }
}

/// <summary>
/// The offer filter used by the game hook: gates (authority, settings ready) plus counters for diagnostics.
/// Main-thread only.
/// </summary>
public sealed class OfferFilter
{
    private readonly IPolicyProvider _policies;
    private readonly Func<ProductCatalogSnapshot?> _catalog;

    public OfferFilter(IPolicyProvider policies, Func<ProductCatalogSnapshot?> catalog)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>Whether this session may change offers (single player host). Null means always.</summary>
    public Func<bool>? Authoritative { get; set; }

    /// <summary>Whether the live settings are loaded and trustworthy. Null means always.</summary>
    public Func<bool>? PolicyReady { get; set; }

    public long CandidatesSeen { get; private set; }

    public long CandidatesRemoved { get; private set; }

    /// <summary>Number of candidate pools that ended up empty because of the filter (no offer generated).</summary>
    public long PoolsEmptied { get; private set; }

    /// <summary>
    /// Filters a candidate pool in place through <paramref name="productIdAt"/> / <paramref name="removeAt"/>.
    /// Returns how many candidates were removed (0 when the mod does not apply).
    /// </summary>
    public int Filter(int count, Func<int, string?> productIdAt, Action<int> removeAt) => Filter(count, productIdAt, removeAt, SalesScope.Player);

    /// <summary>Filters a candidate pool for one path (player, or the dealer the offer goes to).</summary>
    public int Filter(int count, Func<int, string?> productIdAt, Action<int> removeAt, SalesScope scope)
    {
        if (count <= 0 || productIdAt is null || removeAt is null || !Applies(out var policy, out var catalog) || !policy.IsPathEnabled(scope))
        {
            return 0;
        }

        policy = policy.ForScope(scope);

        var removed = 0;
        for (var i = count - 1; i >= 0; i--)
        {
            CandidatesSeen++;
            if (OfferPolicy.Resolved(policy, catalog!, productIdAt(i)) == OfferDecision.Excluded)
            {
                removeAt(i);
                removed++;
            }
        }

        CandidatesRemoved += removed;
        if (removed > 0 && removed == count)
        {
            PoolsEmptied++;
        }

        return removed;
    }

    /// <summary>Same decision for one product (diagnostics and tests).</summary>
    public OfferDecision Decide(string? productId) => Decide(productId, SalesScope.Player);

    public OfferDecision Decide(string? productId, SalesScope scope)
        => Applies(out var policy, out var catalog) ? OfferPolicy.Evaluate(policy, catalog, productId, scope) : OfferDecision.Vanilla;

    public IReadOnlyList<string> StatusLines(bool hookActive)
        => new[]
        {
            $"Customer offer filter: {(hookActive ? "Active" : "Unavailable")}",
            $"Offer candidates removed: {CandidatesRemoved} of {CandidatesSeen} ({PoolsEmptied} pool(s) left empty)",
        };

    private bool Applies(out PolicyConfiguration policy, out ProductCatalogSnapshot? catalog)
    {
        policy = _policies.Current;
        catalog = _catalog();
        return (Authoritative is null || Authoritative()) && (PolicyReady is null || PolicyReady()) &&
               policy is not null && policy.Enabled && catalog is not null;
    }
}
