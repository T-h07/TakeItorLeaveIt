using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>Units of one product at one quality tier (null: quality unknown). The unit of dealer reservations (Phase 05).</summary>
public readonly struct StockLot : IEquatable<StockLot>, IComparable<StockLot>
{
    public StockLot(ProductKey product, QualityTier? quality)
    {
        Product = product;
        Quality = quality;
    }

    public ProductKey Product { get; }

    public QualityTier? Quality { get; }

    /// <summary>Sortable id: product, then tier (lowest first), unknown quality last.</summary>
    public string Id => Product.Value + "@" + (Quality is { } q ? ((int)q).ToString(System.Globalization.CultureInfo.InvariantCulture) : "?");

    public bool Meets(QualityTier? minimum) => QualityTiers.Meets(Quality, minimum);

    public bool Equals(StockLot other) => Product == other.Product && Quality == other.Quality;

    public override bool Equals(object? obj) => obj is StockLot other && Equals(other);

    public override int GetHashCode() => (Product.GetHashCode() * 397) ^ (Quality is { } q ? (int)q + 1 : 0);

    public int CompareTo(StockLot other) => string.CompareOrdinal(Id, other.Id);

    public override string ToString() => Quality is { } q ? $"{Product} ({q})" : Product.ToString();
}

/// <summary>
/// Read-only snapshot of one hired dealer's sellable stock, taken by the game adapter from the dealer's
/// inventory slots. Quantities are units (stack size x units per package). Invalid stacks (blank or
/// duplicated stack ids, unknown product ids, non-positive quantities) are ignored, never guessed, so the
/// snapshot can only under-count. Phase 05: units are kept per <see cref="StockLot"/> (product and quality tier), so a
/// quality minimum never counts stock that the game's removal would refuse. Immutable.
/// </summary>
public sealed class DealerStock
{
    private readonly Dictionary<StockLot, long> _units;
    private readonly Dictionary<ProductKey, long> _byProduct;

    public DealerStock(string dealerId, IEnumerable<InventoryStack?>? stacks)
    {
        if (!IsValidDealerId(dealerId))
        {
            throw new ArgumentException("Dealer id must be a short non-empty token.", nameof(dealerId));
        }

        DealerId = dealerId;
        var input = (stacks ?? Array.Empty<InventoryStack?>()).ToList();
        var duplicates = new HashSet<string>(
            input.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.StackId))
                 .GroupBy(s => s!.StackId, StringComparer.Ordinal)
                 .Where(g => g.Count() > 1)
                 .Select(g => g.Key),
            StringComparer.Ordinal);

        _units = new Dictionary<StockLot, long>();
        _byProduct = new Dictionary<ProductKey, long>();
        var ignored = 0;
        foreach (var stack in input)
        {
            if (stack is null || string.IsNullOrWhiteSpace(stack.StackId) || !stack.Product.IsValid ||
                stack.Quantity <= 0 || duplicates.Contains(stack.StackId))
            {
                ignored++;
                continue;
            }

            var lot = new StockLot(stack.Product, stack.Quality);
            _units[lot] = checked((_units.TryGetValue(lot, out var u) ? u : 0) + stack.Quantity);
            _byProduct[stack.Product] = checked((_byProduct.TryGetValue(stack.Product, out var p) ? p : 0) + stack.Quantity);
        }

        IgnoredStacks = ignored;
        Products = _byProduct.Keys.OrderBy(p => p).ToList();
        Lots = _units.Keys.OrderBy(l => l).ToList();
        unchecked
        {
            long hash = 1469598103934665603L;
            foreach (var lot in Lots)
            {
                hash = (hash ^ StringComparer.Ordinal.GetHashCode(lot.Id)) * 1099511628211L;
                hash = (hash ^ _units[lot]) * 1099511628211L;
            }

            Signature = hash;
        }
    }

    public string DealerId { get; }

    /// <summary>Products with at least one unit, sorted.</summary>
    public IReadOnlyList<ProductKey> Products { get; }

    /// <summary>Lots (product and quality) with at least one unit, sorted by <see cref="StockLot.Id"/>.</summary>
    public IReadOnlyList<StockLot> Lots { get; }

    /// <summary>Stacks left out because they were not readable or not unique.</summary>
    public int IgnoredStacks { get; }

    /// <summary>Changes whenever any product's unit count changes (cheap change detection for reconciling).</summary>
    public long Signature { get; }

    public long TotalUnits => _units.Values.Sum();

    /// <summary>All units of a product, any quality.</summary>
    public long UnitsOf(ProductKey product) => _byProduct.TryGetValue(product, out var units) ? units : 0;

    public long UnitsOf(StockLot lot) => _units.TryGetValue(lot, out var units) ? units : 0;

    /// <summary>Units of a product at or above <paramref name="minimum"/> (unknown quality counts only without a minimum).</summary>
    public long UnitsOf(ProductKey product, QualityTier? minimum)
        => minimum is null ? UnitsOf(product) : _units.Where(p => p.Key.Product == product && p.Key.Meets(minimum)).Sum(p => p.Value);

    public static DealerStock Empty(string dealerId) => new(dealerId, null);

    /// <summary>Dealer ids are the game's NPC ids: short tokens (letters, digits, '-', '_', '.').</summary>
    public static bool IsValidDealerId(string? id)
        => !string.IsNullOrWhiteSpace(id) && id!.Length <= 64 &&
           id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');

    public override string ToString() => $"{DealerId}: {TotalUnits} unit(s) of {Products.Count} product(s)";
}
