using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TakeItOrLeaveIt.Game.PhoneMenu;

/// <summary>
/// Runtime layout self-check for the phone app. Measures the real Unity rectangles after layout and reports
/// elements that leave their clipping parent (the bug class from the first physical test) and buttons that
/// are too small to click. Read-only: it changes nothing in the UI.
/// </summary>
internal static class LayoutProbe
{
    private const float Tolerance = 1.5f;
    private const int MaxProblems = 8;

    internal sealed class Result
    {
        public int Checked { get; set; }

        public List<string> Problems { get; } = new();

        public int ProblemCount { get; set; }

        public string Summary => ProblemCount == 0
            ? $"OK ({Checked} visible elements inside their areas)"
            : $"{ProblemCount} problem(s) in {Checked} visible elements";
    }

    public static Result Check(RectTransform root)
    {
        Canvas.ForceUpdateCanvases(); // runs pending layout so the rectangles are final
        var result = new Result();
        var rootRect = Bounds(root, root);

        foreach (var graphic in root.GetComponentsInChildren<Graphic>(false))
        {
            if (graphic == null || !graphic.gameObject.activeInHierarchy || !graphic.enabled)
            {
                continue;
            }

            var rt = graphic.rectTransform;
            if (rt == root)
            {
                continue;
            }

            result.Checked++;
            var r = Bounds(rt, root);
            var (clip, clipName, scrolls) = ClipFor(rt, root, rootRect);

            var horizontalOk = r.xMin >= clip.xMin - Tolerance && r.xMax <= clip.xMax + Tolerance;
            // Content inside a scroll view may extend vertically (that is what scrolling is for).
            var verticalOk = scrolls || (r.yMin >= clip.yMin - Tolerance && r.yMax <= clip.yMax + Tolerance);
            if (!horizontalOk || !verticalOk)
            {
                Report(result, $"{Describe(graphic)} {Fmt(r)} outside {clipName} {Fmt(clip)}");
            }

            var text = graphic.TryCast<Text>();
            if (text != null && text.verticalOverflow == VerticalWrapMode.Overflow && text.preferredHeight > r.height + 2f && !string.IsNullOrEmpty(text.text))
            {
                Report(result, $"{Describe(graphic)} text needs {text.preferredHeight:0} high but has {r.height:0}");
            }
        }

        foreach (var selectable in root.GetComponentsInChildren<Selectable>(false))
        {
            if (selectable == null || !selectable.gameObject.activeInHierarchy)
            {
                continue;
            }

            var r = Bounds(selectable.GetComponent<RectTransform>(), root);
            if (r.width < 24f || r.height < 20f)
            {
                Report(result, $"control '{selectable.gameObject.name}' is too small to use: {Fmt(r)}");
            }
        }

        return result;
    }

    private static void Report(Result result, string problem)
    {
        result.ProblemCount++;
        if (result.Problems.Count < MaxProblems)
        {
            result.Problems.Add(problem);
        }
    }

    /// <summary>Nearest ancestor that clips (RectMask2D or Mask); the root otherwise.</summary>
    private static (Rect Clip, string Name, bool Scrolls) ClipFor(RectTransform rt, RectTransform root, Rect rootRect)
    {
        var scrolls = false;
        var t = rt.parent;
        while (t != null && t != root.transform)
        {
            var go = t.gameObject;
            if (go.GetComponent<ScrollRect>() != null)
            {
                scrolls = true;
            }

            if (go.GetComponent<RectMask2D>() != null || go.GetComponent<Mask>() != null)
            {
                // A scroll view's viewport clips; anything below it may scroll vertically.
                var parentScroll = t.parent != null && t.parent.gameObject.GetComponent<ScrollRect>() != null;
                return (Bounds(t.GetComponent<RectTransform>(), root), go.name, scrolls || parentScroll);
            }

            t = t.parent;
        }

        return (rootRect, "app", scrolls);
    }

    private static Rect Bounds(RectTransform rt, RectTransform root)
    {
        var rect = rt.rect;
        var a = root.InverseTransformPoint(rt.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f)));
        var b = root.InverseTransformPoint(rt.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f)));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static string Describe(Graphic graphic)
    {
        var text = graphic.TryCast<Text>();
        if (text != null)
        {
            var value = text.text ?? string.Empty;
            return $"text '{(value.Length > 28 ? value.Substring(0, 28) + "..." : value)}'";
        }

        return $"'{graphic.gameObject.name}'";
    }

    private static string Fmt(Rect r) => $"x[{r.xMin:0}..{r.xMax:0}] y[{r.yMin:0}..{r.yMax:0}]";
}
