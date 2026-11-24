using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Game.Discovery;

/// <summary>
/// Reads the active save's discovered products from the game. <b>Read-only.</b>
/// <para>Game members used (PROBABLE, from S1API source; to be confirmed in game by P01-T03):
/// <c>ScheduleOne.Product.ProductManager.DiscoveredProducts</c> (list of ProductDefinition),
/// <c>ItemDefinition.ID</c> / <c>.Name</c>, <c>ProductDefinition.DrugType</c> (EDrugType) and
/// <c>ProductDefinition.Properties</c> (list of Effect with <c>.ID</c>).</para>
/// <para>Every product in DiscoveredProducts is reported as <see cref="UnlockState.Unlocked"/>. Products the
/// save has not discovered are simply absent, so they can never become eligible (fail closed). Categories
/// are the drug types that actually occur; no category is invented.</para>
/// <para>Phase 05 ancestry (VERIFIED members, see PHASE_05_ANALYSIS.md section 1): the instance field
/// <c>ProductManager.mixRecipes</c> (list of StationRecipe). Each recipe gives the edges <i>input product → output
/// product</i>: <c>StationRecipe.Product.Item.ID</c> is the output and every ingredient item that is a product
/// definition (has a <c>DrugType</c>) is a parent; mixer ingredients are not products and are ignored. Names are
/// never used. When the list cannot be read every product is reported with unknown ancestry; a recipe that cannot
/// be read in full makes only its output's ancestry unknown.</para>
/// </summary>
internal sealed class GameProductDiscoverySource : IProductDiscoverySource
{
    internal const string ProductManagerType = "ScheduleOne.Product.ProductManager";

    /// <summary>Reads the discovered-products list object, or explains why it cannot.</summary>
    public static bool TryGetDiscoveredList(out object? list, out string? problem, out bool unsupported)
    {
        list = null;
        problem = null;
        unsupported = false;

        var managerType = GameReflection.FindGameType(ProductManagerType);
        if (managerType is null)
        {
            problem = $"Type {ProductManagerType} not found.";
            unsupported = true;
            return false;
        }

        if (GameReflection.TryGetStatic(managerType, "DiscoveredProducts", out list))
        {
            return Present(list, ref problem);
        }

        // Fallback: an instance member on the manager singleton.
        if (GameReflection.TryGetStatic(managerType, "Instance", out var instance) &&
            GameReflection.TryGet(instance, "DiscoveredProducts", out list))
        {
            return Present(list, ref problem);
        }

        if (instance is null && GameReflection.HasMember(managerType, "Instance", isStatic: true))
        {
            problem = "ProductManager instance is not available yet.";
        }
        else
        {
            problem = "ProductManager.DiscoveredProducts not found.";
            unsupported = true;
        }

        return false;

        static bool Present(object? value, ref string? why)
        {
            if (value is not null)
            {
                return true;
            }

            why = "ProductManager.DiscoveredProducts is null (not initialised yet).";
            return false;
        }
    }

    public DiscoveryResult Discover()
    {
        if (!TryGetDiscoveredList(out var listObject, out var problem, out var unsupported))
        {
            return DiscoveryResult.Failure(problem ?? "Discovered products unavailable.", unsupported);
        }

        var items = GameReflection.ToList(listObject);
        if (items is null)
        {
            return DiscoveryResult.Failure("DiscoveredProducts is not a readable list.", unsupported: true);
        }

        var categories = new Dictionary<CategoryKey, CategoryDescriptor>();
        var products = new List<ProductDescriptor>(items.Count);
        var issues = new List<string>();
        var ancestry = ReadAncestry(issues);

        for (var index = 0; index < items.Count; index++)
        {
            var definition = items[index];
            if (definition is null)
            {
                issues.Add($"Discovered product #{index} is null; skipped.");
                continue;
            }

            if (!GameReflection.TryGet(definition, "ID", out var idObj) || !ProductKey.TryCreate(idObj as string, out var key))
            {
                issues.Add($"Discovered product #{index} has no valid ID; skipped.");
                continue;
            }

            if (!GameReflection.TryGet(definition, "DrugType", out var drugType) || drugType is null ||
                !CategoryKey.TryCreate(drugType.ToString(), out var category))
            {
                issues.Add($"Product '{key}' has no readable DrugType; skipped.");
                continue;
            }

            if (!categories.ContainsKey(category))
            {
                categories.Add(category, new CategoryDescriptor(category, drugType.ToString()));
            }

            GameReflection.TryGet(definition, "Name", out var nameObj);
            var effects = ReadEffectIds(definition, out var effectsKnown);
            IEnumerable<ProductKey>? parents = null;
            var ancestryKnown = ancestry is not null && !ancestry.Unreadable.Contains(key);
            if (ancestry is not null && ancestry.Parents.TryGetValue(key, out var found))
            {
                parents = found;
            }

            products.Add(new ProductDescriptor(key, category, nameObj as string, UnlockState.Unlocked, effects,
                parents: parents, ancestryKnown: ancestryKnown, effectsKnown: effectsKnown));
        }

        return DiscoveryResult.Success(new List<CategoryDescriptor>(categories.Values), products, issues);
    }

    private static IEnumerable<string> ReadEffectIds(object definition, out bool known)
    {
        var ids = new List<string>();
        known = false;
        if (!GameReflection.TryGet(definition, "Properties", out var effects) || GameReflection.ToList(effects) is not { } list)
        {
            return ids;
        }

        known = true;
        foreach (var effect in list)
        {
            if (GameReflection.TryGet(effect, "ID", out var id) && id is string s && !string.IsNullOrWhiteSpace(s))
            {
                ids.Add(s.Trim());
            }
            else
            {
                known = false; // an unreadable effect could be an excluded one: the product never passes an effect rule
            }
        }

        return ids;
    }

    private sealed class Ancestry
    {
        public Dictionary<ProductKey, List<ProductKey>> Parents { get; } = new();

        public HashSet<ProductKey> Unreadable { get; } = new();
    }

    /// <summary>Reads the recorded mix recipes, or null (with an issue) when they cannot be read at all.</summary>
    private static Ancestry? ReadAncestry(List<string> issues)
    {
        var managerType = GameReflection.FindGameType(ProductManagerType);
        if (managerType is null || !GameReflection.TryGetStatic(managerType, "Instance", out var manager) || manager is null)
        {
            issues.Add("Mix recipes: ProductManager instance not available; ancestry unknown for every product (base-strain rules match only the selected product itself).");
            return null;
        }

        if (!GameReflection.TryGet(manager, "mixRecipes", out var raw) || GameReflection.ToList(raw) is not { } recipes)
        {
            issues.Add("Mix recipes: ProductManager.mixRecipes not readable; ancestry unknown for every product.");
            return null;
        }

        var result = new Ancestry();
        var skipped = 0;
        for (var index = 0; index < recipes.Count; index++)
        {
            var recipe = recipes[index];
            if (!GameReflection.TryGet(recipe, "Product", out var output) ||
                !GameReflection.TryGet(output, "Item", out var outputItem) ||
                !GameReflection.TryGet(outputItem, "ID", out var outputId) ||
                !ProductKey.TryCreate(outputId as string, out var child))
            {
                skipped++;
                continue;
            }

            if (!GameReflection.TryGet(recipe, "Ingredients", out var rawIngredients) || GameReflection.ToList(rawIngredients) is not { } ingredients)
            {
                result.Unreadable.Add(child);
                skipped++;
                continue;
            }

            var parents = new List<ProductKey>();
            var complete = true;
            foreach (var ingredient in ingredients)
            {
                if (!GameReflection.TryGet(ingredient, "Item", out var item) || item is null)
                {
                    complete = false;
                    continue;
                }

                if (!GameReflection.TryGet(item, "DrugType", out _))
                {
                    continue; // a mixer (not a product definition)
                }

                if (GameReflection.TryGet(item, "ID", out var parentId) && ProductKey.TryCreate(parentId as string, out var parent))
                {
                    parents.Add(parent);
                }
                else
                {
                    complete = false;
                }
            }

            if (!complete)
            {
                result.Unreadable.Add(child);
                skipped++;
            }

            if (!result.Parents.TryGetValue(child, out var list))
            {
                list = new List<ProductKey>();
                result.Parents.Add(child, list);
            }

            foreach (var parent in parents)
            {
                if (parent != child && !list.Contains(parent))
                {
                    list.Add(parent);
                }
            }
        }

        if (skipped > 0)
        {
            issues.Add($"Mix recipes: {skipped} of {recipes.Count} recipe(s) could not be read in full; affected products have unknown ancestry.");
        }

        return result;
    }
}
