using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>One item stack the player put in the handover slots.</summary>
public sealed class DeliveredItem
{
    public DeliveredItem(string slotId, string? productId, int quantity, int unitsPerItem, bool isPackaged)
    {
        SlotId = slotId;
        ProductId = productId;
        Quantity = quantity;
        UnitsPerItem = unitsPerItem;
        IsPackaged = isPackaged;
    }

    /// <summary>Position in the handover list; unique per delivery.</summary>
    public string SlotId { get; }

    public string? ProductId { get; }

    /// <summary>Stack size (number of packages).</summary>
    public int Quantity { get; }

    /// <summary>Units in one package (the game's packaging amount, e.g. 1 for a baggie, 5 for a jar).</summary>
    public int UnitsPerItem { get; }

    public bool IsPackaged { get; }

    /// <summary>Units this stack contributes; 0 when the numbers are invalid.</summary>
    public long Units => Quantity > 0 && UnitsPerItem > 0 ? (long)Quantity * UnitsPerItem : 0;
}

/// <summary>Why a delivery was refused. Checked in the declared order; the first problem wins.</summary>
public enum DeliveryRejection
{
    None,
    NothingDelivered,
    InvalidItem,
    NotPackaged,
    UnknownProduct,
    WrongCategory,
    ProductNotAllowed,
    ProductLocked,
    MixedNotAllowed,
    CatalogNotReady,
    TooFew,
    TooMany,

    /// <summary>The basket is worth less than the agreed quote at the acceptance prices (fair-price rule).</summary>
    BelowQuotedValue,
}

public sealed class DeliveryValidation
{
    internal DeliveryValidation(
        CategoryOrder order,
        DeliveryRejection rejection,
        long deliveredUnits,
        IReadOnlyList<ProductKey> products,
        AllocationPlan? plan,
        string? offendingSlot,
        string message)
    {
        Order = order;
        Rejection = rejection;
        DeliveredUnits = deliveredUnits;
        Products = products;
        Plan = plan;
        OffendingSlot = offendingSlot;
        Message = message;
    }

    public CategoryOrder Order { get; }

    public bool IsValid => Rejection == DeliveryRejection.None;

    public DeliveryRejection Rejection { get; }

    /// <summary>Sum of units over every delivered stack (valid or not).</summary>
    public long DeliveredUnits { get; }

    /// <summary>Distinct products delivered, sorted.</summary>
    public IReadOnlyList<ProductKey> Products { get; }

    /// <summary>The allocation planner's result (null when validation stopped before planning).</summary>
    public AllocationPlan? Plan { get; }

    public string? OffendingSlot { get; }

    /// <summary>Short player-facing text for the handover screen.</summary>
    public string Message { get; }
}

/// <summary>
/// Validates a handover against a <see cref="CategoryOrder"/> before anything is transferred. Reuses
/// <see cref="SaleEligibilityPolicy"/> (through <see cref="InventoryAllocationPlanner"/>) with the order's
/// acceptance-time snapshot, so the menu's rules and the sale rules can never disagree. Pure and allocation-light.
/// Requires exactly the ordered units: fewer cannot complete the order, more would hand over product the
/// customer did not pay for.
/// </summary>
public static class DeliveryValidator
{
    public static DeliveryValidation Validate(CategoryOrder order, IReadOnlyList<DeliveredItem?>? items, ProductCatalogSnapshot? catalog)
    {
        if (order is null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        var list = items ?? Array.Empty<DeliveredItem?>();
        var products = new List<ProductKey>();
        var stacks = new List<InventoryStack>(list.Count);
        long total = 0;

        if (list.Count == 0)
        {
            return Reject(order, DeliveryRejection.NothingDelivered, 0, products, null, null);
        }

        foreach (var item in list)
        {
            if (item is null || string.IsNullOrEmpty(item.SlotId) || item.Units <= 0 || !ProductKey.TryCreate(item.ProductId, out var key))
            {
                return Reject(order, DeliveryRejection.InvalidItem, total, products, null, item?.SlotId);
            }

            if (!item.IsPackaged)
            {
                return Reject(order, DeliveryRejection.NotPackaged, total, products, null, item.SlotId);
            }

            total += item.Units;
            if (!products.Contains(key))
            {
                products.Add(key);
            }

            stacks.Add(new InventoryStack(item.SlotId, key, (int)Math.Min(item.Units, int.MaxValue)));
        }

        products.Sort();

        if (catalog is null)
        {
            return Reject(order, DeliveryRejection.CatalogNotReady, total, products, null, null);
        }

        // Category, eligibility and lock state of every stack, judged with the snapshot policy.
        var plan = InventoryAllocationPlanner.Plan(order.Category, order.RequiredUnits, stacks, order.ToSnapshotPolicy(), catalog);
        var rejection = plan.Rejections.FirstOrDefault();
        if (rejection is not null)
        {
            return Reject(order, Map(rejection), total, products, plan, rejection.StackId);
        }

        if (!plan.CanFulfill && plan.Failure == AllocationFailure.CategoryNotEligible)
        {
            return Reject(order, plan.CategoryReason == EligibilityReason.CatalogNotReady ? DeliveryRejection.CatalogNotReady : DeliveryRejection.ProductNotAllowed,
                total, products, plan, null);
        }

        if (!order.MixedDeliveries && products.Count > 1)
        {
            return Reject(order, DeliveryRejection.MixedNotAllowed, total, products, plan, null);
        }

        if (total < order.RequiredUnits)
        {
            return Reject(order, DeliveryRejection.TooFew, total, products, plan, null);
        }

        if (total > order.RequiredUnits)
        {
            return Reject(order, DeliveryRejection.TooMany, total, products, plan, null);
        }

        // Defence in depth: allowed products are already priced at or above the quote, so this only refuses a
        // snapshot whose allowed list no longer matches its prices (edited or damaged file).
        if (order.PricingBasis == PricingBasis.Priced)
        {
            var value = FairPriceRule.BasketValue(order, stacks.Select(s => (s.Product, (long)s.Quantity)));
            if (value is null || value < order.QuotedValue)
            {
                return Reject(order, DeliveryRejection.BelowQuotedValue, total, products, plan, null);
            }
        }

        return new DeliveryValidation(order, DeliveryRejection.None, total, products, plan, null, Describe(order, DeliveryRejection.None, total, catalog));

        DeliveryValidation Reject(CategoryOrder o, DeliveryRejection reason, long units, List<ProductKey> seen, AllocationPlan? p, string? slot)
        {
            seen.Sort();
            return new DeliveryValidation(o, reason, units, seen, p, slot, Describe(o, reason, units, catalog));
        }
    }

    private static DeliveryRejection Map(StackRejection rejection) => rejection.Reason switch
    {
        StackRejectionReason.Ineligible => rejection.Eligibility switch
        {
            EligibilityReason.ProductUnknown => DeliveryRejection.UnknownProduct,
            EligibilityReason.WrongCategory => DeliveryRejection.WrongCategory,
            EligibilityReason.ProductLocked or EligibilityReason.UnlockStateUnknown => DeliveryRejection.ProductLocked,
            EligibilityReason.CatalogNotReady => DeliveryRejection.CatalogNotReady,
            _ => DeliveryRejection.ProductNotAllowed,
        },
        _ => DeliveryRejection.InvalidItem,
    };

    /// <summary>Player-facing text. Kept short: the handover screen shows one line.</summary>
    public static string Describe(CategoryOrder order, DeliveryRejection rejection, long deliveredUnits, ProductCatalogSnapshot? catalog)
    {
        var category = CategoryName(order.Category, catalog);
        return rejection switch
        {
            DeliveryRejection.None => $"{deliveredUnits} {category}: accepted.",
            DeliveryRejection.NothingDelivered => $"This customer wants {order.RequiredUnits} {category}.",
            DeliveryRejection.InvalidItem => "One of the items cannot be sold.",
            DeliveryRejection.NotPackaged => "Package the product first.",
            DeliveryRejection.UnknownProduct => "This customer does not recognise one of the products.",
            DeliveryRejection.WrongCategory => $"This customer only wants {category}.",
            DeliveryRejection.ProductNotAllowed => $"One of the products is not allowed for {category} orders (Take It settings).",
            DeliveryRejection.ProductLocked => "One of the products is not unlocked.",
            DeliveryRejection.MixedNotAllowed => "Mixed deliveries are off: use one product only.",
            DeliveryRejection.CatalogNotReady => "Product data is still loading. Try again in a moment.",
            DeliveryRejection.TooFew => $"Needs {order.RequiredUnits} {category}: you offered {deliveredUnits}.",
            DeliveryRejection.TooMany => $"Needs exactly {order.RequiredUnits} {category}: you offered {deliveredUnits}.",
            DeliveryRejection.BelowQuotedValue => "These products are worth less than the agreed price.",
            _ => "Delivery refused.",
        };
    }

    public static string CategoryName(CategoryKey category, ProductCatalogSnapshot? catalog)
        => catalog is not null && catalog.TryGetCategory(category, out var descriptor) ? descriptor.DisplayName : category.Value;
}
