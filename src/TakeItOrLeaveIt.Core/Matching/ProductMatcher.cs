using System;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Matching;

/// <summary>
/// Pure identity and category comparisons. Matching uses keys only. Display names never take part,
/// so a product cannot satisfy a category or another product by having a similar name.
/// </summary>
public static class ProductMatcher
{
    /// <summary>True when <paramref name="product"/> belongs to <paramref name="category"/>.</summary>
    public static bool MatchesCategory(ProductDescriptor product, CategoryKey category)
    {
        if (product is null)
        {
            throw new ArgumentNullException(nameof(product));
        }

        return category.IsValid && product.Category == category;
    }

    /// <summary>Exact-product match (same stable key).</summary>
    public static bool IsSameProduct(ProductDescriptor a, ProductDescriptor b)
    {
        if (a is null || b is null)
        {
            throw new ArgumentNullException(a is null ? nameof(a) : nameof(b));
        }

        return a.Key == b.Key;
    }

    /// <summary>
    /// Same category and same effect set. Two DIFFERENT products can match here (and they stay
    /// distinct products). This is a grouping aid for Phase 05 recipe rules, not an identity check.
    /// </summary>
    public static bool HasSameRecipeFingerprint(ProductDescriptor a, ProductDescriptor b)
    {
        if (a is null || b is null)
        {
            throw new ArgumentNullException(a is null ? nameof(a) : nameof(b));
        }

        return string.Equals(a.RecipeFingerprint, b.RecipeFingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="product"/> declares <paramref name="baseProduct"/> as its base.
    /// Returns false when the base is unknown; ancestry is never guessed from names.
    /// </summary>
    public static bool IsDerivedFrom(ProductDescriptor product, ProductKey baseProduct)
    {
        if (product is null)
        {
            throw new ArgumentNullException(nameof(product));
        }

        return baseProduct.IsValid && (product.Key == baseProduct || product.BaseProduct == baseProduct);
    }
}
