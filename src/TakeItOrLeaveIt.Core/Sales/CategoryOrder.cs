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
    private readonly Dictionary<ProductKey, decimal> _prices;

    public CategoryOrder(
        string contractId,
        CategoryKey category,
        int requiredUnits,
        IEnumerable<ProductKey> allowedProducts,
        bool mixedDeliveries,
        long policyRevision,
        IEnumerable<ProductKey>? requestedProducts = null,
        PricingBasis pricingBasis = PricingBasis.Legacy,
        decimal? quotedValue = null,
        IEnumerable<KeyValuePair<ProductKey, decimal>>? unitPrices = null)
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
        _prices = new Dictionary<ProductKey, decimal>();
        foreach (var pair in unitPrices ?? Array.Empty<KeyValuePair<ProductKey, decimal>>())
        {
            if (pair.Key.IsValid && pair.Value >= 0 && !_prices.ContainsKey(pair.Key))
            {
                _prices.Add(pair.Key, ProductPrices.Normalize(pair.Value));
            }
        }

        // A priced snapshot without a usable quote, or with an allowed product that is unpriced or cheaper than
        // the quoted unit price, cannot prove fairness: it is downgraded to requested-only (no substitution).
        if (pricingBasis == PricingBasis.Priced &&
            (quotedValue is null || quotedValue < 0 ||
             allowed.Any(p => !_prices.TryGetValue(p, out var price) || price * requiredUnits < quotedValue.Value)))
        {
            pricingBasis = PricingBasis.RequestedOnly;
            quotedValue = null;
        }

        PricingBasis = pricingBasis;
        QuotedValue = pricingBasis == PricingBasis.Priced ? quotedValue : null;
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

    /// <summary>How substitutes were bounded by price at acceptance (see <see cref="FairPriceRule"/>).</summary>
    public PricingBasis PricingBasis { get; }

    /// <summary>Value of the order at the requested products' prices at acceptance (only for <see cref="PricingBasis.Priced"/>).</summary>
    public decimal? QuotedValue { get; }

    /// <summary>The unit prices recorded at acceptance (allowed and requested products), sorted by product.</summary>
    public IReadOnlyList<KeyValuePair<ProductKey, decimal>> UnitPrices => _prices.OrderBy(p => p.Key).ToList();

    public bool IsAllowed(ProductKey product) => _allowed.Contains(product);

    public bool TryGetUnitPrice(ProductKey product, out decimal price) => _prices.TryGetValue(product, out price);

    /// <summary>
    /// Compatibility path for a snapshot saved before the fair-price rule (no prices). Its allowed products are
    /// narrowed to the requested ones it already allowed, so an old contract can never be filled with a cheaper
    /// product. When it allowed none of the requested products the snapshot is kept unchanged (it could not be
    /// filled otherwise) and stays marked <see cref="PricingBasis.Legacy"/> in diagnostics. Returns this order when
    /// nothing changes.
    /// </summary>
    public CategoryOrder WithLegacyPricing()
    {
        if (PricingBasis != PricingBasis.Legacy)
        {
            return this;
        }

        var requested = AllowedProducts.Where(p => RequestedProducts.Contains(p)).ToList();
        return requested.Count == 0
            ? this
            : new CategoryOrder(ContractId, Category, RequiredUnits, requested, MixedDeliveries, PolicyRevision, RequestedProducts,
                PricingBasis.RequestedOnly);
    }

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
        => $"{ContractId}: {RequiredUnits} x {Category} (any of {AllowedProducts.Count} product(s), mixed {(MixedDeliveries ? "on" : "off")}, policy r{PolicyRevision}, {PricingText})";

    private string PricingText => PricingBasis switch
    {
        PricingBasis.Priced => $"quote {QuotedValue:0.##}",
        PricingBasis.RequestedOnly => "requested products only",
        _ => "legacy snapshot",
    };
}
