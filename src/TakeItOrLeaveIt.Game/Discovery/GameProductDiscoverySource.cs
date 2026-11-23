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
/// are the drug types that actually occur; no category is invented. Base products of mixes are not
/// inferred.</para>
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
            products.Add(new ProductDescriptor(key, category, nameObj as string, UnlockState.Unlocked, ReadEffectIds(definition)));
        }

        return DiscoveryResult.Success(new List<CategoryDescriptor>(categories.Values), products, issues);
    }

    private static IEnumerable<string> ReadEffectIds(object definition)
    {
        var ids = new List<string>();
        if (!GameReflection.TryGet(definition, "Properties", out var effects))
        {
            return ids;
        }

        foreach (var effect in GameReflection.ToList(effects) ?? Array.Empty<object?>())
        {
            if (GameReflection.TryGet(effect, "ID", out var id) && id is string s && !string.IsNullOrWhiteSpace(s))
            {
                ids.Add(s.Trim());
            }
        }

        return ids;
    }
}
