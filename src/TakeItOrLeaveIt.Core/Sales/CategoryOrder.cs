using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>
/// A customer order the mod manages: "deliver <see cref="RequiredUnits"/> units of <see cref="Category"/>,
/// any of <see cref="AllowedProducts"/>". It is the policy snapshot taken when the customer's contract was
/// accepted, so later menu changes never change what an active order accepts. Immutable.
/// </summary>
public sealed class CategoryOrder
{
    public const int MaxContractIdLength = 64;

    private readonly HashSet<ProductKey> _allowed;

    public CategoryOrder(
        string contractId,
        CategoryKey category,
        int requiredUnits,
        IEnumerable<ProductKey> allowedProducts,
        bool mixedDeliveries,
        long policyRevision,
        IEnumerable<ProductKey>? requestedProducts = null)
    {
        if (!IsValidContractId(contractId))
        {
            throw new ArgumentException("Contract id must be a short non-empty token.", nameof(contractId));
        }

        if (!category.IsValid)
        {
            throw new ArgumentException("Category key is invalid.", nameof(category));
        }

        if (requiredUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredUnits), "An order needs at least one unit.");
        }

        var allowed = (allowedProducts ?? throw new ArgumentNullException(nameof(allowedProducts)))
            .Where(p => p.IsValid)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
        if (allowed.Count == 0)
        {
            throw new ArgumentException("An order needs at least one allowed product.", nameof(allowedProducts));
        }

        ContractId = contractId;
        Category = category;
        RequiredUnits = requiredUnits;
        AllowedProducts = allowed;
        _allowed = new HashSet<ProductKey>(allowed);
        MixedDeliveries = mixedDeliveries;
        PolicyRevision = policyRevision;
        RequestedProducts = (requestedProducts ?? Array.Empty<ProductKey>()).Where(p => p.IsValid).Distinct().OrderBy(p => p).ToList();
    }

    /// <summary>The game's contract GUID (text form). Key of the snapshot.</summary>
    public string ContractId { get; }

    public CategoryKey Category { get; }

    /// <summary>Total units the customer expects (sum of the vanilla order lines).</summary>
    public int RequiredUnits { get; }

    /// <summary>Products eligible for this category when the order was accepted, sorted.</summary>
    public IReadOnlyList<ProductKey> AllowedProducts { get; }

    /// <summary>Whether several different allowed products may be combined (setting value at acceptance).</summary>
    public bool MixedDeliveries { get; }

    /// <summary>Revision of the policy configuration the snapshot was taken from (diagnostics only).</summary>
    public long PolicyRevision { get; }

    /// <summary>The exact products vanilla asked for (diagnostics only; never used to validate).</summary>
    public IReadOnlyList<ProductKey> RequestedProducts { get; }

    public bool IsAllowed(ProductKey product) => _allowed.Contains(product);

    /// <summary>
    /// The policy to validate deliveries with: this category in Selected Only mode with exactly the
    /// snapshot's products. Lets <see cref="SaleEligibilityPolicy"/> and the allocation planner judge
    /// deliveries with the acceptance-time rules instead of the live settings.
    /// </summary>
    public PolicyConfiguration ToSnapshotPolicy()
        => PolicyConfiguration.Default
            .WithEnabled(true)
            .WithCategory(Category, new CategoryPolicy(PolicyMode.SelectedOnly, AllowedProducts));

    public static bool IsValidContractId(string? id)
        => !string.IsNullOrWhiteSpace(id) && id!.Length <= MaxContractIdLength &&
           id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    public override string ToString()
        => $"{ContractId}: {RequiredUnits} x {Category} (any of {AllowedProducts.Count} product(s), mixed {(MixedDeliveries ? "on" : "off")}, policy r{PolicyRevision})";
}
