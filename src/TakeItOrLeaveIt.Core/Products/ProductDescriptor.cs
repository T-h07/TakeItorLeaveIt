using System;
using System.Collections.Generic;
using System.Linq;

namespace TakeItOrLeaveIt.Core.Products;

/// <summary>Whether the active save has legitimately unlocked/discovered a product.</summary>
public enum UnlockState
{
    /// <summary>Could not be determined. Always treated as NOT unlocked (fail closed).</summary>
    Unknown = 0,

    /// <summary>Known to exist but not discovered in the active save.</summary>
    Locked = 1,

    /// <summary>Discovered in the active save.</summary>
    Unlocked = 2,
}

/// <summary>A drug category known to the catalog.</summary>
public sealed class CategoryDescriptor
{
    public CategoryDescriptor(CategoryKey key, string? displayName = null)
    {
        if (!key.IsValid)
        {
            throw new ArgumentException("Category key is invalid.", nameof(key));
        }

        Key = key;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? key.Value : displayName!.Trim();
    }

    public CategoryKey Key { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Game-independent, immutable description of one exact product.
/// <para>
/// Identity is <see cref="Key"/> only. <see cref="DisplayName"/> is for people (two products may
/// share it). <see cref="RecipeFingerprint"/> (category + sorted effect IDs) groups products with the
/// same effects but is NOT an identity. <see cref="Parents"/> (and <see cref="BaseProduct"/>) are only set from the
/// game's recorded mix recipes; they are never inferred from names. <see cref="AncestryKnown"/> is false when the
/// recipes could not be read, and <see cref="EffectsKnown"/> is false when the effect list could not be read.
/// </para>
/// </summary>
public sealed class ProductDescriptor
{
    public ProductDescriptor(
        ProductKey key,
        CategoryKey category,
        string? displayName,
        UnlockState unlockState,
        IEnumerable<string>? effectIds = null,
        ProductKey? baseProduct = null,
        IEnumerable<ProductKey>? parents = null,
        bool ancestryKnown = false,
        bool effectsKnown = true)
    {
        if (!key.IsValid)
        {
            throw new ArgumentException("Product key is invalid.", nameof(key));
        }

        if (!category.IsValid)
        {
            throw new ArgumentException("Category key is invalid.", nameof(category));
        }

        if (baseProduct is { IsValid: false })
        {
            throw new ArgumentException("Base product key is invalid.", nameof(baseProduct));
        }

        Key = key;
        Category = category;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? key.Value : displayName!.Trim();
        UnlockState = unlockState;
        var parentList = new List<ProductKey>();
        foreach (var parent in parents ?? Array.Empty<ProductKey>())
        {
            if (parent.IsValid && parent != key && !parentList.Contains(parent))
            {
                parentList.Add(parent);
            }
        }

        if (baseProduct is { } single && single != key && !parentList.Contains(single))
        {
            parentList.Add(single);
        }

        parentList.Sort();
        Parents = parentList;
        BaseProduct = baseProduct ?? (parentList.Count == 1 ? parentList[0] : null);
        AncestryKnown = ancestryKnown;
        EffectsKnown = effectsKnown;
        EffectIds = (effectIds ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToArray();
        RecipeFingerprint = Category.Value + ":" + string.Join("+", EffectIds);
    }

    public ProductKey Key { get; }

    public CategoryKey Category { get; }

    public string DisplayName { get; }

    public UnlockState UnlockState { get; }

    public bool IsUnlocked => UnlockState == UnlockState.Unlocked;

    /// <summary>The single direct parent when there is exactly one (or the one the caller stated); otherwise null.</summary>
    public ProductKey? BaseProduct { get; }

    /// <summary>Direct parents from the game's mix recipes (input product of a recipe whose output is this product), sorted.</summary>
    public IReadOnlyList<ProductKey> Parents { get; }

    /// <summary>True when the game's mix recipes were read for this catalog, so <see cref="Parents"/> is complete.</summary>
    public bool AncestryKnown { get; }

    /// <summary>True when the effect list was read (an empty list is then a real "no effects").</summary>
    public bool EffectsKnown { get; }

    /// <summary>Effect IDs, de-duplicated and sorted ordinally.</summary>
    public IReadOnlyList<string> EffectIds { get; }

    /// <summary>Category plus sorted effect IDs. Useful for grouping; never used as identity.</summary>
    public string RecipeFingerprint { get; }

    /// <summary>Returns a copy with a different unlock state (used when merging discovery sources).</summary>
    public ProductDescriptor WithUnlockState(UnlockState state)
        => state == UnlockState ? this : new ProductDescriptor(Key, Category, DisplayName, state, EffectIds, BaseProduct, Parents, AncestryKnown, EffectsKnown);

    public override string ToString() => $"{DisplayName} [{Key}] ({Category}, {UnlockState})";
}
