using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Menu;

/// <summary>Top-level menu screens.</summary>
public enum MenuScreen
{
    Overview,
    Products,
    Settings,
    Diagnostics,

    /// <summary>Lineage matching, quality and effect rules of the selected category (Phase 05).</summary>
    Rules,

    /// <summary>Player/dealer switches, mixing mode and dealer overrides (Phase 05).</summary>
    Sales,

    /// <summary>Presets with preview and confirmation (Phase 05).</summary>
    Presets,
}

/// <summary>A dealer the host has seen in this save (Phase 05 overrides).</summary>
public sealed class DealerInfo
{
    public DealerInfo(string id, string? name, bool hired)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name!.Trim();
        Hired = hired;
    }

    public string Id { get; }

    public string Name { get; }

    public bool Hired { get; }
}

/// <summary>One dealer row on the Sales screen.</summary>
public sealed class DealerScopeView
{
    internal DealerScopeView(string id, string name, bool hired, bool known, int overrides, bool editing)
    {
        Id = id;
        Name = name;
        Hired = hired;
        Known = known;
        OverrideCount = overrides;
        IsEditing = editing;
    }

    public string Id { get; }

    public string Name { get; }

    public bool Hired { get; }

    /// <summary>False for overrides saved for a dealer not seen in this session (shown as inactive).</summary>
    public bool Known { get; }

    public int OverrideCount { get; }

    public bool IsEditing { get; }

    public string Status => !Known ? "not seen this session - overrides inactive"
        : !Hired ? "not hired - overrides inactive"
        : OverrideCount == 0 ? "uses the global rules"
        : OverrideCount == 1 ? "1 category override" : $"{OverrideCount} category overrides";
}

/// <summary>Effect state on the Rules screen.</summary>
public enum EffectState
{
    None,
    Required,
    Excluded,
}

/// <summary>One effect row on the Rules screen.</summary>
public sealed class EffectRowView
{
    internal EffectRowView(string id, EffectState state, bool onDiscoveredProduct, bool conflicting)
    {
        Id = id;
        State = state;
        OnDiscoveredProduct = onDiscoveredProduct;
        IsConflicting = conflicting;
    }

    public string Id { get; }

    public EffectState State { get; }

    /// <summary>False for a saved effect id that no discovered product of the category has (inactive).</summary>
    public bool OnDiscoveredProduct { get; }

    public bool IsConflicting { get; }

    public string StateLabel => State switch
    {
        EffectState.Required => "Required",
        EffectState.Excluded => "Excluded",
        _ => "Any",
    };
}

/// <summary>What the menu can currently do, derived from the catalog state.</summary>
public enum MenuAvailability
{
    /// <summary>No save loaded: nothing can be edited.</summary>
    WaitingForSave,

    /// <summary>A save is loading or the catalog is being built.</summary>
    LoadingCatalog,

    /// <summary>Catalog ready: everything can be edited.</summary>
    Ready,

    /// <summary>Discovery failed for this save; settings are kept but cannot be edited.</summary>
    CatalogFailed,

    /// <summary>This game build cannot provide product data.</summary>
    Unsupported,
}

public enum FeedbackKind
{
    None,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A one-line message for the player (e.g. "Settings saved.").</summary>
public sealed class MenuFeedback
{
    public static readonly MenuFeedback Empty = new(FeedbackKind.None, string.Empty);

    public MenuFeedback(FeedbackKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public FeedbackKind Kind { get; }

    public string Text { get; }

    public override string ToString() => $"{Kind}: {Text}";
}

/// <summary>One category as shown on the Overview screen and in the category list.</summary>
public sealed class CategorySummaryView
{
    internal CategorySummaryView(CategoryKey key, string displayName, PolicyMode mode, int unlocked, int eligible, int selected,
        int inactiveSelections, EligibilityReason reason)
    {
        Key = key;
        DisplayName = displayName;
        Mode = mode;
        UnlockedCount = unlocked;
        EligibleCount = eligible;
        SelectedCount = selected;
        InactiveSelectionCount = inactiveSelections;
        Reason = reason;
    }

    public CategoryKey Key { get; }

    public string DisplayName { get; }

    public PolicyMode Mode { get; }

    public string ModeLabel => MenuText.ModeLabel(Mode);

    public int UnlockedCount { get; }

    public int EligibleCount { get; }

    public int SelectedCount { get; }

    /// <summary>Saved selections that the active save cannot use (unknown, locked or wrong category).</summary>
    public int InactiveSelectionCount { get; }

    public EligibilityReason Reason { get; }

    /// <summary>Phase 05 rule details in one short line ("" when none), e.g. "base strain - Premium+ - 2 effect rules".</summary>
    public string RuleDetail { get; internal set; } = string.Empty;

    /// <summary>True when the category's rule comes from the edited dealer's override (dealer scope only).</summary>
    public bool IsDealerOverride { get; internal set; }

    /// <summary>e.g. "1 permitted product".</summary>
    public string EligibleLabel => EligibleCount == 1 ? "1 permitted product" : $"{EligibleCount} permitted products";

    /// <summary>Warning for this category, or null.</summary>
    public string? Warning => Reason switch
    {
        EligibilityReason.NoProductsSelected => MenuText.EmptySelectionWarning,
        EligibilityReason.NoEligibleProducts when Mode == PolicyMode.SelectedOnly => "None of the selected products are unlocked in this save.",
        EligibilityReason.EffectRuleContradictory => "The effect rule can never be met: no product is permitted.",
        EligibilityReason.NoEligibleProducts when RuleDetail.Length > 0 => "No unlocked product meets these rules.",
        EligibilityReason.NoEligibleProducts => "No unlocked products in this category yet.",
        _ => null,
    };
}

/// <summary>One product row on the Products screen.</summary>
public sealed class ProductRowView
{
    internal ProductRowView(ProductDescriptor product, bool selected, bool selectable, EligibilityDecision decision, bool duplicateName, string? lineage = null)
    {
        Key = product.Key;
        DisplayName = product.DisplayName;
        UnlockState = product.UnlockState;
        IsSelected = selected;
        IsSelectable = selectable;
        Decision = decision.Reason;
        IsEligible = decision.IsEligible;
        Detail = BuildDetail(product, duplicateName);
        Lineage = lineage ?? string.Empty;
    }

    /// <summary>Recorded lineage ("from OG Kush"), "base strain", "lineage unknown", or "" (Phase 05).</summary>
    public string Lineage { get; }

    public ProductKey Key { get; }

    public string DisplayName { get; }

    /// <summary>Secondary line: effects, plus the product ID when another product shares the display name.</summary>
    public string Detail { get; }

    public UnlockState UnlockState { get; }

    /// <summary>On the category's active list: the allowed list, or the blocked list in Blocklist mode.</summary>
    public bool IsSelected { get; }

    /// <summary>False for products that are not unlocked: their checkbox must be disabled.</summary>
    public bool IsSelectable { get; }

    /// <summary>The Phase 01 policy decision under the current (draft) rules.</summary>
    public EligibilityReason Decision { get; }

    public bool IsEligible { get; }

    public string StatusLabel => IsEligible ? "Permitted" : MenuText.ShortReason(Decision);

    private static string BuildDetail(ProductDescriptor product, bool duplicateName)
    {
        var parts = new List<string>();
        if (product.UnlockState != UnlockState.Unlocked)
        {
            parts.Add(product.UnlockState == UnlockState.Locked ? "Locked" : "Unlock state unknown");
        }

        if (product.EffectIds.Count > 0)
        {
            parts.Add(string.Join(", ", product.EffectIds));
        }

        if (duplicateName || parts.Count == 0)
        {
            parts.Add("ID " + product.Key.Value);
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>A page of product rows (the list is paged so huge catalogs stay cheap to render).</summary>
public sealed class ProductPageView
{
    internal ProductPageView(IReadOnlyList<ProductRowView> rows, int pageIndex, int pageCount, int matchCount, int totalCount)
    {
        Rows = rows;
        PageIndex = pageIndex;
        PageCount = pageCount;
        MatchCount = matchCount;
        TotalCount = totalCount;
    }

    public IReadOnlyList<ProductRowView> Rows { get; }

    public int PageIndex { get; }

    public int PageCount { get; }

    /// <summary>Products matching the search.</summary>
    public int MatchCount { get; }

    /// <summary>All products in the category.</summary>
    public int TotalCount { get; }
}

/// <summary>Hypothetical order preview ("5 weed"), computed by the Phase 01 policy.</summary>
public sealed class PolicyPreviewView
{
    internal PolicyPreviewView(CategoryKey category, int quantity, EligibilityReason reason, IReadOnlyList<string> permitted,
        int permittedCount, IReadOnlyList<string> lines, bool reflectsUnsavedChanges)
    {
        Category = category;
        Quantity = quantity;
        Reason = reason;
        PermittedNames = permitted;
        PermittedCount = permittedCount;
        Lines = lines;
        ReflectsUnsavedChanges = reflectsUnsavedChanges;
    }

    public CategoryKey Category { get; }

    public int Quantity { get; }

    public EligibilityReason Reason { get; }

    public bool AnyPermitted => Reason == EligibilityReason.Allowed;

    public string Headline => $"Hypothetical order: {Quantity} {Category.Value}";

    public string Summary => AnyPermitted
        ? (PermittedCount == 1 ? "1 product would be permitted." : $"{PermittedCount} products would be permitted.")
        : EligibilityText.Explain(Reason);

    /// <summary>Up to a few permitted product names.</summary>
    public IReadOnlyList<string> PermittedNames { get; }

    public int PermittedCount { get; }

    /// <summary>Per-product lines such as "OG Kush: Not selected".</summary>
    public IReadOnlyList<string> Lines { get; }

    public bool ReflectsUnsavedChanges { get; }

    public string Disclaimer => "Eligibility only - this does not check stock or item quality. Orders accepted from now on use these rules; accepted orders keep theirs.";
}

/// <summary>Player-facing wording, kept in one place.</summary>
public static class MenuText
{
    public const string WaitingForSave = "Waiting for active save...";
    public const string WaitingForProducts = "Waiting for product data...";
    public const string CatalogFailed = "Unable to load product catalog.";
    public const string Unsupported = "Product data is not available in this game version.";
    public const string EmptySelectionWarning = "No products selected. This category currently has no eligible products.";
    public const string SalesNotice = "Orders accepted from now on take any permitted product of their category, for you and your dealers. Accepted orders keep the rules they were accepted with.";
    public const string Saved = "Settings saved.";

    public static string ModeLabel(PolicyMode mode) => mode switch
    {
        PolicyMode.AnyUnlocked => "Any Unlocked",
        PolicyMode.SelectedOnly => "Selected Only",
        PolicyMode.Blocklist => "Blocklist",
        _ => "Disabled",
    };

    public static string ModeDescription(PolicyMode mode) => mode switch
    {
        PolicyMode.AnyUnlocked => "All products you've discovered in this category are permitted.",
        PolicyMode.SelectedOnly => "Only the unlocked products on the allowed list are permitted.",
        PolicyMode.Blocklist => "Every unlocked product is permitted except the ones on the blocked list.",
        _ => "No products in this category are permitted.",
    };

    public static string MatchLabel(SelectionMatch match) => match == SelectionMatch.BaseStrain ? "Base strain (with its mixes)" : "Exact recipe";

    public static string MatchDescription(SelectionMatch match) => match == SelectionMatch.BaseStrain
        ? "A listed product also covers every mix the game recorded as made from it (directly or through other mixes). Products whose lineage could not be read only match themselves."
        : "A listed product covers exactly that recipe and nothing else.";

    public static string QualityLabel(QualityTier? minimum) => minimum is { } q ? QualityTiers.Label(q) + "+" : "Any quality";

    public static string MixingDescription(MixingMode mixing) => mixing switch
    {
        MixingMode.AnyPermitted => "One order may combine any permitted products of its category.",
        MixingMode.SingleProduct => "One order is filled with a single product.",
        MixingMode.SingleBaseStrain => "One order may combine products only when the game recorded them as coming from the same base strain.",
        _ => "No substitution: only the requested product, and only if your rules permit it.",
    };

    public static string ShortReason(EligibilityReason reason) => reason switch
    {
        EligibilityReason.Allowed => "Permitted",
        EligibilityReason.ProductNotSelected => "Not selected",
        EligibilityReason.NoProductsSelected => "Nothing selected",
        EligibilityReason.ProductLocked => "Not unlocked",
        EligibilityReason.UnlockStateUnknown => "Unlock unknown",
        EligibilityReason.CategoryDisabled => "Category disabled",
        EligibilityReason.WrongCategory => "Wrong category",
        EligibilityReason.ModDisabled => "Mod disabled",
        EligibilityReason.CatalogNotReady => "Catalog unavailable",
        EligibilityReason.ProductUnknown => "Unknown product",
        EligibilityReason.ProductBlocked => "Blocked",
        EligibilityReason.AncestryUnknown => "Lineage unknown",
        EligibilityReason.EffectMissing => "Missing effect",
        EligibilityReason.EffectExcluded => "Excluded effect",
        EligibilityReason.EffectsUnknown => "Effects unknown",
        EligibilityReason.EffectRuleContradictory => "Effect rule conflict",
        EligibilityReason.QualityTooLow => "Quality too low",
        EligibilityReason.QualityUnknown => "Quality unknown",
        EligibilityReason.SalesPathDisabled => "Sales off",
        _ => reason.ToString(),
    };

    /// <summary>"marijuana" -> "Weed" etc. for the categories players know by another name; others are title-cased.</summary>
    public static string CategoryLabel(CategoryKey key, string? sourceName)
    {
        switch (key.Value)
        {
            case "marijuana":
                return "Weed";
            case "methamphetamine":
                return "Meth";
        }

        var name = string.IsNullOrWhiteSpace(sourceName) ? key.Value : sourceName!;
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
