using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Synthetic discovery source. Product names here are test data only, not game facts.</summary>
internal sealed class FakeDiscoverySource : IProductDiscoverySource
{
    public Func<DiscoveryResult> Next { get; set; } = () => DiscoveryResult.Success(Array.Empty<CategoryDescriptor>(), Array.Empty<ProductDescriptor>());

    public int Calls { get; private set; }

    public DiscoveryResult Discover()
    {
        Calls++;
        return Next();
    }
}

internal static class Catalog
{
    public static readonly CategoryKey Weed = CategoryKey.Create("marijuana");
    public static readonly CategoryKey Meth = CategoryKey.Create("methamphetamine");
    public static readonly CategoryKey Cocaine = CategoryKey.Create("cocaine");

    public static readonly ProductKey OgKush = ProductKey.Create("ogkush");
    public static readonly ProductKey SourDiesel = ProductKey.Create("sourdiesel");
    public static readonly ProductKey Gdp = ProductKey.Create("granddaddypurple");
    public static readonly ProductKey Meth1 = ProductKey.Create("meth");
    public static readonly ProductKey Coke1 = ProductKey.Create("cocaine");

    public static ProductDescriptor Product(ProductKey key, CategoryKey category, UnlockState state = UnlockState.Unlocked, string? name = null, params string[] effects)
        => new(key, category, name ?? key.Value, state, effects);

    public static DiscoveryResult Result(params ProductDescriptor[] products)
        => DiscoveryResult.Success(
            new[] { new CategoryDescriptor(Weed, "Weed"), new CategoryDescriptor(Meth, "Meth"), new CategoryDescriptor(Cocaine, "Cocaine") },
            products);

    /// <summary>Weed: OG Kush + Sour Diesel unlocked, GDP locked. Meth + cocaine unlocked.</summary>
    public static DiscoveryResult Standard() => Result(
        Product(OgKush, Weed),
        Product(SourDiesel, Weed),
        Product(Gdp, Weed, UnlockState.Locked),
        Product(Meth1, Meth),
        Product(Coke1, Cocaine));

    /// <summary>Builds a Ready snapshot through the real service.</summary>
    public static ProductCatalogSnapshot Snapshot(DiscoveryResult result)
    {
        var source = new FakeDiscoverySource { Next = () => result };
        var service = new ProductCatalogService(source);
        var session = service.BeginSave("save-test");
        service.Refresh(session);
        return service.State.Snapshot ?? throw new InvalidOperationException($"Fixture catalog not ready: {service.State}");
    }

    public static ProductCatalogSnapshot Snapshot(params ProductDescriptor[] products) => Snapshot(Result(products));

    public static ProductCatalogSnapshot AllWeedUnlocked() => Snapshot(
        Product(OgKush, Weed),
        Product(SourDiesel, Weed),
        Product(Gdp, Weed),
        Product(Meth1, Meth),
        Product(Coke1, Cocaine));

    public static IEnumerable<ProductKey> Keys(this IEnumerable<ProductDescriptor> products) => products.Select(p => p.Key);
}
