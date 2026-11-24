using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Matching;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Policies;

/// <summary>
/// The single source of truth for "may this product be sold for this category?". It is pure and
/// deterministic: inputs are passed explicitly and there is no global state. The menu, player sales
/// and dealer sales all call this instead of re-implementing the rules.
/// <para>Evaluation order (first failing rule wins):</para>
/// <list type="number">
/// <item>mod disabled → <see cref="EligibilityReason.ModDisabled"/></item>
/// <item>invalid keys → <see cref="EligibilityReason.InvalidRequest"/></item>
/// <item>no ready snapshot → <see cref="EligibilityReason.CatalogNotReady"/></item>
/// <item>category not in catalog → <see cref="EligibilityReason.UnsupportedCategory"/></item>
/// <item>product not in catalog → <see cref="EligibilityReason.ProductUnknown"/></item>
/// <item>product of another category → <see cref="EligibilityReason.WrongCategory"/></item>
/// <item>category Disabled → <see cref="EligibilityReason.CategoryDisabled"/></item>
/// <item>not unlocked → <see cref="EligibilityReason.ProductLocked"/> / <see cref="EligibilityReason.UnlockStateUnknown"/></item>
/// <item>allowlist: empty selection → <see cref="EligibilityReason.NoProductsSelected"/>; not selected → <see cref="EligibilityReason.ProductNotSelected"/>;
/// blocklist: blocked → <see cref="EligibilityReason.ProductBlocked"/>. By base strain, a listed product also covers every product the game
/// records as mixed from it; unknown ancestry → <see cref="EligibilityReason.AncestryUnknown"/> unless the product itself is listed.</item>
/// <item>effect rule → <see cref="EligibilityReason.EffectRuleContradictory"/> / <see cref="EligibilityReason.EffectsUnknown"/> /
/// <see cref="EligibilityReason.EffectExcluded"/> / <see cref="EligibilityReason.EffectMissing"/></item>
/// </list>
/// Quality is not a product property: <see cref="CheckQuality"/> judges physical stacks with the same rule.
/// </summary>
public static class SaleEligibilityPolicy
{
    /// <summary>Evaluates whether <paramref name="product"/> may satisfy a request for <paramref name="requestedCategory"/>.</summary>
    public static EligibilityDecision Evaluate(
        PolicyConfiguration policy,
        ProductCatalogSnapshot? snapshot,
        CategoryKey requestedCategory,
        ProductKey product)
    {
        policy ??= PolicyConfiguration.Default;
        var generation = snapshot?.Generation;
        EligibilityDecision Decide(EligibilityReason reason) => new(reason, requestedCategory, product, generation, policy.Revision);

        if (!policy.Enabled)
        {
            return Decide(EligibilityReason.ModDisabled);
        }

        if (!requestedCategory.IsValid || !product.IsValid)
        {
            return Decide(EligibilityReason.InvalidRequest);
        }

        if (snapshot is null)
        {
            return Decide(EligibilityReason.CatalogNotReady);
        }

        if (!snapshot.HasCategory(requestedCategory))
        {
            return Decide(EligibilityReason.UnsupportedCategory);
        }

        if (!snapshot.TryGetProduct(product, out var descriptor))
        {
            return Decide(EligibilityReason.ProductUnknown);
        }

        if (!ProductMatcher.MatchesCategory(descriptor, requestedCategory))
        {
            return Decide(EligibilityReason.WrongCategory);
        }

        var rule = policy.GetEffectivePolicy(requestedCategory);
        if (rule.Mode == PolicyMode.Disabled)
        {
            return Decide(EligibilityReason.CategoryDisabled);
        }

        switch (descriptor.UnlockState)
        {
            case UnlockState.Locked:
                return Decide(EligibilityReason.ProductLocked);
            case UnlockState.Unlocked:
                break;
            default:
                return Decide(EligibilityReason.UnlockStateUnknown);
        }

        var selection = CheckSelection(rule, snapshot, descriptor);
        if (selection != EligibilityReason.Allowed)
        {
            return Decide(selection);
        }

        return Decide(rule.Effects.Check(descriptor.EffectIds, descriptor.EffectsKnown));
    }

    /// <summary>
    /// Quality step of the pipeline for one physical stack: the category's minimum (if any) against the stack's tier.
    /// Unknown quality fails only when a minimum is set.
    /// </summary>
    public static EligibilityReason CheckQuality(PolicyConfiguration policy, CategoryKey category, QualityTier? quality)
    {
        var minimum = (policy ?? PolicyConfiguration.Default).GetEffectivePolicy(category).MinimumQuality;
        if (minimum is null)
        {
            return EligibilityReason.Allowed;
        }

        if (quality is null)
        {
            return EligibilityReason.QualityUnknown;
        }

        return quality.Value >= minimum.Value ? EligibilityReason.Allowed : EligibilityReason.QualityTooLow;
    }

    private static EligibilityReason CheckSelection(CategoryPolicy rule, ProductCatalogSnapshot snapshot, ProductDescriptor product)
    {
        switch (rule.Mode)
        {
            case PolicyMode.SelectedOnly:
                if (rule.SelectedProducts.Count == 0)
                {
                    return EligibilityReason.NoProductsSelected;
                }

                if (rule.IsSelected(product.Key))
                {
                    return EligibilityReason.Allowed;
                }

                if (rule.Match == SelectionMatch.BaseStrain)
                {
                    if (rule.SelectedProducts.Any(s => snapshot.IsSameOrDescendantOf(product.Key, s)))
                    {
                        return EligibilityReason.Allowed;
                    }

                    if (!product.AncestryKnown)
                    {
                        return EligibilityReason.AncestryUnknown;
                    }
                }

                return EligibilityReason.ProductNotSelected;

            case PolicyMode.Blocklist:
                if (rule.IsBlocked(product.Key))
                {
                    return EligibilityReason.ProductBlocked;
                }

                if (rule.Match == SelectionMatch.BaseStrain && rule.BlockedProducts.Count > 0)
                {
                    if (rule.BlockedProducts.Any(b => snapshot.IsSameOrDescendantOf(product.Key, b)))
                    {
                        return EligibilityReason.ProductBlocked;
                    }

                    if (!product.AncestryKnown)
                    {
                        // It might descend from a blocked strain: never sell it on a guess.
                        return EligibilityReason.AncestryUnknown;
                    }
                }

                return EligibilityReason.Allowed;

            default:
                return EligibilityReason.Allowed;
        }
    }

    /// <summary>Summarises a category: is anything eligible, which products, and which selections are inactive.</summary>
    public static CategoryEligibility EvaluateCategory(PolicyConfiguration policy, ProductCatalogSnapshot? snapshot, CategoryKey category)
    {
        policy ??= PolicyConfiguration.Default;
        var rule = policy.GetEffectivePolicy(category);
        var empty = new ProductDescriptor[0];
        var noKeys = new ProductKey[0];

        CategoryEligibility Summary(EligibilityReason reason, IReadOnlyList<ProductDescriptor> eligible, int unlocked, IReadOnlyList<ProductKey> inactive)
            => new(category, rule.Mode, reason, eligible, unlocked, rule.ActiveList.Count, inactive, snapshot?.Generation, policy.Revision);

        if (!policy.Enabled)
        {
            return Summary(EligibilityReason.ModDisabled, empty, 0, noKeys);
        }

        if (!category.IsValid)
        {
            return Summary(EligibilityReason.InvalidRequest, empty, 0, noKeys);
        }

        if (snapshot is null)
        {
            return Summary(EligibilityReason.CatalogNotReady, empty, 0, noKeys);
        }

        if (!snapshot.HasCategory(category))
        {
            return Summary(EligibilityReason.UnsupportedCategory, empty, 0, noKeys);
        }

        var unlocked = snapshot.GetUnlockedProducts(category);
        var inactive = rule.ActiveList
            .Where(k => !snapshot.TryGetProduct(k, out var p) || !p.IsUnlocked || p.Category != category)
            .ToArray();

        if (rule.Mode == PolicyMode.Disabled)
        {
            return Summary(EligibilityReason.CategoryDisabled, empty, unlocked.Count, inactive);
        }

        if (rule.Mode == PolicyMode.SelectedOnly && rule.SelectedProducts.Count == 0)
        {
            return Summary(EligibilityReason.NoProductsSelected, empty, unlocked.Count, inactive);
        }

        if (rule.Effects.IsContradictory)
        {
            return Summary(EligibilityReason.EffectRuleContradictory, empty, unlocked.Count, inactive);
        }

        var eligible = unlocked.Where(p => Evaluate(policy, snapshot, category, p.Key).IsEligible).ToArray();
        return Summary(eligible.Length > 0 ? EligibilityReason.Allowed : EligibilityReason.NoEligibleProducts, eligible, unlocked.Count, inactive);
    }

    /// <summary>Evaluates every category in the snapshot (sorted by key). Empty when no catalog is ready.</summary>
    public static IReadOnlyList<CategoryEligibility> EvaluateAll(PolicyConfiguration policy, ProductCatalogSnapshot? snapshot)
        => snapshot is null
            ? new CategoryEligibility[0]
            : snapshot.Categories.Select(c => EvaluateCategory(policy, snapshot, c.Key)).ToArray();
}
