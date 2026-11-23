using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Menu;
using Xunit;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 02 layout fix: every region stays inside the measured phone viewport and never overlaps a sibling.</summary>
public class MenuLayoutTests
{
    /// <summary>Viewports in canvas units, from the smallest supported to 4K-like canvases (UI scaling changes these numbers).</summary>
    public static IEnumerable<object[]> UsableViewports() => new[]
    {
        new object[] { 800f, 450f },
        new object[] { 720f, 640f },
        new object[] { 1000f, 550f },
        new object[] { 1024f, 576f },
        new object[] { 1280f, 720f },
        new object[] { 1600f, 900f },
        new object[] { 1920f, 1080f },
        new object[] { 2560f, 1440f },
        new object[] { 700f, 800f },
    };

    public static IEnumerable<object[]> AnyViewports() => new[]
    {
        new object[] { 0f, 0f },
        new object[] { 100f, 60f },
        new object[] { 320f, 200f },
        new object[] { 480f, 270f },
        new object[] { 640f, 400f },
        new object[] { 3840f, 2160f },
    };

    [Theory]
    [MemberData(nameof(UsableViewports))]
    public void Top_level_regions_fill_the_viewport_without_overlap(float width, float height)
    {
        var plan = MenuLayout.Compute(width, height);

        Assert.True(plan.Viewport.Contains(plan.Header));
        Assert.True(plan.Viewport.Contains(plan.Nav));
        Assert.True(plan.Viewport.Contains(plan.Body));
        Assert.False(plan.Header.Overlaps(plan.Nav));
        Assert.False(plan.Header.Overlaps(plan.Body));
        Assert.False(plan.Nav.Overlaps(plan.Body));
        Assert.Equal(width, plan.Nav.Width + plan.Body.Width, 3);
        Assert.Equal(height, plan.Header.Height + plan.Body.Height, 3);
    }

    [Theory]
    [MemberData(nameof(UsableViewports))]
    public void Products_regions_stay_inside_the_body_and_do_not_overlap(float width, float height)
    {
        var plan = MenuLayout.Compute(width, height);
        var body = new UiRect(0f, 0f, plan.Body.Width, plan.Body.Height);
        var regions = new[] { plan.ProductsHeader, plan.ProductList, plan.Pager, plan.Preview };

        foreach (var region in regions)
        {
            Assert.True(body.Contains(region), $"{region} outside body {body} ({plan})");
            Assert.True(region.Width > 0 && region.Height > 0, $"{region} is empty ({plan})");
        }

        for (var i = 0; i < regions.Length; i++)
        {
            for (var j = i + 1; j < regions.Length; j++)
            {
                Assert.False(regions[i].Overlaps(regions[j]), $"{regions[i]} overlaps {regions[j]} ({plan})");
            }
        }

        Assert.True(body.Contains(plan.BodyContent));
    }

    [Theory]
    [MemberData(nameof(UsableViewports))]
    public void Product_rows_and_preview_are_wide_and_tall_enough_to_read(float width, float height)
    {
        var plan = MenuLayout.Compute(width, height);

        Assert.True(plan.ProductList.Width >= MenuLayout.MinProductRowWidth, plan.ToString());
        Assert.True(plan.ProductList.Height >= 52f, plan.ToString()); // at least one full product row
        Assert.True(plan.Preview.Width >= MenuLayout.MinPreviewWidth, plan.ToString());
        Assert.True(plan.Preview.Height >= 90f, plan.ToString());
        Assert.Equal(MenuLayout.PagerHeight, plan.Pager.Height, 3);
    }

    [Theory]
    [MemberData(nameof(AnyViewports))]
    public void Tiny_or_huge_viewports_never_produce_negative_sizes_or_overlaps(float width, float height)
    {
        var plan = MenuLayout.Compute(width, height);
        var all = new[] { plan.Header, plan.Nav, plan.Body, plan.ProductsHeader, plan.ProductList, plan.Pager, plan.Preview, plan.BodyContent };
        foreach (var r in all)
        {
            Assert.True(r.Width >= 0 && r.Height >= 0);
        }

        Assert.False(plan.ProductList.Overlaps(plan.Preview));
        Assert.False(plan.ProductList.Overlaps(plan.Pager));
        Assert.False(plan.Header.Overlaps(plan.Body));
    }

    [Fact]
    public void Wide_viewports_use_two_columns_and_narrow_ones_stack_the_preview()
    {
        Assert.Equal(ProductsArrangement.TwoColumns, MenuLayout.Compute(1000f, 550f).Arrangement);
        Assert.Equal(ProductsArrangement.TwoColumns, MenuLayout.Compute(1920f, 1080f).Arrangement);

        var narrow = MenuLayout.Compute(700f, 800f);
        Assert.Equal(ProductsArrangement.Stacked, narrow.Arrangement);
        Assert.True(narrow.Preview.Y >= narrow.Pager.Bottom);
    }

    [Fact]
    public void Two_column_preview_sits_right_of_the_list_with_a_gap()
    {
        var plan = MenuLayout.Compute(1280f, 720f);

        Assert.Equal(plan.ProductList.Right + MenuLayout.Gap, plan.Preview.X, 3);
        Assert.Equal(plan.ProductList.Y, plan.Preview.Y, 3);
        Assert.Equal(plan.ProductList.Width, plan.Pager.Width, 3);
    }

    [Fact]
    public void Rect_helpers_handle_edges()
    {
        var a = new UiRect(0, 0, 10, 10);
        Assert.False(a.Overlaps(new UiRect(10, 0, 5, 5)));
        Assert.True(a.Overlaps(new UiRect(9, 9, 5, 5)));
        Assert.True(a.Contains(new UiRect(0, 0, 10, 10)));
        Assert.False(a.Contains(new UiRect(-2, 0, 5, 5)));
        Assert.Equal(0f, new UiRect(0, 0, -5, -5).Width);
    }
}
