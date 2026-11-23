using System;

namespace TakeItOrLeaveIt.Core.Menu;

/// <summary>An axis-aligned rectangle in UI units, origin at the top-left of its parent, y growing downwards.</summary>
public readonly struct UiRect : IEquatable<UiRect>
{
    public UiRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = Math.Max(0f, width);
        Height = Math.Max(0f, height);
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public float Right => X + Width;

    public float Bottom => Y + Height;

    /// <summary>True when <paramref name="inner"/> lies inside this rectangle (with a tolerance for rounding).</summary>
    public bool Contains(UiRect inner, float tolerance = 0.5f)
        => inner.X >= X - tolerance && inner.Y >= Y - tolerance
           && inner.Right <= Right + tolerance && inner.Bottom <= Bottom + tolerance;

    /// <summary>True when the two rectangles share interior area (touching edges do not count).</summary>
    public bool Overlaps(UiRect other, float tolerance = 0.5f)
        => X < other.Right - tolerance && other.X < Right - tolerance
           && Y < other.Bottom - tolerance && other.Y < Bottom - tolerance;

    public bool Equals(UiRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is UiRect other && Equals(other);

    public override int GetHashCode() => (X, Y, Width, Height).GetHashCode();

    public override string ToString() => $"({X:0},{Y:0} {Width:0}x{Height:0})";
}

/// <summary>How the Products screen places the product list and the eligibility preview.</summary>
public enum ProductsArrangement
{
    /// <summary>List on the left, preview on the right.</summary>
    TwoColumns,

    /// <summary>List on top, preview below (narrow viewports).</summary>
    Stacked,
}

/// <summary>Regions of the phone app for one measured viewport. Body-relative regions use the body's top-left as origin.</summary>
public sealed class MenuLayoutPlan
{
    internal MenuLayoutPlan(UiRect viewport, UiRect header, UiRect nav, UiRect body, UiRect productsHeader,
        UiRect productList, UiRect pager, UiRect preview, UiRect bodyContent, ProductsArrangement arrangement)
    {
        Viewport = viewport;
        Header = header;
        Nav = nav;
        Body = body;
        ProductsHeader = productsHeader;
        ProductList = productList;
        Pager = pager;
        Preview = preview;
        BodyContent = bodyContent;
        Arrangement = arrangement;
    }

    /// <summary>The whole app area provided by S1API (origin 0,0).</summary>
    public UiRect Viewport { get; }

    /// <summary>Top bar: title, status, Discard, Save.</summary>
    public UiRect Header { get; }

    /// <summary>Left navigation sidebar.</summary>
    public UiRect Nav { get; }

    /// <summary>Main content area (right of the sidebar, below the header).</summary>
    public UiRect Body { get; }

    /// <summary>Body-relative: scroll area used by Overview, Settings and Diagnostics.</summary>
    public UiRect BodyContent { get; }

    /// <summary>Body-relative: category tabs, modes, search and bulk actions, summary.</summary>
    public UiRect ProductsHeader { get; }

    /// <summary>Body-relative: scrollable product rows.</summary>
    public UiRect ProductList { get; }

    /// <summary>Body-relative: Prev / page label / Next, directly under the list.</summary>
    public UiRect Pager { get; }

    /// <summary>Body-relative: scrollable eligibility preview.</summary>
    public UiRect Preview { get; }

    public ProductsArrangement Arrangement { get; }

    public override string ToString()
        => $"viewport {Viewport.Width:0}x{Viewport.Height:0}, nav {Nav.Width:0}, body {Body.Width:0}x{Body.Height:0}, " +
           $"list {ProductList}, preview {Preview}, {Arrangement}";
}

/// <summary>
/// Pure layout math for the phone app. The UI measures the real container S1API gives it and places every
/// region with these rectangles, so nothing depends on guessed screen sizes. Unit-tested for many viewports.
/// </summary>
public static class MenuLayout
{
    /// <summary>Spacing between regions and the outer edge.</summary>
    public const float Gap = 8f;

    /// <summary>Height of the Products header (tabs, modes, description, search row, summary; see the phone app).</summary>
    public const float ProductsHeaderHeight = 206f;

    public const float PagerHeight = 36f;

    /// <summary>Check box + a readable name + status label + paddings.</summary>
    public const float MinProductRowWidth = 360f;

    public const float MinPreviewWidth = 260f;

    public const float MinStackedPreviewHeight = 110f;

    /// <summary>Smallest viewport the layout is designed to be fully usable in (smaller still never overlaps).</summary>
    public const float MinUsableWidth = 800f;

    public const float MinUsableHeight = 450f;

    public static float HeaderHeight(float viewportHeight) => Clamp(viewportHeight * 0.11f, 48f, 72f);

    public static float NavWidth(float viewportWidth) => Clamp(viewportWidth * 0.18f, 130f, 220f);

    public static MenuLayoutPlan Compute(float width, float height)
    {
        width = Math.Max(0f, width);
        height = Math.Max(0f, height);
        var viewport = new UiRect(0f, 0f, width, height);

        var headerH = Math.Min(height, HeaderHeight(height));
        var navW = Math.Min(width, NavWidth(width));
        var header = new UiRect(0f, 0f, width, headerH);
        var nav = new UiRect(0f, headerH, navW, height - headerH);
        var body = new UiRect(navW, headerH, width - navW, height - headerH);

        var innerW = Math.Max(0f, body.Width - (2 * Gap));
        var innerH = Math.Max(0f, body.Height - (2 * Gap));
        var bodyContent = new UiRect(Gap, Gap, innerW, innerH);

        var productsHeaderH = Math.Min(innerH, ProductsHeaderHeight);
        var productsHeader = new UiRect(Gap, Gap, innerW, productsHeaderH);

        var areaTop = productsHeader.Bottom + Gap;
        var areaH = Math.Max(0f, (body.Height - Gap) - areaTop);
        var pagerH = Math.Min(PagerHeight, areaH);

        UiRect list, pager, preview;
        ProductsArrangement arrangement;
        if (innerW >= MinProductRowWidth + Gap + MinPreviewWidth)
        {
            arrangement = ProductsArrangement.TwoColumns;
            var previewW = Clamp(innerW * 0.36f, MinPreviewWidth, 420f);
            var listW = innerW - Gap - previewW;
            var listH = Math.Max(0f, areaH - pagerH - Gap);
            list = new UiRect(Gap, areaTop, listW, listH);
            pager = new UiRect(Gap, list.Bottom + Gap, listW, pagerH);
            preview = new UiRect(list.Right + Gap, areaTop, previewW, areaH);
        }
        else
        {
            arrangement = ProductsArrangement.Stacked;
            var previewH = Math.Min(Math.Max(MinStackedPreviewHeight, areaH * 0.35f), Math.Max(0f, areaH - pagerH - (2 * Gap)));
            var listH = Math.Max(0f, areaH - pagerH - previewH - (2 * Gap));
            list = new UiRect(Gap, areaTop, innerW, listH);
            pager = new UiRect(Gap, list.Bottom + Gap, innerW, pagerH);
            preview = new UiRect(Gap, pager.Bottom + Gap, innerW, previewH);
        }

        return new MenuLayoutPlan(viewport, header, nav, body, productsHeader, list, pager, preview, bodyContent, arrangement);
    }

    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
}
