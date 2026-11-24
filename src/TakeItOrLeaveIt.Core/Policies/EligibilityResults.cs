using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Policies;

/// <summary>Machine-readable reason for an eligibility decision. Program logic switches on this, never on text.</summary>
public enum EligibilityReason
{
    Allowed = 0,

    /// <summary>The mod is globally disabled; integrations must use vanilla behaviour.</summary>
    ModDisabled,

    /// <summary>No ready catalog for the active save (loading, failed, unsupported, or no save).</summary>
    CatalogNotReady,

    /// <summary>The request itself was malformed (invalid category or product key).</summary>
    InvalidRequest,

    /// <summary>The requested category is not present in the active catalog.</summary>
    UnsupportedCategory,

    /// <summary>The product is not present in the active catalog.</summary>
    ProductUnknown,

    WrongCategory,
    CategoryDisabled,

    /// <summary>The product exists but is not unlocked in the active save.</summary>
    ProductLocked,

    /// <summary>The product's unlock state could not be determined (fail closed).</summary>
    UnlockStateUnknown,

    /// <summary>SelectedOnly with an empty selection.</summary>
    NoProductsSelected,

    ProductNotSelected,

    /// <summary>The category has no eligible product (e.g. nothing unlocked, or every selection is inactive).</summary>
    NoEligibleProducts,

    InvalidQuantity,

    /// <summary>Blocklist: the product (or, by base strain, one of its ancestors) is blocked.</summary>
    ProductBlocked,

    /// <summary>A base-strain rule needs the product's ancestry, but the game's mix recipes could not be read (fail closed).</summary>
    AncestryUnknown,

    /// <summary>The product lacks a required effect.</summary>
    EffectMissing,

    /// <summary>The product has an excluded effect.</summary>
    EffectExcluded,

    /// <summary>An effect rule is active but the product's effects could not be read (fail closed).</summary>
    EffectsUnknown,

    /// <summary>The category's effect rule can never be met (an effect both required and excluded).</summary>
    EffectRuleContradictory,

    /// <summary>A physical stack is below the category's minimum quality.</summary>
    QualityTooLow,

    /// <summary>A minimum quality is set but the stack's quality could not be read (fail closed).</summary>
    QualityUnknown,

    /// <summary>Player or dealer sales are switched off for this save (vanilla for that path).</summary>
    SalesPathDisabled,
}

/// <summary>Result of evaluating one product for one requested category.</summary>
public sealed class EligibilityDecision
{
    internal EligibilityDecision(EligibilityReason reason, CategoryKey category, ProductKey product, long? catalogGeneration, long policyRevision)
    {
        Reason = reason;
        Category = category;
        Product = product;
        CatalogGeneration = catalogGeneration;
        PolicyRevision = policyRevision;
    }

    public bool IsEligible => Reason == EligibilityReason.Allowed;

    public EligibilityReason Reason { get; }

    public CategoryKey Category { get; }

    public ProductKey Product { get; }

    /// <summary>Snapshot generation the decision was made against (null when no catalog was ready).</summary>
    public long? CatalogGeneration { get; }

    /// <summary>Policy revision the decision was made against. Later phases revalidate if either changed.</summary>
    public long PolicyRevision { get; }

    public string Explanation => EligibilityText.Explain(Reason);

    public override string ToString() => $"{Product} for {Category}: {Reason}";
}

/// <summary>Category-level summary for order generation and the future menu.</summary>
public sealed class CategoryEligibility
{
    internal CategoryEligibility(
        CategoryKey category,
        PolicyMode mode,
        EligibilityReason reason,
        IReadOnlyList<ProductDescriptor> eligibleProducts,
        int unlockedCount,
        int selectedCount,
        IReadOnlyList<ProductKey> inactiveSelections,
        long? catalogGeneration,
        long policyRevision)
    {
        Category = category;
        Mode = mode;
        Reason = reason;
        EligibleProducts = eligibleProducts;
        UnlockedCount = unlockedCount;
        SelectedCount = selectedCount;
        InactiveSelections = inactiveSelections;
        CatalogGeneration = catalogGeneration;
        PolicyRevision = policyRevision;
    }

    public CategoryKey Category { get; }

    public PolicyMode Mode { get; }

    /// <summary><see cref="EligibilityReason.Allowed"/> when at least one product is eligible, otherwise why not.</summary>
    public EligibilityReason Reason { get; }

    /// <summary>True when new mod-managed offers for this category may be generated.</summary>
    public bool HasEligibleProducts => Reason == EligibilityReason.Allowed;

    /// <summary>Eligible products sorted by key.</summary>
    public IReadOnlyList<ProductDescriptor> EligibleProducts { get; }

    public int UnlockedCount { get; }

    public int SelectedCount { get; }

    /// <summary>Selected keys that are currently unusable (unknown, locked, unknown unlock state, or another category). They stay inactive; nothing is substituted.</summary>
    public IReadOnlyList<ProductKey> InactiveSelections { get; }

    public long? CatalogGeneration { get; }

    public long PolicyRevision { get; }

    public string Explanation => EligibilityText.Explain(Reason);
}

/// <summary>Plain-English explanations for the future menu. Display only.</summary>
public static class EligibilityText
{
    public static string Explain(EligibilityReason reason) => reason switch
    {
        EligibilityReason.Allowed => "Allowed by your rules.",
        EligibilityReason.ModDisabled => "Take It Or Leave It is turned off; the game's normal rules apply.",
        EligibilityReason.CatalogNotReady => "Products are not available yet (no save loaded, still loading, or discovery failed).",
        EligibilityReason.InvalidRequest => "The request was not valid.",
        EligibilityReason.UnsupportedCategory => "This category is not supported in the current save or game version.",
        EligibilityReason.ProductUnknown => "This product is not known in the current save.",
        EligibilityReason.WrongCategory => "Wrong category for this order.",
        EligibilityReason.CategoryDisabled => "This category is disabled in your rules.",
        EligibilityReason.ProductLocked => "Not unlocked in this save yet.",
        EligibilityReason.UnlockStateUnknown => "Could not confirm this product is unlocked, so it is not used.",
        EligibilityReason.NoProductsSelected => "\"Selected only\" is on, but no products are selected.",
        EligibilityReason.ProductNotSelected => "Not selected in your rules.",
        EligibilityReason.NoEligibleProducts => "No product in this category is currently eligible.",
        EligibilityReason.InvalidQuantity => "The quantity is not valid.",
        EligibilityReason.ProductBlocked => "Blocked by your rules.",
        EligibilityReason.AncestryUnknown => "Base strain could not be confirmed (the game's mix recipes are not readable), so it is not used.",
        EligibilityReason.EffectMissing => "Does not have the required effect(s).",
        EligibilityReason.EffectExcluded => "Has an excluded effect.",
        EligibilityReason.EffectsUnknown => "Its effects could not be read, so the effect rule cannot be checked.",
        EligibilityReason.EffectRuleContradictory => "The effect rule can never be met (an effect is both required and excluded).",
        EligibilityReason.QualityTooLow => "The item quality is below your minimum.",
        EligibilityReason.QualityUnknown => "The item quality could not be read, so the quality rule cannot be checked.",
        EligibilityReason.SalesPathDisabled => "These sales are switched off in your settings; the game's normal rules apply.",
        _ => reason.ToString(),
    };
}
