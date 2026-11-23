using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using Xunit.Abstractions;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 02 group D (search) and large synthetic catalogs; plus the JSON codec.</summary>
public class MenuSearchTests
{
    private readonly ITestOutputHelper _output;

    public MenuSearchTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Search_is_case_insensitive_and_partial()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        h.Menu.SetSearch("GRAND");

        Assert.Equal(new[] { Gdp }, h.Rows().Select(r => r.Key));
    }

    [Fact]
    public void Search_matches_effects_and_requires_every_term()
    {
        var a = Product(ProductKey.Create("a"), Weed, name: "Calm Kush", effects: "sedating");
        var b = Product(ProductKey.Create("b"), Weed, name: "Wild Kush", effects: "energizing");

        Assert.Equal(new[] { b }, ProductSearch.Filter(new[] { a, b }, "kush ENERG"));
        Assert.Empty(ProductSearch.Filter(new[] { a, b }, "kush nothing"));
    }

    [Fact]
    public void Clearing_search_restores_all_entries_and_never_changes_selection()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetProductSelected(Weed, OgKush, true);

        h.Menu.SetSearch("sour");
        Assert.Single(h.Rows());
        h.Menu.SetSearch("");

        Assert.Equal(3, h.Rows().Count);
        Assert.Equal(new[] { OgKush }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.True(h.Row(OgKush).IsSelected);
    }

    [Fact]
    public void Overlong_query_is_truncated()
    {
        Assert.Equal(ProductSearch.MaxQueryLength, ProductSearch.Normalize(new string('x', 500)).Length);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Large_catalogs_page_search_and_select_correctly(int count)
    {
        var h = new MenuHarness(() => MenuHarness.Synthetic(count)).Load();
        h.Menu.SelectCategory(Weed);

        var clock = Stopwatch.StartNew();
        var first = h.Menu.GetProductPage();
        var pageMs = clock.Elapsed.TotalMilliseconds;

        clock.Restart();
        h.Menu.SetSearch("purple");
        var searched = h.Menu.GetProductPage();
        var searchMs = clock.Elapsed.TotalMilliseconds;

        clock.Restart();
        h.Menu.SelectAllUnlocked(Weed);
        var selectMs = clock.Elapsed.TotalMilliseconds;

        _output.WriteLine($"{count} products: first page {pageMs:F2} ms, search {searchMs:F2} ms, select-all {selectMs:F2} ms");

        var expectedUnlocked = Enumerable.Range(0, count).Count(i => i % 10 != 9);
        var expectedMatches = Enumerable.Range(0, count).Count(i => i % 3 == 0);
        Assert.Equal(count, first.TotalCount);
        Assert.True(first.Rows.Count <= ModMenuController.DefaultPageSize);
        Assert.Equal(expectedMatches, searched.MatchCount);
        Assert.Equal(expectedUnlocked, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts.Count);

        // Walk every page: each product appears exactly once.
        h.Menu.SetSearch("");
        var seen = new HashSet<ProductKey>();
        var page = h.Menu.GetProductPage();
        for (var i = 0; i < page.PageCount; i++)
        {
            foreach (var row in h.Menu.GetProductPage().Rows)
            {
                Assert.True(seen.Add(row.Key));
            }

            h.Menu.NextPage();
        }

        Assert.Equal(count, seen.Count);
    }

    // ------------------------------------------------------------- JSON codec

    [Fact]
    public void Json_round_trips_settings()
    {
        var config = PolicyConfiguration.Default
            .WithEnabled(false)
            .WithCategory(Weed, CategoryPolicy.SelectedOnly(OgKush, ProductKey.Create("quote\"and\\slash")))
            .WithCategory(Meth, CategoryPolicy.Disabled);

        var json = PolicySettingsJson.ToJson(config.ToSettings());
        var back = PolicyConfiguration.FromSettings(PolicySettingsJson.FromJson(json), out var issues);

        Assert.Empty(issues);
        Assert.False(back.Enabled);
        Assert.Equal(config.GetEffectivePolicy(Weed).SelectedProducts, back.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(PolicyMode.Disabled, back.GetEffectivePolicy(Meth).Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":1,\"enabled\":\"yes\"}")]
    [InlineData("{\"schemaVersion\":1,\"categories\":[]}")]
    [InlineData("{\"schemaVersion\":1} trailing")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public void Malformed_settings_are_rejected(string json)
    {
        Assert.ThrowsAny<Exception>(() => PolicySettingsJson.FromJson(json));
    }

    [Fact]
    public void Json_parser_handles_escapes_numbers_and_nesting_limits()
    {
        var value = (Dictionary<string, object?>)MiniJson.Parse("{\"s\":\"a\\u0041\\n\",\"n\":-1.5e2,\"b\":false,\"z\":null,\"l\":[1,[2]]}")!;

        Assert.Equal("aA\n", value["s"]);
        Assert.Equal(-150d, value["n"]);
        Assert.Equal(false, value["b"]);
        Assert.Null(value["z"]);
        Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', 100) + new string(']', 100)));
    }
}
