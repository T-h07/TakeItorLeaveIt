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
/// <item>SelectedOnly: empty selection → <see cref="EligibilityReason.NoProductsSelected"/>; not selected → <see cref="EligibilityReason.ProductNotSelected"/></item>
/// </list>
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

        if (rule.Mode == PolicyMode.SelectedOnly)
        {
            if (rule.SelectedProducts.Count == 0)
            {
                return Decide(EligibilityReason.NoProductsSelected);
            }

            if (!rule.IsSelected(product))
            {
                return Decide(EligibilityReason.ProductNotSelected);
            }
        }

        return Decide(EligibilityReason.Allowed);
    }

    /// <summary>Summarises a category: is anything eligible, which products, and which selections are inactive.</summary>
    public static CategoryEligibility EvaluateCategory(PolicyConfiguration policy, ProductCatalogSnapshot? snapshot, CategoryKey category)
    {
        policy ??= PolicyConfiguration.Default;
        var rule = policy.GetEffectivePolicy(category);
        var empty = new ProductDescriptor[0];
        var noKeys = new ProductKey[0];

        CategoryEligibility Summary(EligibilityReason reason, IReadOnlyList<ProductDescriptor> eligible, int unlocked, IReadOnlyList<ProductKey> inactive)
            => new(category, rule.Mode, reason, eligible, unlocked, rule.SelectedProducts.Count, inactive, snapshot?.Generation, policy.Revision);

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
        var inactive = rule.SelectedProducts
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

        var eligible = rule.Mode == PolicyMode.SelectedOnly
            ? unlocked.Where(p => rule.IsSelected(p.Key)).ToArray()
            : unlocked.ToArray();

        return Summary(eligible.Length > 0 ? EligibilityReason.Allowed : EligibilityReason.NoEligibleProducts, eligible, unlocked.Count, inactive);
    }

    /// <summary>Evaluates every category in the snapshot (sorted by key). Empty when no catalog is ready.</summary>
    public static IReadOnlyList<CategoryEligibility> EvaluateAll(PolicyConfiguration policy, ProductCatalogSnapshot? snapshot)
        => snapshot is null
            ? new CategoryEligibility[0]
            : snapshot.Categories.Select(c => EvaluateCategory(policy, snapshot, c.Key)).ToArray();
}
