using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>One line of the vanilla contract: an exact product id and a unit count.</summary>
public sealed class OrderLine
{
    public OrderLine(string? productId, int quantity)
    {
        ProductId = productId;
        Quantity = quantity;
    }

    public string? ProductId { get; }

    public int Quantity { get; }
}

/// <summary>What the game knows about a contract at acceptance.</summary>
public sealed class OrderRequest
{
    public OrderRequest(string? contractId, bool isPlayerContract, IEnumerable<OrderLine?>? lines)
    {
        ContractId = contractId;
        IsPlayerContract = isPlayerContract;
        Lines = (lines ?? Array.Empty<OrderLine?>()).ToList();
    }

    public string? ContractId { get; }

    /// <summary>False for contracts handled by a dealer: those are never managed (dealers stay vanilla).</summary>
    public bool IsPlayerContract { get; }

    public IReadOnlyList<OrderLine?> Lines { get; }
}

/// <summary>Why a contract is or is not managed by the mod. Every value except <see cref="Managed"/> means "vanilla rules".</summary>
public enum OrderOutcome
{
    Managed,
    NotPlayerContract,
    ModDisabled,
    PolicyNotLoaded,
    CatalogNotReady,
    InvalidContract,
    UnknownProduct,
    MixedCategories,
    CategoryNotEligible,
}

public sealed class OrderCreation
{
    internal OrderCreation(OrderOutcome outcome, CategoryOrder? order, string detail)
    {
        Outcome = outcome;
        Order = order;
        Detail = detail;
    }

    public OrderOutcome Outcome { get; }

    /// <summary>The snapshot, only when <see cref="Outcome"/> is <see cref="OrderOutcome.Managed"/>.</summary>
    public CategoryOrder? Order { get; }

    public string Detail { get; }

    public bool IsManaged => Outcome == OrderOutcome.Managed;
}

/// <summary>
/// Decides at contract acceptance whether a player contract becomes a category order, and takes the
/// policy snapshot. Pure: the same inputs always give the same result. Anything unexpected leaves the
/// contract vanilla (fail closed).
/// </summary>
public static class CategoryOrderFactory
{
    public static OrderCreation Create(OrderRequest? request, PolicyConfiguration? policy, ProductCatalogSnapshot? catalog)
        => Build(request, policy, catalog, forDealer: false);

    /// <summary>
    /// The same snapshot for a contract a hired dealer will fulfil (Phase 04). Identical rules: one category,
    /// the live policy's eligible products, the mixed-deliveries setting. Player contracts are refused here.
    /// </summary>
    public static OrderCreation CreateForDealer(OrderRequest? request, PolicyConfiguration? policy, ProductCatalogSnapshot? catalog)
        => Build(request, policy, catalog, forDealer: true);

    private static OrderCreation Build(OrderRequest? request, PolicyConfiguration? policy, ProductCatalogSnapshot? catalog, bool forDealer)
    {
        if (request is null || !CategoryOrder.IsValidContractId(request.ContractId))
        {
            return Vanilla(OrderOutcome.InvalidContract, "Contract has no usable id.");
        }

        if (request.IsPlayerContract == forDealer)
        {
            return forDealer
                ? Vanilla(OrderOutcome.InvalidContract, "Player contract: not a dealer order.")
                : Vanilla(OrderOutcome.NotPlayerContract, "Dealer contract: handled by dealer sales.");
        }

        if (policy is null || !policy.Enabled)
        {
            return Vanilla(OrderOutcome.ModDisabled, "Mod disabled for this save: vanilla rules.");
        }

        if (catalog is null)
        {
            return Vanilla(OrderOutcome.CatalogNotReady, "Product catalog not ready: vanilla rules.");
        }

        if (request.Lines.Count == 0)
        {
            return Vanilla(OrderOutcome.InvalidContract, "Contract has no product lines.");
        }

        long units = 0;
        CategoryKey? category = null;
        var requested = new List<ProductKey>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.Quantity <= 0 || !ProductKey.TryCreate(line.ProductId, out var key))
            {
                return Vanilla(OrderOutcome.InvalidContract, "Contract has an invalid product line.");
            }

            if (!catalog.TryGetProduct(key, out var product))
            {
                return Vanilla(OrderOutcome.UnknownProduct, $"Requested product '{key}' is not in the catalog.");
            }

            if (category is not null && category.Value != product.Category)
            {
                return Vanilla(OrderOutcome.MixedCategories, $"Contract asks for {category} and {product.Category}: vanilla rules.");
            }

            category = product.Category;
            requested.Add(key);
            units += line.Quantity;
        }

        if (units > int.MaxValue)
        {
            return Vanilla(OrderOutcome.InvalidContract, "Contract quantity is out of range.");
        }

        var eligibility = SaleEligibilityPolicy.EvaluateCategory(policy, catalog, category!.Value);
        if (!eligibility.HasEligibleProducts)
        {
            return Vanilla(OrderOutcome.CategoryNotEligible, $"{category}: {eligibility.Reason}; vanilla rules.");
        }

        var order = new CategoryOrder(
            request.ContractId!,
            category.Value,
            (int)units,
            eligibility.EligibleProducts.Select(p => p.Key),
            policy.MixedDeliveries,
            policy.Revision,
            requested);
        return new OrderCreation(OrderOutcome.Managed, order, order.ToString());
    }

    private static OrderCreation Vanilla(OrderOutcome outcome, string detail) => new(outcome, null, detail);
}
