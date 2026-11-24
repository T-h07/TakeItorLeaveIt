using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>How an order's substitutes were bounded by price.</summary>
public enum PricingBasis
{
    /// <summary>Snapshot written before the fair-price rule existed (no price data). See <see cref="CategoryOrder.WithLegacyPricing"/>.</summary>
    Legacy = 0,

    /// <summary>Every allowed product is worth at least the quoted unit price, so every basket is worth at least the quote.</summary>
    Priced = 1,

    /// <summary>A requested product's price was unknown: only the requested products may be delivered (no substitution).</summary>
    RequestedOnly = 2,
}

/// <summary>Result of <see cref="FairPriceRule.Apply"/>.</summary>
public sealed class FairPriceResult
{
    internal FairPriceResult(PricingBasis basis, IReadOnlyList<ProductKey> allowed, decimal? quotedValue, IReadOnlyList<KeyValuePair<ProductKey, decimal>> prices, string detail)
    {
        Basis = basis;
        Allowed = allowed;
        QuotedValue = quotedValue;
        Prices = prices;
        Detail = detail;
    }

    public PricingBasis Basis { get; }

    /// <summary>The eligible products that are fair substitutes (sorted). Empty when none is.</summary>
    public IReadOnlyList<ProductKey> Allowed { get; }

    /// <summary>Value of the order at the game's prices of the requested products (null when unknown).</summary>
    public decimal? QuotedValue { get; }

    /// <summary>The prices the decision used (allowed and requested products), for the snapshot.</summary>
    public IReadOnlyList<KeyValuePair<ProductKey, decimal>> Prices { get; }

    public string Detail { get; }
}

/// <summary>
/// The fixed-quote economy rule. The game prices an offer from the product it chose (quantity × that product's
/// price, then its own adjustments) and pays that fixed amount at completion whatever is delivered. To keep that
/// payment fair, a substitute is allowed only when its own unit price is at least the quoted unit price
/// (<c>Σ requested quantity × price ÷ units</c>). Any basket of such products is then worth at least the quote,
/// so a cheaper product can never be delivered for an expensive quote, and a mixed basket can never be underpaid
/// for the player's stock. Pure; never changes prices, payments, cuts or XP.
/// <para>Fail closed: when a requested product has no known price, substitution is off (requested products only);
/// a substitute with no known price is never allowed.</para>
/// </summary>
public static class FairPriceRule
{
    public static FairPriceResult Apply(
        IReadOnlyList<(ProductKey Product, int Quantity)> requested,
        IEnumerable<ProductKey> eligible,
        IProductPriceSource? prices)
    {
        if (requested is null)
        {
            throw new ArgumentNullException(nameof(requested));
        }

        var eligibleList = (eligible ?? Array.Empty<ProductKey>()).Distinct().OrderBy(p => p).ToList();
        IProductPriceSource table = prices ?? ProductPrices.Empty;
        var requestedKeys = requested.Select(r => r.Product).Distinct().ToList();
        long units = requested.Sum(r => (long)r.Quantity);
        var used = new SortedDictionary<ProductKey, decimal>();

        decimal quoted = 0;
        var missing = new List<ProductKey>();
        foreach (var line in requested)
        {
            if (table.TryGet(line.Product, out var price))
            {
                quoted += price * line.Quantity;
                used[line.Product] = price;
            }
            else
            {
                missing.Add(line.Product);
            }
        }

        if (prices is null || missing.Count > 0 || units <= 0)
        {
            var onlyRequested = eligibleList.Where(requestedKeys.Contains).ToList();
            var why = prices is null
                ? "no price data"
                : $"no price for {string.Join(", ", missing.Distinct())}";
            return new FairPriceResult(PricingBasis.RequestedOnly, onlyRequested, null, used.ToList(),
                $"{why}: requested product(s) only, no substitution");
        }

        var allowed = new List<ProductKey>();
        var tooCheap = 0;
        var unpriced = 0;
        foreach (var product in eligibleList)
        {
            if (!table.TryGet(product, out var price))
            {
                unpriced++;
                continue;
            }

            // price >= quoted / units, without dividing (exact at cent precision).
            if (price * units >= quoted)
            {
                allowed.Add(product);
                used[product] = price;
            }
            else
            {
                tooCheap++;
            }
        }

        var floor = Math.Round(quoted / units, 2, MidpointRounding.AwayFromZero);
        var detail = $"quote {quoted:0.##} ({floor:0.##}/unit): {allowed.Count} fair product(s)"
                     + (tooCheap > 0 ? $", {tooCheap} cheaper excluded" : string.Empty)
                     + (unpriced > 0 ? $", {unpriced} unpriced excluded" : string.Empty);
        return new FairPriceResult(PricingBasis.Priced, allowed, quoted, used.ToList(), detail);
    }

    /// <summary>
    /// Value of a basket at the snapshot prices; null when a product has no price (the basket cannot be proven fair).
    /// </summary>
    public static decimal? BasketValue(CategoryOrder order, IEnumerable<(ProductKey Product, long Units)> basket)
    {
        decimal total = 0;
        foreach (var (product, units) in basket ?? Array.Empty<(ProductKey, long)>())
        {
            if (units <= 0)
            {
                continue;
            }

            if (!order.TryGetUnitPrice(product, out var price))
            {
                return null;
            }

            total += price * units;
        }

        return total;
    }
}
