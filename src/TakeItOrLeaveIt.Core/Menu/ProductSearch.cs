using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Menu;

/// <summary>
/// Case-insensitive product search over display name, product ID and effect IDs. Pure; never changes
/// selections. Every whitespace-separated term must match somewhere ("grand calm").
/// </summary>
public static class ProductSearch
{
    public const int MaxQueryLength = 64;

    public static string Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query!.Trim();
        return trimmed.Length > MaxQueryLength ? trimmed.Substring(0, MaxQueryLength) : trimmed;
    }

    public static bool Matches(ProductDescriptor product, string normalizedQuery)
    {
        if (product is null)
        {
            return false;
        }

        if (normalizedQuery.Length == 0)
        {
            return true;
        }

        foreach (var term in normalizedQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!MatchesTerm(product, term))
            {
                return false;
            }
        }

        return true;
    }

    public static List<ProductDescriptor> Filter(IEnumerable<ProductDescriptor> products, string? query)
    {
        var normalized = Normalize(query);
        var result = new List<ProductDescriptor>();
        foreach (var product in products ?? Array.Empty<ProductDescriptor>())
        {
            if (Matches(product, normalized))
            {
                result.Add(product);
            }
        }

        return result;
    }

    private static bool MatchesTerm(ProductDescriptor product, string term)
    {
        if (Contains(product.DisplayName, term) || Contains(product.Key.Value, term))
        {
            return true;
        }

        foreach (var effect in product.EffectIds)
        {
            if (Contains(effect, term))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
