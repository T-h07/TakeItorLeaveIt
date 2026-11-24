using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>
/// Pure stock arithmetic for restock messages. Eligibility always comes from <see cref="SaleEligibilityPolicy"/>
/// (live settings for "can the dealer sell this category at all", the appointment's snapshot for "can this
/// appointment be filled"), and availability from the <see cref="ReservationLedger"/>: no second rule set.
/// </summary>
public static class DealerStockMonitor
{
    /// <summary>
    /// Permitted units per category under the live settings. Only categories that currently have eligible products
    /// appear (a Disabled category, or one whose selection is empty or locked, is never reported as "out").
    /// Phase 05: pass the policy resolved for the dealer (<see cref="PolicyConfiguration.ForScope"/>); only stock at or
    /// above the category's quality minimum counts.
    /// </summary>
    public static IReadOnlyDictionary<CategoryKey, long> EligibleUnitsByCategory(DealerStock stock, PolicyConfiguration? policy, ProductCatalogSnapshot? catalog)
    {
        var result = new Dictionary<CategoryKey, long>();
        if (stock is null || policy is null || !policy.Enabled || catalog is null)
        {
            return result;
        }

        foreach (var category in catalog.Categories)
        {
            var eligibility = SaleEligibilityPolicy.EvaluateCategory(policy, catalog, category.Key);
            if (!eligibility.HasEligibleProducts)
            {
                continue;
            }

            long units = 0;
            var minimum = policy.GetEffectivePolicy(category.Key).MinimumQuality;
            foreach (var product in eligibility.EligibleProducts)
            {
                units += stock.UnitsOf(product.Key, minimum);
            }

            result[category.Key] = units;
        }

        return result;
    }

    /// <summary>
    /// Units the dealer could use for this appointment right now: its snapshot's products at or above its quality
    /// minimum, minus what other appointments hold. With limited mixing: the best single combination group (one
    /// product, or one base strain), because an order is never split across groups then.
    /// </summary>
    public static long AvailableFor(DealerAppointment appointment, DealerStock stock, ReservationLedger ledger)
    {
        if (appointment is null || stock is null || ledger is null)
        {
            return 0;
        }

        var order = appointment.Order;
        var perProduct = order.AllowedProducts
            .Select(p => (Product: p, Units: ledger.Available(stock, p, order.MinimumQuality, appointment.ContractId)))
            .ToList();
        if (perProduct.Count == 0)
        {
            return 0;
        }

        if (order.MixedDeliveries)
        {
            return perProduct.Sum(p => p.Units);
        }

        return perProduct
            .SelectMany(p => order.GroupsOf(p.Product).Select(g => (Group: g, p.Units)))
            .GroupBy(x => x.Group, StringComparer.Ordinal)
            .Select(g => g.Sum(x => x.Units))
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>max(0, required − available).</summary>
    public static long Shortfall(long required, long available) => Math.Max(0, required - Math.Max(0, available));
}
