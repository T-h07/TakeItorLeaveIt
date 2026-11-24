using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Inventory;

/// <summary>
/// Read-only description of one physical inventory stack, supplied by a game adapter.
/// <see cref="StackId"/> must uniquely and stably identify the stack within one planning call
/// (e.g. "player:slot:3", "dealer:&lt;id&gt;:slot:1").
/// </summary>
public sealed class InventoryStack
{
    public InventoryStack(string stackId, ProductKey product, int quantity, QualityTier? quality = null)
    {
        StackId = stackId;
        Product = product;
        Quantity = quantity;
        Quality = quality;
    }

    public string StackId { get; }

    public ProductKey Product { get; }

    public int Quantity { get; }

    /// <summary>The stack's quality tier (Phase 05); null when unknown or not relevant.</summary>
    public QualityTier? Quality { get; }

    public override string ToString() => Quality is null ? $"{StackId}: {Quantity} x {Product}" : $"{StackId}: {Quantity} x {Product} ({Quality})";
}

/// <summary>A proposed draw from one stack.</summary>
public sealed class StackAllocation
{
    internal StackAllocation(string stackId, ProductKey product, int quantity, QualityTier? quality = null)
    {
        StackId = stackId;
        Product = product;
        Quantity = quantity;
        Quality = quality;
    }

    /// <summary>Quality of the stack drawn from (Phase 05); null when unknown.</summary>
    public QualityTier? Quality { get; }

    public string StackId { get; }

    public ProductKey Product { get; }

    public int Quantity { get; }

    public override string ToString() => $"{Quantity} x {Product} from {StackId}";
}

public enum StackRejectionReason
{
    /// <summary>Null stack or missing/blank stack id.</summary>
    InvalidStack,

    /// <summary>The same stack id appeared more than once; every occurrence is ignored so nothing is double-counted.</summary>
    DuplicateStackId,

    /// <summary>Quantity was zero or negative.</summary>
    InvalidQuantity,

    /// <summary>The product is not eligible; see <see cref="StackRejection.Eligibility"/>.</summary>
    Ineligible,
}

/// <summary>Why a stack did not contribute to the plan.</summary>
public sealed class StackRejection
{
    internal StackRejection(string? stackId, StackRejectionReason reason, EligibilityReason? eligibility)
    {
        StackId = stackId;
        Reason = reason;
        Eligibility = eligibility;
    }

    public string? StackId { get; }

    public StackRejectionReason Reason { get; }

    /// <summary>Set when <see cref="Reason"/> is <see cref="StackRejectionReason.Ineligible"/>.</summary>
    public EligibilityReason? Eligibility { get; }

    public override string ToString() => Eligibility is null ? $"{StackId}: {Reason}" : $"{StackId}: {Reason} ({Eligibility})";
}

/// <summary>Why a plan cannot fulfil the order.</summary>
public enum AllocationFailure
{
    None = 0,
    InvalidQuantity,

    /// <summary>The category itself rejects the order; see <see cref="AllocationPlan.CategoryReason"/>.</summary>
    CategoryNotEligible,

    /// <summary>No eligible units at all.</summary>
    NoEligibleStock,

    /// <summary>Some eligible units, but fewer than requested.</summary>
    InsufficientEligibleQuantity,
}

/// <summary>
/// A proposed fulfilment. Nothing is reserved or consumed; integrations must revalidate against
/// live inventory, <see cref="CatalogGeneration"/> and <see cref="PolicyRevision"/> before acting.
/// </summary>
public sealed class AllocationPlan
{
    internal AllocationPlan(
        CategoryKey category,
        int requestedQuantity,
        AllocationFailure failure,
        EligibilityReason categoryReason,
        long eligibleQuantityAvailable,
        IReadOnlyList<StackAllocation> allocations,
        IReadOnlyList<StackRejection> rejections,
        long? catalogGeneration,
        long policyRevision)
    {
        Category = category;
        RequestedQuantity = requestedQuantity;
        Failure = failure;
        CategoryReason = categoryReason;
        EligibleQuantityAvailable = eligibleQuantityAvailable;
        Allocations = allocations;
        Rejections = rejections;
        CatalogGeneration = catalogGeneration;
        PolicyRevision = policyRevision;
    }

    public bool CanFulfill => Failure == AllocationFailure.None;

    public CategoryKey Category { get; }

    public int RequestedQuantity { get; }

    public AllocationFailure Failure { get; }

    /// <summary>Category-level eligibility (Allowed unless the category itself blocks the order).</summary>
    public EligibilityReason CategoryReason { get; }

    /// <summary>Total eligible units across accepted stacks (64-bit, so many large stacks cannot overflow).</summary>
    public long EligibleQuantityAvailable { get; }

    /// <summary>Proposed draws (only when <see cref="CanFulfill"/>); they sum exactly to <see cref="RequestedQuantity"/>.</summary>
    public IReadOnlyList<StackAllocation> Allocations { get; }

    public IReadOnlyList<StackRejection> Rejections { get; }

    public long? CatalogGeneration { get; }

    public long PolicyRevision { get; }

    public int AllocatedQuantity
    {
        get
        {
            var total = 0;
            foreach (var a in Allocations)
            {
                total = checked(total + a.Quantity);
            }

            return total;
        }
    }
}
