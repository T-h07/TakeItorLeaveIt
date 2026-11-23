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

    /// <summary>e.g. "1 permitted product".</summary>
    public string EligibleLabel => EligibleCount == 1 ? "1 permitted product" : $"{EligibleCount} permitted products";

    /// <summary>Warning for this category, or null.</summary>
    public string? Warning => Reason switch
    {
        EligibilityReason.NoProductsSelected => MenuText.EmptySelectionWarning,
        EligibilityReason.NoEligibleProducts when Mode == PolicyMode.SelectedOnly => "None of the selected products are unlocked in this save.",
        EligibilityReason.NoEligibleProducts => "No unlocked products in this category yet.",
        _ => null,
    };
}

/// <summary>One product row on the Products screen.</summary>
public sealed class ProductRowView
{
    internal ProductRowView(ProductDescriptor product, bool selected, bool selectable, EligibilityDecision decision, bool duplicateName)
    {
        Key = product.Key;
        DisplayName = product.DisplayName;
        UnlockState = product.UnlockState;
        IsSelected = selected;
        IsSelectable = selectable;
        Decision = decision.Reason;
        IsEligible = decision.IsEligible;
        Detail = BuildDetail(product, duplicateName);
    }

    public ProductKey Key { get; }

    public string DisplayName { get; }

    /// <summary>Secondary line: effects, plus the product ID when another product shares the display name.</summary>
    public string Detail { get; }

    public UnlockState UnlockState { get; }

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

    public string Disclaimer => "Eligibility only - this does not check stock. Customer orders accepted from now on use these rules; dealers stay vanilla until Phase 04.";
}

/// <summary>Player-facing wording, kept in one place.</summary>
public static class MenuText
{
    public const string WaitingForSave = "Waiting for active save...";
    public const string WaitingForProducts = "Waiting for product data...";
    public const string CatalogFailed = "Unable to load product catalog.";
    public const string Unsupported = "Product data is not available in this game version.";
    public const string EmptySelectionWarning = "No products selected. This category currently has no eligible products.";
    public const string SalesNotice = "Customer orders accepted from now on take any permitted product of their category. Dealers are unchanged until Phase 04.";
    public const string Saved = "Settings saved.";

    public static string ModeLabel(PolicyMode mode) => mode switch
    {
        PolicyMode.AnyUnlocked => "Any Unlocked",
        PolicyMode.SelectedOnly => "Selected Only",
        _ => "Disabled",
    };

    public static string ModeDescription(PolicyMode mode) => mode switch
    {
        PolicyMode.AnyUnlocked => "All products you've discovered in this category are permitted.",
        PolicyMode.SelectedOnly => "Only the unlocked products you select are permitted.",
        _ => "No products in this category are permitted.",
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
