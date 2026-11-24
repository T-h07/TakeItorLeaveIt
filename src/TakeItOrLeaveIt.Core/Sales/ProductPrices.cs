using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>The game's unit price of a product, when known. Implementations must be side-effect free.</summary>
public interface IProductPriceSource
{
    bool TryGet(ProductKey product, out decimal price);
}

/// <summary>Adapts a lookup function (the game adapter's price read) to <see cref="IProductPriceSource"/>.</summary>
public sealed class PriceLookup : IProductPriceSource
{
    private readonly Func<ProductKey, decimal?> _lookup;

    public PriceLookup(Func<ProductKey, decimal?> lookup) => _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));

    public bool TryGet(ProductKey product, out decimal price)
    {
        var value = product.IsValid ? _lookup(product) : null;
        if (value is decimal v && v >= 0)
        {
            price = ProductPrices.Normalize(v);
            return true;
        }

        price = 0;
        return false;
    }
}

/// <summary>
/// Unit prices read from the game when an order is accepted (the game's own price for each product, e.g. the
/// player's listing price). Immutable. Invalid values (negative, NaN, infinite) are dropped, so an unknown price
/// is always "missing", never zero. The mod never sets or changes prices: they only bound which substitutes are fair.
/// </summary>
public sealed class ProductPrices : IProductPriceSource
{
    public static readonly ProductPrices Empty = new(Array.Empty<KeyValuePair<ProductKey, decimal>>());

    private readonly Dictionary<ProductKey, decimal> _prices;

    public ProductPrices(IEnumerable<KeyValuePair<ProductKey, decimal>>? prices)
    {
        _prices = new Dictionary<ProductKey, decimal>();
        foreach (var pair in prices ?? Array.Empty<KeyValuePair<ProductKey, decimal>>())
        {
            if (pair.Key.IsValid && pair.Value >= 0 && !_prices.ContainsKey(pair.Key))
            {
                _prices.Add(pair.Key, Normalize(pair.Value));
            }
        }
    }

    public int Count => _prices.Count;

    public IEnumerable<KeyValuePair<ProductKey, decimal>> Entries => _prices.OrderBy(p => p.Key);

    /// <summary>Builds a table from game floats; NaN, infinite and negative values are skipped.</summary>
    public static ProductPrices FromFloats(IEnumerable<KeyValuePair<ProductKey, float>>? prices)
        => new((prices ?? Array.Empty<KeyValuePair<ProductKey, float>>())
            .Where(p => !float.IsNaN(p.Value) && !float.IsInfinity(p.Value) && p.Value >= 0 && p.Value < 1e9f)
            .Select(p => new KeyValuePair<ProductKey, decimal>(p.Key, (decimal)p.Value)));

    public bool TryGet(ProductKey product, out decimal price) => _prices.TryGetValue(product, out price);

    /// <summary>Prices are compared at cent precision so float noise never decides fairness.</summary>
    public static decimal Normalize(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
