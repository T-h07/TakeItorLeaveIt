using System;
using TakeItOrLeaveIt.Core.Matching;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group A: product identity.</summary>
public class ProductIdentityTests
{
    [Fact]
    public void Equal_product_ids_compare_equal_and_hash_equal()
    {
        var a = ProductKey.Create("ogkush");
        var b = ProductKey.Create("  ogkush ");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Product_ids_are_case_sensitive_game_identifiers()
    {
        Assert.NotEqual(ProductKey.Create("OGKush"), ProductKey.Create("ogkush"));
    }

    [Fact]
    public void Category_keys_are_normalised_case_insensitively()
    {
        Assert.Equal(CategoryKey.Create("Marijuana"), CategoryKey.Create("marijuana"));
        Assert.Equal("marijuana", CategoryKey.Create(" MARIJUANA ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0001id")]
    public void Invalid_identifiers_are_rejected(string? raw)
    {
        Assert.False(ProductKey.TryCreate(raw, out var p));
        Assert.False(p.IsValid);
        Assert.False(CategoryKey.TryCreate(raw, out var c));
        Assert.False(c.IsValid);
        Assert.Throws<ArgumentException>(() => ProductKey.Create(raw));
    }

    [Fact]
    public void Overlong_identifiers_are_rejected()
    {
        Assert.False(ProductKey.TryCreate(new string('a', ProductKey.MaxLength + 1), out _));
        Assert.True(ProductKey.TryCreate(new string('a', ProductKey.MaxLength), out _));
        Assert.False(CategoryKey.TryCreate(new string('a', CategoryKey.MaxLength + 1), out _));
    }

    [Fact]
    public void Default_keys_are_invalid()
    {
        Assert.False(default(ProductKey).IsValid);
        Assert.False(default(CategoryKey).IsValid);
    }

    [Fact]
    public void Same_display_name_different_products_stay_distinct()
    {
        // e.g. a player-named mix that happens to share a name with another product
        var a = Product(ProductKey.Create("mix-1"), Weed, name: "Purple Haze", effects: "calming");
        var b = Product(ProductKey.Create("mix-2"), Weed, name: "Purple Haze", effects: "energizing");

        Assert.Equal(a.DisplayName, b.DisplayName);
        Assert.False(ProductMatcher.IsSameProduct(a, b));
        Assert.False(ProductMatcher.HasSameRecipeFingerprint(a, b));
        var snapshot = Snapshot(a, b);
        Assert.Equal(2, snapshot.GetProducts(Weed).Count);
    }

    [Fact]
    public void Same_effects_different_products_share_fingerprint_but_not_identity()
    {
        var a = Product(ProductKey.Create("mix-1"), Weed, effects: new[] { "b", "a" });
        var b = Product(ProductKey.Create("mix-2"), Weed, effects: new[] { "a", "b", "a" });

        Assert.True(ProductMatcher.HasSameRecipeFingerprint(a, b));
        Assert.False(ProductMatcher.IsSameProduct(a, b));
        Assert.Equal(new[] { "a", "b" }, a.EffectIds);
    }

    [Fact]
    public void Same_effects_in_different_categories_have_different_fingerprints()
    {
        var weed = Product(ProductKey.Create("w"), Weed, effects: "x");
        var meth = Product(ProductKey.Create("m"), Meth, effects: "x");

        Assert.False(ProductMatcher.HasSameRecipeFingerprint(weed, meth));
    }

    [Fact]
    public void Base_product_is_only_known_when_declared()
    {
        var plain = Product(Gdp, Weed);
        var mix = new ProductDescriptor(ProductKey.Create("gdp-mix"), Weed, "Granddaddy Purple", UnlockState.Unlocked, new[] { "e" }, Gdp);
        var lookalike = Product(ProductKey.Create("granddaddypurple-2"), Weed, name: "Granddaddy Purple");

        Assert.True(ProductMatcher.IsDerivedFrom(mix, Gdp));
        Assert.True(ProductMatcher.IsDerivedFrom(plain, Gdp));
        Assert.False(ProductMatcher.IsDerivedFrom(lookalike, Gdp)); // never inferred from name
        Assert.Null(lookalike.BaseProduct);
    }

    [Fact]
    public void Descriptor_rejects_invalid_keys()
    {
        Assert.Throws<ArgumentException>(() => new ProductDescriptor(default, Weed, "x", UnlockState.Unlocked));
        Assert.Throws<ArgumentException>(() => new ProductDescriptor(OgKush, default, "x", UnlockState.Unlocked));
    }

    [Fact]
    public void Missing_display_name_falls_back_to_key()
    {
        Assert.Equal("ogkush", new ProductDescriptor(OgKush, Weed, "  ", UnlockState.Unlocked).DisplayName);
    }
}
