using System;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Product;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Reads the game's own unit price of a product for the fair-price rule: <c>Registry.GetItem(id)</c> as a
/// <c>ProductDefinition</c>, then <c>ProductDefinition.Price</c> (which the game forwards to
/// <c>ProductManager.GetPrice</c>; the same getter <c>Customer.TryGenerateContract</c> prices offers with, see
/// docs/PHASE_04_PATCH_ANALYSIS.md §9). Read-only: it never sets a price or touches a definition. Any failure
/// means "price unknown", which the rule treats as "no substitution".
/// </summary>
internal static class GamePrices
{
    public static readonly IProductPriceSource Source = new PriceLookup(Lookup);

    private static decimal? Lookup(ProductKey product)
    {
        try
        {
            var definition = Registry.GetItem(product.Value)?.TryCast<ProductDefinition>();
            if (definition is null)
            {
                return null;
            }

            var price = definition.Price;
            return float.IsNaN(price) || float.IsInfinity(price) || price < 0 || price > 1e9f ? null : (decimal)price;
        }
        catch (Exception ex)
        {
            GameSales.Once("price-read-error", $"Could not read a product price; that product is not used as a substitute. {LogText.Describe(ex)}", warning: true);
            return null;
        }
    }
}
