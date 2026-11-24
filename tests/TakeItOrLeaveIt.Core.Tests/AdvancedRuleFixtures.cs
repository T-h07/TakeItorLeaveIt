using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 05 fixture catalog with recorded mix ancestry. Every relationship comes from the descriptors' Parents (the
/// stand-in for the game's mix recipes), never from names: the mix keys below deliberately do not share a prefix
/// with their parents. Test data only, not game facts.
/// </summary>
internal static class Advanced
{
    public const string Calming = "calming";
    public const string Energizing = "energizing";
    public const string Paranoia = "paranoia";

    /// <summary>OG Kush + a mixer.</summary>
    public static readonly ProductKey MixA = ProductKey.Create("zz_alpha");

    /// <summary>MixA + a mixer (OG Kush is a transitive ancestor).</summary>
    public static readonly ProductKey MixB = ProductKey.Create("aa_bravo");

    /// <summary>Recorded with two parents (OG Kush and Sour Diesel).</summary>
    public static readonly ProductKey Hybrid = ProductKey.Create("mm_hybrid");

    /// <summary>Sour Diesel + a mixer.</summary>
    public static readonly ProductKey MixS = ProductKey.Create("qq_sierra");

    /// <summary>Recipes could not be read for this one: ancestry unknown.</summary>
    public static readonly ProductKey Orphan = ProductKey.Create("ogkush_lookalike");

    /// <summary>Effects could not be read for this one.</summary>
    public static readonly ProductKey Blank = ProductKey.Create("kk_blank");

    /// <summary>Every fixture product at one unit price, so any permitted product is a fair substitute.</summary>
    public static readonly TakeItOrLeaveIt.Core.Sales.ProductPrices Prices = new(new[] { OgKush, SourDiesel, Gdp, MixA, MixB, Hybrid, MixS, Orphan, Blank, Meth1, Coke1 }
        .Select(p => new System.Collections.Generic.KeyValuePair<ProductKey, decimal>(p, 40m)));

    public static ProductDescriptor Base(ProductKey key, params string[] effects)
        => new(key, Weed, key.Value, UnlockState.Unlocked, effects, parents: null, ancestryKnown: true);

    public static ProductDescriptor Mix(ProductKey key, ProductKey[] parents, params string[] effects)
        => new(key, Weed, key.Value, UnlockState.Unlocked, effects, parents: parents, ancestryKnown: true);

    public static ProductCatalogSnapshot Snapshot() => Catalog.Snapshot(
        Base(OgKush, Calming),
        Base(SourDiesel, Energizing),
        Base(Gdp),
        Mix(MixA, new[] { OgKush }, Calming, Energizing),
        Mix(MixB, new[] { MixA }, Paranoia),
        Mix(Hybrid, new[] { OgKush, SourDiesel }, Calming),
        Mix(MixS, new[] { SourDiesel }, Energizing, Paranoia),
        new ProductDescriptor(Orphan, Weed, "OG Kush (lookalike)", UnlockState.Unlocked, new[] { Calming }, ancestryKnown: false),
        new ProductDescriptor(Blank, Weed, "Blank", UnlockState.Unlocked, null, parents: new[] { Gdp }, ancestryKnown: true, effectsKnown: false),
        Product(Meth1, Meth),
        Product(Coke1, Cocaine));

    public static PolicyConfiguration With(CategoryPolicy weed) => PolicyConfiguration.Default.WithCategory(Weed, weed);

    public static CategoryPolicy Allow(SelectionMatch match, params ProductKey[] products)
        => new(PolicyMode.SelectedOnly, products, match, null, null, null);

    public static CategoryPolicy Block(SelectionMatch match, params ProductKey[] products)
        => new(PolicyMode.Blocklist, null, match, products, null, null);

    public static CategoryPolicy Effects(EffectMatch match, string[] required, params string[] excluded)
        => new(PolicyMode.AnyUnlocked, null, SelectionMatch.ExactProduct, null, new EffectRule(match, required, excluded), null);

    public static CategoryPolicy MinQuality(QualityTier tier)
        => new(PolicyMode.AnyUnlocked, null, SelectionMatch.ExactProduct, null, null, tier);

    public static EligibilityReason Reason(PolicyConfiguration policy, ProductKey product, ProductCatalogSnapshot? catalog = null)
        => SaleEligibilityPolicy.Evaluate(policy, catalog ?? Snapshot(), Weed, product).Reason;

    public static ProductKey[] Eligible(PolicyConfiguration policy, ProductCatalogSnapshot? catalog = null)
        => SaleEligibilityPolicy.EvaluateCategory(policy, catalog ?? Snapshot(), Weed).EligibleProducts.Select(p => p.Key).ToArray();
}
