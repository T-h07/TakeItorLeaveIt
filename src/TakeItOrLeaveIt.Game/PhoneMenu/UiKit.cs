using System;
using S1API.UI;
using S1API.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace TakeItOrLeaveIt.Game.PhoneMenu;

/// <summary>Colours and sizes for the phone app (dark, low-contrast chrome with a muted green accent).</summary>
internal static class Theme
{
    public static readonly Color Background = Rgb(0x121418);
    public static readonly Color Surface = Rgb(0x1A1D23);
    public static readonly Color SurfaceRaised = Rgb(0x23272F);
    public static readonly Color SurfaceHover = Rgb(0x2B3039);
    public static readonly Color Accent = Rgb(0x3E8E63);
    public static readonly Color AccentMuted = Rgb(0x2C5A43);
    public static readonly Color Text = Rgb(0xECEFF3);
    public static readonly Color TextMuted = Rgb(0x9AA3AF);
    public static readonly Color TextFaint = Rgb(0x6B7380);
    public static readonly Color Warning = Rgb(0xE0A43A);
    public static readonly Color Danger = Rgb(0xD9534F);
    public static readonly Color DangerMuted = Rgb(0x6A2E2C);
    public static readonly Color Success = Rgb(0x5CBF7F);
    public static readonly Color CheckboxOff = Rgb(0x3A404A);

    public const int TitleSize = 24;
    public const int HeadingSize = 18;
    public const int BodySize = 17;
    public const int SmallSize = 14;

    private static Color Rgb(uint hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
}

/// <summary>Small layout helpers on top of S1API's UIFactory. Everything here runs on the Unity main thread.</summary>
internal static class UiKit
{
    public static GameObject Node(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return go;
    }

    public static GameObject Panel(string name, Transform parent, Color color) => UIFactory.Panel(name, parent, color);

    /// <summary>Anchors <paramref name="go"/> to a fraction of its parent (0..1 coordinates), with a pixel inset.</summary>
    public static void Anchor(GameObject go, float xMin, float yMin, float xMax, float yMax, float inset = 0f)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public static void Stretch(GameObject go, float inset = 0f) => Anchor(go, 0f, 0f, 1f, 1f, inset);

    public static VerticalLayoutGroup VStack(GameObject go, float spacing, int padding)
    {
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    public static HorizontalLayoutGroup HStack(GameObject go, float spacing, int padding, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.childAlignment = alignment;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return layout;
    }

    /// <summary>
    /// Places <paramref name="go"/> at an exact rectangle of its parent (top-left origin, UI units). Used for all
    /// top-level regions, computed by <see cref="TakeItOrLeaveIt.Core.Menu.MenuLayout"/> from the measured container.
    /// </summary>
    public static void Place(GameObject go, TakeItOrLeaveIt.Core.Menu.UiRect r)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(r.X, -r.Y);
        rt.sizeDelta = new Vector2(r.Width, r.Height);
    }

    public static LayoutElement Size(GameObject go, float preferredWidth = -1f, float preferredHeight = -1f, float flexibleWidth = -1f, float minHeight = -1f, float minWidth = -1f)
    {
        var element = go.GetComponent<LayoutElement>();
        if (element == null)
        {
            element = go.AddComponent<LayoutElement>();
        }

        element.preferredWidth = preferredWidth;
        element.preferredHeight = preferredHeight;
        element.flexibleWidth = flexibleWidth;
        element.minHeight = minHeight;
        element.minWidth = minWidth;
        return element;
    }

    public static Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var label = UIFactory.Text("Label", text, parent, size, anchor, style);
        label.color = color;
        label.supportRichText = false;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>
    /// A flat button with a centred label. Returns the button and its label. In a horizontal layout it takes
    /// <paramref name="width"/> when there is room and shrinks to <paramref name="minWidth"/> (label wraps) when not.
    /// </summary>
    public static (Button Button, Text Label) Button(Transform parent, string text, Color color, float width, float height, Action onClick, int fontSize = 15, float minWidth = -1f)
    {
        var (go, button, label) = UIFactory.ButtonWithLabel("Button", text, parent, color, width, height);
        label.fontSize = fontSize;
        label.color = Theme.Text;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(6f, 0f);
        labelRt.offsetMax = new Vector2(-6f, 0f);
        Size(go, width, height, -1f, height, minWidth < 0f ? width : minWidth);
        ButtonUtils.AddListener(button, onClick);
        return (button, label);
    }

    public static void SetButton(Button button, Text label, string text, Color color, bool interactable)
    {
        label.text = text;
        label.color = interactable ? Theme.Text : Theme.TextFaint;
        button.interactable = interactable;
        var image = button.targetGraphic;
        if (image != null)
        {
            image.color = interactable ? color : Theme.Surface;
        }
    }

    /// <summary>
    /// A vertical scroll list filling <paramref name="parent"/>; returns the content transform.
    /// <para>Built here instead of with S1API's <c>UIFactory.ScrollableVerticalList</c>: that helper anchors the
    /// content to stretch horizontally but leaves Unity's default <c>sizeDelta</c> of (100, 100), so the content
    /// is 100 units wider than the viewport and centred, and the mask cuts 50 units off each side. Here the
    /// content's width equals the viewport's (sizeDelta.x = 0) and its height comes from the layout.</para>
    /// </summary>
    public static Transform ScrollList(Transform parent, out ScrollRect scroll)
    {
        var root = Node("Scroll", parent);
        Stretch(root);
        scroll = root.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 28f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;

        var viewport = Node("Viewport", root.transform);
        Stretch(viewport);
        viewport.AddComponent<RectMask2D>();
        // Transparent graphic so scrolling with the wheel / drag also works over empty space.
        var hit = viewport.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        scroll.viewport = viewport.GetComponent<RectTransform>();

        var content = Node("Content", viewport.transform);
        var rt = content.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        var layout = VStack(content, 6f, 8);
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = rt;
        return content.transform;
    }

    /// <summary>Removes children without a one-frame overlap (Destroy is deferred to the end of the frame).</summary>
    public static void Clear(Transform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }

    /// <summary>A single-line text field with a placeholder.</summary>
    public static InputField TextField(Transform parent, string placeholder, int characterLimit)
    {
        var box = UIFactory.Panel("SearchField", parent, Theme.SurfaceRaised);
        Size(box, -1f, 40f, 1f);

        var text = UIFactory.Text("Text", string.Empty, box.transform, Theme.BodySize, TextAnchor.MiddleLeft);
        text.color = Theme.Text;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        Stretch(text.gameObject);
        Inset(text.gameObject, 12f, 4f);

        var hint = UIFactory.Text("Placeholder", placeholder, box.transform, Theme.BodySize, TextAnchor.MiddleLeft, FontStyle.Italic);
        hint.color = Theme.TextFaint;
        hint.raycastTarget = false;
        Stretch(hint.gameObject);
        Inset(hint.gameObject, 12f, 4f);

        var input = box.AddComponent<InputField>();
        input.textComponent = text;
        input.placeholder = hint;
        input.targetGraphic = box.GetComponent<Image>();
        input.characterLimit = characterLimit;
        input.lineType = InputField.LineType.SingleLine;
        return input;
    }

    private static void Inset(GameObject go, float x, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(x, y);
        rt.offsetMax = new Vector2(-x, -y);
    }
}
