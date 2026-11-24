using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>
/// Read-only snapshot of one hired dealer's sellable stock, taken by the game adapter from the dealer's
/// inventory slots. Quantities are units (stack size x units per package). Invalid stacks (blank or
/// duplicated stack ids, unknown product ids, non-positive quantities) are ignored, never guessed, so the
/// snapshot can only under-count. Immutable.
/// </summary>
public sealed class DealerStock
{
    private readonly Dictionary<ProductKey, long> _units;

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

        _units = new Dictionary<ProductKey, long>();
        var ignored = 0;
        foreach (var stack in input)
        {
            if (stack is null || string.IsNullOrWhiteSpace(stack.StackId) || !stack.Product.IsValid ||
                stack.Quantity <= 0 || duplicates.Contains(stack.StackId))
            {
                ignored++;
                continue;
            }

            _units[stack.Product] = checked((_units.TryGetValue(stack.Product, out var u) ? u : 0) + stack.Quantity);
        }

        IgnoredStacks = ignored;
        Products = _units.Keys.OrderBy(p => p).ToList();
        unchecked
        {
            long hash = 1469598103934665603L;
            foreach (var product in Products)
            {
                hash = (hash ^ StringComparer.Ordinal.GetHashCode(product.Value)) * 1099511628211L;
                hash = (hash ^ _units[product]) * 1099511628211L;
            }

            Signature = hash;
        }
    }

    public string DealerId { get; }

    /// <summary>Products with at least one unit, sorted.</summary>
    public IReadOnlyList<ProductKey> Products { get; }

    /// <summary>Stacks left out because they were not readable or not unique.</summary>
    public int IgnoredStacks { get; }

    /// <summary>Changes whenever any product's unit count changes (cheap change detection for reconciling).</summary>
    public long Signature { get; }

    public long TotalUnits => _units.Values.Sum();

    public long UnitsOf(ProductKey product) => _units.TryGetValue(product, out var units) ? units : 0;

    public static DealerStock Empty(string dealerId) => new(dealerId, null);

    /// <summary>Dealer ids are the game's NPC ids: short tokens (letters, digits, '-', '_', '.').</summary>
    public static bool IsValidDealerId(string? id)
        => !string.IsNullOrWhiteSpace(id) && id!.Length <= 64 &&
           id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');

    public override string ToString() => $"{DealerId}: {TotalUnits} unit(s) of {Products.Count} product(s)";
}
