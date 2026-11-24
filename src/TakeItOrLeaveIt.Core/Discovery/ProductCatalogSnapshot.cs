using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Discovery;

/// <summary>
/// Immutable product catalog for exactly one save session and one discovery pass. Consumers hold a
/// reference to a complete snapshot; a refresh publishes a new snapshot rather than mutating this one,
/// so nobody ever observes a partially refreshed list.
/// </summary>
public sealed class ProductCatalogSnapshot
{
    private static readonly IReadOnlyList<ProductDescriptor> NoProducts = Array.Empty<ProductDescriptor>();

    private readonly Dictionary<ProductKey, ProductDescriptor> _products;
    private readonly Dictionary<CategoryKey, CategoryDescriptor> _categories;
    private readonly Dictionary<CategoryKey, IReadOnlyList<ProductDescriptor>> _byCategory;
    private readonly Dictionary<ProductKey, HashSet<ProductKey>> _ancestors;
    private readonly Dictionary<ProductKey, IReadOnlyList<ProductKey>> _baseStrains;
    private readonly Dictionary<CategoryKey, IReadOnlyList<string>> _effects;

    private ProductCatalogSnapshot(
        SaveSession session,
        long generation,
        Dictionary<CategoryKey, CategoryDescriptor> categories,
        Dictionary<ProductKey, ProductDescriptor> products,
        IReadOnlyList<string> issues)
    {
        Session = session;
        Generation = generation;
        _categories = categories;
        _products = products;
        Issues = issues;
        Categories = categories.Values.OrderBy(c => c.Key).ToArray();
        _byCategory = products.Values
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProductDescriptor>)g.OrderBy(p => p.Key).ToArray());
        _effects = products.Values
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.SelectMany(p => p.EffectIds).Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToArray());

        // Transitive ancestry over the recorded parents (cycles are tolerated: a product is never its own ancestor).
        _ancestors = new Dictionary<ProductKey, HashSet<ProductKey>>();
        _baseStrains = new Dictionary<ProductKey, IReadOnlyList<ProductKey>>();
        foreach (var product in products.Values)
        {
            var seen = new HashSet<ProductKey>();
            var stack = new Stack<ProductKey>(product.Parents);
            while (stack.Count > 0)
            {
                var next = stack.Pop();
                if (next == product.Key || !seen.Add(next))
                {
                    continue;
                }

                if (products.TryGetValue(next, out var parent))
                {
                    foreach (var grand in parent.Parents)
                    {
                        stack.Push(grand);
                    }
                }
            }

            _ancestors[product.Key] = seen;
        }

        foreach (var product in products.Values)
        {
            var roots = _ancestors[product.Key]
                .Where(a => !products.TryGetValue(a, out var d) || d.Parents.Count == 0)
                .OrderBy(a => a)
                .ToList();
            _baseStrains[product.Key] = product.Parents.Count == 0 || roots.Count == 0 ? new[] { product.Key } : roots.ToArray();
        }
    }

    /// <summary>Save session this catalog belongs to.</summary>
    public SaveSession Session { get; }

    /// <summary>Monotonically increasing number, unique per published snapshot.</summary>
    public long Generation { get; }

    /// <summary>Categories, sorted by key.</summary>
    public IReadOnlyList<CategoryDescriptor> Categories { get; }

    /// <summary>Non-fatal problems found while building (skipped/duplicate entries).</summary>
    public IReadOnlyList<string> Issues { get; }

    public int ProductCount => _products.Count;

    public bool HasCategory(CategoryKey category) => _categories.ContainsKey(category);

    public bool TryGetCategory(CategoryKey category, out CategoryDescriptor descriptor)
        => _categories.TryGetValue(category, out descriptor!);

    public bool TryGetProduct(ProductKey key, out ProductDescriptor product) => _products.TryGetValue(key, out product!);

    /// <summary>All products of a category (any unlock state), sorted by key.</summary>
    public IReadOnlyList<ProductDescriptor> GetProducts(CategoryKey category)
        => _byCategory.TryGetValue(category, out var list) ? list : NoProducts;

    /// <summary>
    /// True when <paramref name="ancestor"/> is <paramref name="product"/> itself or one of its recorded ancestors
    /// (transitive, through the game's mix recipes). Never true for unknown products.
    /// </summary>
    public bool IsSameOrDescendantOf(ProductKey product, ProductKey ancestor)
        => product == ancestor
            ? _products.ContainsKey(product)
            : _ancestors.TryGetValue(product, out var set) && set.Contains(ancestor);

    /// <summary>Recorded ancestors of a product (transitive, excluding itself), sorted. Empty when unknown.</summary>
    public IReadOnlyList<ProductKey> GetAncestors(ProductKey product)
        => _ancestors.TryGetValue(product, out var set) ? set.OrderBy(a => a).ToArray() : Array.Empty<ProductKey>();

    /// <summary>
    /// Base strains of a product: its ancestors with no recorded parent, or the product itself when it has no parent
    /// (or only a cycle). Single product when ancestry is unknown.
    /// </summary>
    public IReadOnlyList<ProductKey> GetBaseStrains(ProductKey product)
        => _baseStrains.TryGetValue(product, out var roots) ? roots : new[] { product };

    /// <summary>Effect ids that occur on the category's discovered products, sorted.</summary>
    public IReadOnlyList<string> GetEffects(CategoryKey category)
        => _effects.TryGetValue(category, out var list) ? list : Array.Empty<string>();

    /// <summary>Unlocked products of a category, sorted by key.</summary>
    public IReadOnlyList<ProductDescriptor> GetUnlockedProducts(CategoryKey category)
        => GetProducts(category).Where(p => p.IsUnlocked).ToArray();

    /// <summary>
    /// Builds a snapshot from raw discovery output. Invalid or conflicting entries are skipped and
    /// recorded in <see cref="Issues"/>; a duplicate key with identical data is merged silently.
    /// A product whose category was not listed adds that category.
    /// </summary>
    internal static ProductCatalogSnapshot Build(
        SaveSession session,
        long generation,
        IEnumerable<CategoryDescriptor> categories,
        IEnumerable<ProductDescriptor> products,
        IEnumerable<string>? sourceIssues)
    {
        var issues = new List<string>(sourceIssues ?? Array.Empty<string>());
        var categoryMap = new Dictionary<CategoryKey, CategoryDescriptor>();
        foreach (var category in categories ?? Array.Empty<CategoryDescriptor>())
        {
            if (category is null)
            {
                issues.Add("Skipped a null category.");
                continue;
            }

            if (!categoryMap.ContainsKey(category.Key))
            {
                categoryMap.Add(category.Key, category);
            }
        }

        var productMap = new Dictionary<ProductKey, ProductDescriptor>();
        var conflicted = new HashSet<ProductKey>();
        foreach (var product in products ?? Array.Empty<ProductDescriptor>())
        {
            if (product is null)
            {
                issues.Add("Skipped a null product.");
                continue;
            }

            if (conflicted.Contains(product.Key))
            {
                continue;
            }

            if (productMap.TryGetValue(product.Key, out var existing))
            {
                if (existing.Category == product.Category && existing.RecipeFingerprint == product.RecipeFingerprint)
                {
                    // Same product reported twice (e.g. by two sources): merge unlock states, see Merge().
                    if (existing.UnlockState != product.UnlockState)
                    {
                        productMap[product.Key] = existing.WithUnlockState(Merge(existing.UnlockState, product.UnlockState));
                    }

                    continue;
                }

                // Same ID, different data: identity is ambiguous, so the product is excluded entirely (fail closed).
                productMap.Remove(product.Key);
                conflicted.Add(product.Key);
                issues.Add($"Product '{product.Key}' was reported with conflicting data and was excluded.");
                continue;
            }

            productMap.Add(product.Key, product);
            if (!categoryMap.ContainsKey(product.Category))
            {
                categoryMap.Add(product.Category, new CategoryDescriptor(product.Category));
            }
        }

        return new ProductCatalogSnapshot(session, generation, categoryMap, productMap, issues);
    }

    /// <summary>Unlocked wins over Locked (a discovery source saw it unlocked); Unknown never upgrades anything.</summary>
    private static UnlockState Merge(UnlockState a, UnlockState b)
    {
        if (a == UnlockState.Unlocked || b == UnlockState.Unlocked)
        {
            return UnlockState.Unlocked;
        }

        return a == UnlockState.Locked || b == UnlockState.Locked ? UnlockState.Locked : UnlockState.Unknown;
    }
}
