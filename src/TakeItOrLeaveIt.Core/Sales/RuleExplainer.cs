using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>
/// Player-facing explanation of why an accepted order does not take a product (Phase 05). Display only: the
/// decision is always the order's acceptance-time snapshot. The explanation checks, in order, what the snapshot
/// itself records (price, requested-only mixing) and then the live rules of the order's scope; when the live rules
/// would permit the product it says the product was not permitted at acceptance, so it never contradicts the
/// decision.
/// </summary>
public static class RuleExplainer
{
    public static string ExplainNotAllowed(CategoryOrder order, PolicyConfiguration? live, ProductCatalogSnapshot? catalog, ProductKey product)
    {
        var category = DeliveryValidator.CategoryName(order.Category, catalog);
        if (order.PricingBasis == PricingBasis.Priced && order.QuotedValue is decimal quote)
        {
            if (order.TryGetUnitPrice(product, out var price) && price * order.RequiredUnits < quote)
            {
                return "This product is worth less than the agreed quote.";
            }
        }

        if (order.Mixing == MixingMode.RequestedOnly && !order.RequestedProducts.Contains(product))
        {
            return "This order only accepts the product the customer asked for.";
        }

        if (order.PricingBasis == PricingBasis.RequestedOnly && !order.RequestedProducts.Contains(product))
        {
            return "Prices could not be compared for this order, so only the requested product is accepted.";
        }

        if (live is null || catalog is null)
        {
            return $"One of the products is not allowed for {category} orders (Take It settings).";
        }

        var rule = live.GetEffectivePolicy(order.Category);
        var decision = SaleEligibilityPolicy.Evaluate(live.WithEnabled(true), catalog, order.Category, product);
        return decision.Reason switch
        {
            EligibilityReason.ProductBlocked => $"This product is blocked by your {category} rules.",
            EligibilityReason.ProductNotSelected => rule.Match == SelectionMatch.BaseStrain
                ? $"This product isn't from a strain on your {category} allowed list."
                : "This recipe isn't on your allowed list.",
            EligibilityReason.NoProductsSelected => $"Nothing is selected on your {category} allowed list.",
            EligibilityReason.AncestryUnknown => "This product's base strain could not be confirmed, so it is not used.",
            EligibilityReason.EffectMissing => "This product doesn't have the required effect.",
            EligibilityReason.EffectExcluded => "This product has an effect your rules exclude.",
            EligibilityReason.EffectsUnknown => "This product's effects could not be read, so your effect rule cannot be checked.",
            EligibilityReason.EffectRuleContradictory => $"Your {category} effect rule can never be met.",
            EligibilityReason.CategoryDisabled => $"{category} is disabled in your rules.",
            EligibilityReason.ProductLocked or EligibilityReason.UnlockStateUnknown => "This product is not unlocked.",
            EligibilityReason.WrongCategory => $"This customer only wants {category}.",
            EligibilityReason.Allowed => "This product was not permitted when the order was accepted.",
            _ => decision.Explanation,
        };
    }
}
