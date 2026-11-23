using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Policies;

/// <summary>How a category participates in mod-managed sales.</summary>
public enum PolicyMode
{
    /// <summary>Any product of the category that is unlocked in the active save.</summary>
    AnyUnlocked = 0,

    /// <summary>Only explicitly selected products that are also unlocked in the active save.</summary>
    SelectedOnly = 1,

    /// <summary>No product of the category is eligible; no new mod-managed offers for it.</summary>
    Disabled = 2,
}

/// <summary>Immutable rule for one category. Selections are stable product keys, never display names.</summary>
public sealed class CategoryPolicy
{
    public static readonly CategoryPolicy AnyUnlocked = new(PolicyMode.AnyUnlocked, null);

    public static readonly CategoryPolicy Disabled = new(PolicyMode.Disabled, null);

    private readonly HashSet<ProductKey> _selected;

    public CategoryPolicy(PolicyMode mode, IEnumerable<ProductKey>? selectedProducts)
    {
        if (!Enum.IsDefined(typeof(PolicyMode), mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown policy mode.");
        }

        Mode = mode;
        _selected = new HashSet<ProductKey>();
        foreach (var key in selectedProducts ?? Array.Empty<ProductKey>())
        {
            if (!key.IsValid)
            {
                throw new ArgumentException("Selected product keys must be valid.", nameof(selectedProducts));
            }

            _selected.Add(key);
        }

        SelectedProducts = _selected.OrderBy(k => k).ToArray();
    }

    public PolicyMode Mode { get; }

    /// <summary>Selected product keys (distinct, sorted). Kept even when the mode is not SelectedOnly so switching modes does not lose them.</summary>
    public IReadOnlyList<ProductKey> SelectedProducts { get; }

    public bool IsSelected(ProductKey key) => _selected.Contains(key);

    public static CategoryPolicy SelectedOnly(params ProductKey[] products) => new(PolicyMode.SelectedOnly, products);

    public CategoryPolicy WithMode(PolicyMode mode) => mode == Mode ? this : new CategoryPolicy(mode, SelectedProducts);

    public CategoryPolicy WithSelection(IEnumerable<ProductKey> products) => new(Mode, products);
}
