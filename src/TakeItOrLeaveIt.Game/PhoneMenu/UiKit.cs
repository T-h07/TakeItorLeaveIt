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

    public static LayoutElement Size(GameObject go, float preferredWidth = -1f, float preferredHeight = -1f, float flexibleWidth = -1f, float minHeight = -1f)
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

    /// <summary>A flat button with a centred label. Returns the button and its label.</summary>
    public static (Button Button, Text Label) Button(Transform parent, string text, Color color, float width, float height, Action onClick, int fontSize = 15)
    {
        var (go, button, label) = UIFactory.ButtonWithLabel("Button", text, parent, color, width, height);
        label.fontSize = fontSize;
        label.color = Theme.Text;
        label.raycastTarget = false;
        Size(go, width, height);
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

    /// <summary>A vertical scroll list filling <paramref name="parent"/>; returns the content transform.</summary>
    public static Transform ScrollList(Transform parent, out ScrollRect scroll)
    {
        var content = UIFactory.ScrollableVerticalList("Scroll", parent, out scroll);
        scroll.scrollSensitivity = 28f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.spacing = 6f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
        }

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
