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

    /// <summary>Allowlist: only selected products (or their derivatives, by <see cref="SelectionMatch"/>) that are unlocked.</summary>
    SelectedOnly = 1,

    /// <summary>No product of the category is eligible; no new mod-managed offers for it.</summary>
    Disabled = 2,

    /// <summary>Blocklist (Phase 05): every unlocked product except the blocked ones (or their derivatives).</summary>
    Blocklist = 3,
}

/// <summary>What a listed product stands for (Phase 05). Applies to the allowlist and the blocklist alike.</summary>
public enum SelectionMatch
{
    /// <summary>Exactly that product id (the Phase 01-04 behaviour).</summary>
    ExactProduct = 0,

    /// <summary>That product and every product the game records as mixed from it, directly or through other mixes.</summary>
    BaseStrain = 1,
}

public enum EffectMatch
{
    /// <summary>A product must have every required effect.</summary>
    All = 0,

    /// <summary>A product must have at least one required effect.</summary>
    Any = 1,
}

/// <summary>Effect filter of one category (Phase 05). Effect ids only, never display names. Immutable.</summary>
public sealed class EffectRule
{
    public static readonly EffectRule None = new(EffectMatch.All, null, null);

    public EffectRule(EffectMatch match, IEnumerable<string>? required, IEnumerable<string>? excluded)
    {
        Match = Enum.IsDefined(typeof(EffectMatch), match) ? match : EffectMatch.All;
        Required = Clean(required);
        Excluded = Clean(excluded);
        Conflicts = Required.Intersect(Excluded, StringComparer.Ordinal).ToArray();
    }

    public EffectMatch Match { get; }

    /// <summary>Required effect ids (distinct, sorted ordinally).</summary>
    public IReadOnlyList<string> Required { get; }

    /// <summary>Excluded effect ids (distinct, sorted ordinally).</summary>
    public IReadOnlyList<string> Excluded { get; }

    /// <summary>Effects that are both required and excluded.</summary>
    public IReadOnlyList<string> Conflicts { get; }

    public bool IsEmpty => Required.Count == 0 && Excluded.Count == 0;

    /// <summary>True when no product could ever pass: an effect required by "all" is also excluded, or every "any" effect is excluded.</summary>
    public bool IsContradictory => Required.Count > 0 && (Match == EffectMatch.All ? Conflicts.Count > 0 : Conflicts.Count == Required.Count);

    /// <summary>Checks a product's effect ids. Returns the failing reason or <see cref="EligibilityReason.Allowed"/>.</summary>
    public EligibilityReason Check(IReadOnlyList<string> effects, bool effectsKnown)
    {
        if (IsEmpty)
        {
            return EligibilityReason.Allowed;
        }

        if (IsContradictory)
        {
            return EligibilityReason.EffectRuleContradictory;
        }

        if (!effectsKnown)
        {
            return EligibilityReason.EffectsUnknown;
        }

        var has = new HashSet<string>(effects ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (Excluded.Any(has.Contains))
        {
            return EligibilityReason.EffectExcluded;
        }

        if (Required.Count == 0)
        {
            return EligibilityReason.Allowed;
        }

        var ok = Match == EffectMatch.All ? Required.All(has.Contains) : Required.Any(has.Contains);
        return ok ? EligibilityReason.Allowed : EligibilityReason.EffectMissing;
    }

    public EffectRule WithMatch(EffectMatch match) => new(match, Required, Excluded);

    /// <summary>Cycles an effect: not used → required → excluded → not used.</summary>
    public EffectRule Cycle(string effect)
    {
        if (string.IsNullOrWhiteSpace(effect))
        {
            return this;
        }

        var id = effect.Trim();
        if (Required.Contains(id, StringComparer.Ordinal))
        {
            return new EffectRule(Match, Required.Where(e => e != id), Excluded.Concat(new[] { id }));
        }

        if (Excluded.Contains(id, StringComparer.Ordinal))
        {
            return new EffectRule(Match, Required, Excluded.Where(e => e != id));
        }

        return new EffectRule(Match, Required.Concat(new[] { id }), Excluded);
    }

    public bool SameAs(EffectRule? other)
        => other is not null && other.Match == Match && other.Required.SequenceEqual(Required) && other.Excluded.SequenceEqual(Excluded);

    private static string[] Clean(IEnumerable<string>? ids)
        => (ids ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>Immutable rule for one category. Selections are stable product keys, never display names.</summary>
public sealed class CategoryPolicy
{
    public static readonly CategoryPolicy AnyUnlocked = new(PolicyMode.AnyUnlocked, null);

    public static readonly CategoryPolicy Disabled = new(PolicyMode.Disabled, null);

    private readonly HashSet<ProductKey> _selected;
    private readonly HashSet<ProductKey> _blocked;

    public CategoryPolicy(PolicyMode mode, IEnumerable<ProductKey>? selectedProducts)
        : this(mode, selectedProducts, SelectionMatch.ExactProduct, null, null, null)
    {
    }

    public CategoryPolicy(
        PolicyMode mode,
        IEnumerable<ProductKey>? selectedProducts,
        SelectionMatch match,
        IEnumerable<ProductKey>? blockedProducts,
        EffectRule? effects,
        QualityTier? minimumQuality)
    {
        if (!Enum.IsDefined(typeof(PolicyMode), mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown policy mode.");
        }

        if (!Enum.IsDefined(typeof(SelectionMatch), match))
        {
            throw new ArgumentOutOfRangeException(nameof(match), match, "Unknown selection match.");
        }

        if (minimumQuality is { } q && !Enum.IsDefined(typeof(QualityTier), q))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumQuality), q, "Unknown quality tier.");
        }

        Mode = mode;
        Match = match;
        Effects = effects ?? EffectRule.None;
        MinimumQuality = minimumQuality == QualityTier.Trash ? null : minimumQuality; // "at least Trash" is no rule
        _selected = Keys(selectedProducts, nameof(selectedProducts));
        _blocked = Keys(blockedProducts, nameof(blockedProducts));
        SelectedProducts = _selected.OrderBy(k => k).ToArray();
        BlockedProducts = _blocked.OrderBy(k => k).ToArray();
    }

    public PolicyMode Mode { get; }

    /// <summary>Whether listed products mean exact products or base strains (Phase 05).</summary>
    public SelectionMatch Match { get; }

    /// <summary>Allowlist (distinct, sorted). Kept even when the mode is not SelectedOnly so switching modes does not lose it.</summary>
    public IReadOnlyList<ProductKey> SelectedProducts { get; }

    /// <summary>Blocklist (distinct, sorted). Kept even when the mode is not Blocklist.</summary>
    public IReadOnlyList<ProductKey> BlockedProducts { get; }

    public EffectRule Effects { get; }

    /// <summary>Lowest quality tier a delivered stack may have; null means any quality (the game's own requirement still applies).</summary>
    public QualityTier? MinimumQuality { get; }

    /// <summary>The list the current mode uses (allowlist for Selected Only, blocklist for Blocklist, otherwise empty).</summary>
    public IReadOnlyList<ProductKey> ActiveList => Mode switch
    {
        PolicyMode.SelectedOnly => SelectedProducts,
        PolicyMode.Blocklist => BlockedProducts,
        _ => Array.Empty<ProductKey>(),
    };

    /// <summary>True when the category has rules beyond its mode (effects or quality).</summary>
    public bool HasAdvancedRules => !Effects.IsEmpty || MinimumQuality is not null || Match != SelectionMatch.ExactProduct;

    public bool IsSelected(ProductKey key) => _selected.Contains(key);

    public bool IsBlocked(ProductKey key) => _blocked.Contains(key);

    /// <summary>Whether <paramref name="key"/> is on the list the current mode edits.</summary>
    public bool IsListed(ProductKey key) => Mode == PolicyMode.Blocklist ? _blocked.Contains(key) : _selected.Contains(key);

    public static CategoryPolicy SelectedOnly(params ProductKey[] products) => new(PolicyMode.SelectedOnly, products);

    public static CategoryPolicy Blocking(params ProductKey[] products)
        => new(PolicyMode.Blocklist, null, SelectionMatch.ExactProduct, products, null, null);

    public CategoryPolicy WithMode(PolicyMode mode) => mode == Mode ? this : Copy(mode: mode);

    public CategoryPolicy WithSelection(IEnumerable<ProductKey> products) => Copy(selected: products);

    public CategoryPolicy WithBlocked(IEnumerable<ProductKey> products) => Copy(blocked: products);

    /// <summary>Replaces the list the current mode uses (the blocklist in Blocklist mode, otherwise the allowlist).</summary>
    public CategoryPolicy WithActiveList(IEnumerable<ProductKey> products)
        => Mode == PolicyMode.Blocklist ? WithBlocked(products) : WithSelection(products);

    public CategoryPolicy WithMatch(SelectionMatch match) => match == Match ? this : Copy(match: match);

    public CategoryPolicy WithEffects(EffectRule effects) => Copy(effects: effects ?? EffectRule.None);

    public CategoryPolicy WithMinimumQuality(QualityTier? minimum) => new(Mode, SelectedProducts, Match, BlockedProducts, Effects, minimum);

    /// <summary>Same mode, lists and rules (used to tell a preset from a custom configuration).</summary>
    public bool SameAs(CategoryPolicy? other)
        => other is not null && other.Mode == Mode && other.Match == Match && other.MinimumQuality == MinimumQuality &&
           other.SelectedProducts.SequenceEqual(SelectedProducts) && other.BlockedProducts.SequenceEqual(BlockedProducts) &&
           other.Effects.SameAs(Effects);

    private CategoryPolicy Copy(
        PolicyMode? mode = null,
        IEnumerable<ProductKey>? selected = null,
        IEnumerable<ProductKey>? blocked = null,
        SelectionMatch? match = null,
        EffectRule? effects = null)
        => new(mode ?? Mode, selected ?? SelectedProducts, match ?? Match, blocked ?? BlockedProducts, effects ?? Effects, MinimumQuality);

    private static HashSet<ProductKey> Keys(IEnumerable<ProductKey>? keys, string parameter)
    {
        var set = new HashSet<ProductKey>();
        foreach (var key in keys ?? Array.Empty<ProductKey>())
        {
            if (!key.IsValid)
            {
                throw new ArgumentException("Listed product keys must be valid.", parameter);
            }

            set.Add(key);
        }

        return set;
    }
}
