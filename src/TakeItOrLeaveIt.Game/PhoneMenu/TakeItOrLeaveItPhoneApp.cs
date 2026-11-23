using System;
using System.Linq;
using S1API.Utils;
using TakeItOrLeaveIt.Core;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TakeItOrLeaveIt.Game.PhoneMenu;

/// <summary>
/// The "Take It Or Leave It" phone app. S1API discovers this class automatically (public parameterless
/// constructor) and creates a new instance every time the phone home screen starts in the Main scene.
/// <para>This class is presentation only. All state, validation, persistence and eligibility come from
/// <see cref="ModMenuController"/> (which uses the Phase 01 <see cref="SaleEligibilityPolicy"/>).</para>
/// <para>Rendering is change-driven: <see cref="Tick"/> runs every frame from the mod's OnUpdate but only
/// rebuilds when the controller's <see cref="ModMenuController.ViewVersion"/> changed and the app is open.
/// No events are subscribed on the controller, so repeated scene loads cannot accumulate handlers.</para>
/// </summary>
public sealed class TakeItOrLeaveItPhoneApp : S1API.PhoneApp.PhoneApp
{
    private const float NavWidth = 0.2f;
    private const float TopBarHeight = 0.11f;

    private ModMenuController? _menu;
    private long _renderedVersion = -1;
    private bool _built;
    private bool _wasOpen;
    private bool _claimedTyping;

    // Persistent chrome
    private Text _status = null!;
    private Button _saveButton = null!;
    private Text _saveLabel = null!;
    private Button _discardButton = null!;
    private Text _discardLabel = null!;
    private readonly (Button Button, Text Label, MenuScreen Screen)[] _nav = new (Button, Text, MenuScreen)[4];
    private Text _footer = null!;

    // Screens
    private GameObject _listScreen = null!;
    private Transform _listContent = null!;
    private ScrollRect _listScroll = null!;
    private GameObject _productsScreen = null!;
    private Transform _tabs = null!;
    private Transform _modes = null!;
    private Text _modeDescription = null!;
    private Text _productSummary = null!;
    private InputField _search = null!;
    private Button _selectAllButton = null!;
    private Text _selectAllLabel = null!;
    private Button _selectVisibleButton = null!;
    private Text _selectVisibleLabel = null!;
    private Button _clearButton = null!;
    private Text _clearLabel = null!;
    private Transform _rows = null!;
    private ScrollRect _rowsScroll = null!;
    private Text _pageLabel = null!;
    private Button _prevButton = null!;
    private Text _prevLabel = null!;
    private Button _nextButton = null!;
    private Text _nextLabel = null!;
    private Transform _preview = null!;
    private MenuScreen _lastScreen = (MenuScreen)(-1);
    private CategoryKey _lastCategory;

    public TakeItOrLeaveItPhoneApp()
    {
    }

    /// <summary>The instance whose UI is live (S1API creates a new one per home-screen start).</summary>
    internal static TakeItOrLeaveItPhoneApp? Current { get; private set; }

    protected override string AppName => "TakeItOrLeaveIt";

    protected override string AppTitle => "Take It Or Leave It";

    protected override string IconLabel => "Take It";

    // Only used if IconSprite returns null; S1API resolves it relative to the Mods folder.
    protected override string IconFileName => "TakeItOrLeaveIt.png";

    protected override Sprite? IconSprite => AppIcon.Create();

    protected override void OnCreatedUI(GameObject container)
    {
        Current = this;
        _menu = TakeItOrLeaveItMod.Menu;
        try
        {
            Build(container);
            _built = true;
            _renderedVersion = -1;
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"[TakeItOrLeaveIt] Phone app UI could not be built: {ex}");
            UiKit.Clear(container.transform);
            var fallback = UiKit.Panel("Error", container.transform, Theme.Background);
            UiKit.Stretch(fallback);
            var label = UiKit.Label(fallback.transform, "Take It Or Leave It could not build its menu. See MelonLoader/Latest.log.", Theme.BodySize, Theme.Danger, anchor: TextAnchor.MiddleCenter);
            UiKit.Stretch(label.gameObject, 24f);
        }
    }

    protected override void OnPhoneClosed()
    {
        ReleaseTyping();
    }

    protected override void OnDestroyed()
    {
        ReleaseTyping();
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        base.OnDestroyed();
    }

    /// <summary>Called every frame by the mod. Cheap unless the app is open and something changed.</summary>
    internal void Tick()
    {
        if (!_built || _menu is null)
        {
            return;
        }

        var open = IsOpen();
        if (!open)
        {
            if (_wasOpen)
            {
                ReleaseTyping();
                _wasOpen = false;
            }

            return;
        }

        _wasOpen = true;
        UpdateTyping();
        if (_menu.ViewVersion != _renderedVersion)
        {
            _renderedVersion = _menu.ViewVersion;
            Render();
        }
    }

    // ------------------------------------------------------------------ layout

    private void Build(GameObject container)
    {
        var root = UiKit.Panel("TIOL_Root", container.transform, Theme.Background);
        UiKit.Stretch(root);

        if (_menu is null)
        {
            var label = UiKit.Label(root.transform, "Take It Or Leave It did not initialise (see MelonLoader/Latest.log). The menu is unavailable.", Theme.BodySize, Theme.Warning, anchor: TextAnchor.MiddleCenter);
            UiKit.Stretch(label.gameObject, 24f);
            return;
        }

        BuildTopBar(root.transform);
        BuildNav(root.transform);

        var body = UiKit.Node("Body", root.transform);
        UiKit.Anchor(body, NavWidth, 0f, 1f, 1f - TopBarHeight);

        _listScreen = UiKit.Node("ListScreen", body.transform);
        UiKit.Stretch(_listScreen, 6f);
        _listContent = UiKit.ScrollList(_listScreen.transform, out _listScroll);

        BuildProductsScreen(body.transform);
    }

    private void BuildTopBar(Transform root)
    {
        var bar = UiKit.Panel("TopBar", root, Theme.Surface);
        UiKit.Anchor(bar, 0f, 1f - TopBarHeight, 1f, 1f);
        UiKit.HStack(bar, 14f, 10);

        var title = UiKit.Label(bar.transform, "TAKE IT OR LEAVE IT", Theme.TitleSize, Theme.Text, FontStyle.Bold);
        UiKit.Size(title.gameObject, 300f);
        var version = UiKit.Label(bar.transform, "v" + ModInfo.Version, Theme.SmallSize, Theme.TextFaint);
        UiKit.Size(version.gameObject, 90f);

        _status = UiKit.Label(bar.transform, string.Empty, Theme.SmallSize, Theme.TextMuted);
        UiKit.Size(_status.gameObject, -1f, -1f, 1f);

        (_discardButton, _discardLabel) = UiKit.Button(bar.transform, "Discard", Theme.SurfaceRaised, 110f, 40f, () => _menu!.Discard());
        (_saveButton, _saveLabel) = UiKit.Button(bar.transform, "Save", Theme.Accent, 110f, 40f, () => _menu!.Save());
    }

    private void BuildNav(Transform root)
    {
        var nav = UiKit.Panel("Nav", root, Theme.Surface);
        UiKit.Anchor(nav, 0f, 0f, NavWidth, 1f - TopBarHeight);
        UiKit.VStack(nav, 8f, 12);

        var screens = new[] { MenuScreen.Overview, MenuScreen.Products, MenuScreen.Settings, MenuScreen.Diagnostics };
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var (button, label) = UiKit.Button(nav.transform, screen.ToString(), Theme.SurfaceRaised, -1f, 46f, () => OpenScreen(screen), 17);
            label.alignment = TextAnchor.MiddleLeft;
            label.GetComponent<RectTransform>().offsetMin = new Vector2(16f, 0f);
            _nav[i] = (button, label, screen);
        }

        var spacer = UiKit.Node("Spacer", nav.transform);
        UiKit.Size(spacer, -1f, -1f, -1f).flexibleHeight = 1f;
        _footer = UiKit.Label(nav.transform, MenuText.SalesNotice, Theme.SmallSize, Theme.TextFaint);
    }

    private void BuildProductsScreen(Transform body)
    {
        _productsScreen = UiKit.Node("ProductsScreen", body);
        UiKit.Stretch(_productsScreen);

        // Header: category tabs, mode buttons, search + bulk actions, summary.
        var header = UiKit.Node("Header", _productsScreen.transform);
        UiKit.Anchor(header, 0f, 0.6f, 1f, 1f);
        UiKit.VStack(header, 8f, 10);

        var tabs = UiKit.Node("Tabs", header.transform);
        UiKit.HStack(tabs, 6f, 0);
        UiKit.Size(tabs, -1f, 40f);
        _tabs = tabs.transform;

        var modes = UiKit.Node("Modes", header.transform);
        UiKit.HStack(modes, 6f, 0);
        UiKit.Size(modes, -1f, 40f);
        _modes = modes.transform;

        _modeDescription = UiKit.Label(header.transform, string.Empty, Theme.SmallSize, Theme.TextMuted);

        var searchRow = UiKit.Node("SearchRow", header.transform);
        UiKit.HStack(searchRow, 6f, 0);
        UiKit.Size(searchRow, -1f, 40f);
        _search = UiKit.TextField(searchRow.transform, "Search products, effects or IDs...", ProductSearch.MaxQueryLength);
        EventHelper.AddListener<string>(OnSearchChanged, _search.onValueChanged);
        (_selectAllButton, _selectAllLabel) = UiKit.Button(searchRow.transform, "Select all unlocked", Theme.SurfaceRaised, 170f, 40f,
            () => _menu!.SelectAllUnlocked(_menu.SelectedCategory), 14);
        (_selectVisibleButton, _selectVisibleLabel) = UiKit.Button(searchRow.transform, "Select shown", Theme.SurfaceRaised, 120f, 40f,
            () => _menu!.SelectAllVisible(), 14);
        (_clearButton, _clearLabel) = UiKit.Button(searchRow.transform, "Clear", Theme.SurfaceRaised, 80f, 40f,
            () => _menu!.ClearSelection(_menu.SelectedCategory), 14);

        _productSummary = UiKit.Label(header.transform, string.Empty, Theme.BodySize, Theme.Text);

        // Product list (left) with pager, preview (right).
        var list = UiKit.Panel("List", _productsScreen.transform, Theme.Surface);
        UiKit.Anchor(list, 0f, 0.08f, 0.62f, 0.6f, 6f);
        _rows = UiKit.ScrollList(list.transform, out _rowsScroll);

        var pager = UiKit.Node("Pager", _productsScreen.transform);
        UiKit.Anchor(pager, 0f, 0f, 0.62f, 0.08f, 4f);
        UiKit.HStack(pager, 8f, 0, TextAnchor.MiddleCenter);
        (_prevButton, _prevLabel) = UiKit.Button(pager.transform, "< Prev", Theme.SurfaceRaised, 90f, 34f, () => _menu!.PreviousPage(), 14);
        _pageLabel = UiKit.Label(pager.transform, string.Empty, Theme.SmallSize, Theme.TextMuted, anchor: TextAnchor.MiddleCenter);
        UiKit.Size(_pageLabel.gameObject, 220f);
        (_nextButton, _nextLabel) = UiKit.Button(pager.transform, "Next >", Theme.SurfaceRaised, 90f, 34f, () => _menu!.NextPage(), 14);

        var preview = UiKit.Panel("Preview", _productsScreen.transform, Theme.Surface);
        UiKit.Anchor(preview, 0.62f, 0f, 1f, 0.6f, 6f);
        _preview = UiKit.ScrollList(preview.transform, out _);
    }

    // --------------------------------------------------------------- rendering

    private void Render()
    {
        var menu = _menu!;
        RenderChrome(menu);

        var products = menu.Screen == MenuScreen.Products;
        _productsScreen.SetActive(products);
        _listScreen.SetActive(!products);

        switch (menu.Screen)
        {
            case MenuScreen.Products:
                RenderProducts(menu);
                break;
            case MenuScreen.Settings:
                RenderList(RenderSettings);
                break;
            case MenuScreen.Diagnostics:
                RenderList(RenderDiagnostics);
                break;
            default:
                RenderList(RenderOverview);
                break;
        }

        _lastScreen = menu.Screen;
        _lastCategory = menu.SelectedCategory;
    }

    private void RenderChrome(ModMenuController menu)
    {
        var feedback = menu.Feedback;
        if (feedback.Kind != FeedbackKind.None)
        {
            _status.text = feedback.Text;
            _status.color = feedback.Kind switch
            {
                FeedbackKind.Success => Theme.Success,
                FeedbackKind.Error => Theme.Danger,
                FeedbackKind.Warning => Theme.Warning,
                _ => Theme.TextMuted,
            };
        }
        else
        {
            var save = menu.ActiveSaveLabel is null ? "No save loaded" : "Save " + menu.ActiveSaveLabel;
            _status.text = menu.HasUnsavedChanges ? $"{save}  -  Unsaved changes" : $"{save}  -  {menu.AvailabilityText}";
            _status.color = menu.HasUnsavedChanges ? Theme.Warning : Theme.TextMuted;
        }

        UiKit.SetButton(_saveButton, _saveLabel, "Save", Theme.Accent, menu.CanEdit && menu.HasUnsavedChanges);
        UiKit.SetButton(_discardButton, _discardLabel, "Discard", Theme.SurfaceRaised, menu.HasUnsavedChanges);

        foreach (var (button, label, screen) in _nav)
        {
            var active = menu.Screen == screen;
            var enabled = screen != MenuScreen.Products || menu.Availability == MenuAvailability.Ready;
            UiKit.SetButton(button, label, screen.ToString(), active ? Theme.AccentMuted : Theme.SurfaceRaised, enabled);
            label.alignment = TextAnchor.MiddleLeft;
        }
    }

    private void RenderList(Action<Transform> fill)
    {
        UiKit.Clear(_listContent);
        fill(_listContent);
        if (_lastScreen != _menu!.Screen)
        {
            _listScroll.verticalNormalizedPosition = 1f;
        }
    }

    private void RenderOverview(Transform c)
    {
        var menu = _menu!;
        Heading(c, "Overview");

        var toggleRow = Row(c, 48f);
        UiKit.Label(toggleRow.transform, "Mod enabled for this save", Theme.BodySize, Theme.Text).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var (toggle, toggleLabel) = UiKit.Button(toggleRow.transform, string.Empty, Theme.SurfaceRaised, 110f, 38f, () => menu.SetModEnabled(!menu.IsModEnabled));
        UiKit.SetButton(toggle, toggleLabel, menu.IsModEnabled ? "ON" : "OFF", menu.IsModEnabled ? Theme.Accent : Theme.DangerMuted, menu.CanEdit);

        Line(c, menu.ActiveSaveLabel is null ? "Active save: none" : $"Active save: {menu.ActiveSaveLabel}", Theme.TextMuted);
        Line(c, "Status: " + menu.AvailabilityText, menu.Availability == MenuAvailability.Ready ? Theme.Success : Theme.Warning);
        Line(c, MenuText.SalesNotice, Theme.TextFaint, Theme.SmallSize);
        if (!menu.IsModEnabled)
        {
            Line(c, "The mod is OFF for this save: future sales integration will use vanilla behaviour.", Theme.Warning);
        }

        if (menu.Availability != MenuAvailability.Ready)
        {
            return;
        }

        Heading(c, "Active policies");
        foreach (var category in menu.GetCategories())
        {
            var key = category.Key;
            var card = UiKit.Panel("Category", c, Theme.SurfaceRaised);
            UiKit.VStack(card, 2f, 10);
            var button = card.AddComponent<Button>();
            button.targetGraphic = card.GetComponent<Image>();
            ButtonUtils.AddListener(button, () => menu.SelectCategory(key));

            UiKit.Label(card.transform, category.DisplayName, Theme.HeadingSize, Theme.Text, FontStyle.Bold);
            UiKit.Label(card.transform, $"{category.ModeLabel}  -  {category.EligibleLabel}  (of {category.UnlockedCount} unlocked)", Theme.BodySize,
                category.EligibleCount > 0 ? Theme.TextMuted : Theme.Warning);
            if (category.Warning is not null)
            {
                UiKit.Label(card.transform, category.Warning, Theme.SmallSize, Theme.Warning);
            }

            if (category.InactiveSelectionCount > 0)
            {
                UiKit.Label(card.transform, $"{category.InactiveSelectionCount} saved selection(s) are not unlocked in this save and are ignored.", Theme.SmallSize, Theme.TextFaint);
            }
        }
    }

    private void RenderSettings(Transform c)
    {
        var menu = _menu!;
        Heading(c, "Settings");
        Line(c, menu.HasUnsavedChanges ? "You have unsaved changes." : "All changes are saved.", menu.HasUnsavedChanges ? Theme.Warning : Theme.Success);
        Line(c, "Settings are stored per save in UserData/TakeItOrLeaveIt/saves. Game saves are never modified.", Theme.TextMuted, Theme.SmallSize);

        var row = Row(c, 48f);
        var (save, saveLabel) = UiKit.Button(row.transform, string.Empty, Theme.Accent, 150f, 40f, () => menu.Save());
        UiKit.SetButton(save, saveLabel, "Save changes", Theme.Accent, menu.CanEdit && menu.HasUnsavedChanges);
        var (discard, discardLabel) = UiKit.Button(row.transform, string.Empty, Theme.SurfaceRaised, 160f, 40f, () => menu.Discard());
        UiKit.SetButton(discard, discardLabel, "Discard changes", Theme.SurfaceRaised, menu.HasUnsavedChanges);

        Heading(c, "Reset");
        Line(c, "Sets every category to Any Unlocked, clears all selections and turns the mod on. Press Save afterwards to keep it.", Theme.TextMuted, Theme.SmallSize);
        var resetRow = Row(c, 48f);
        if (menu.IsResetPending)
        {
            UiKit.Button(resetRow.transform, "Confirm reset", Theme.Danger, 170f, 40f, () => menu.ConfirmReset());
            UiKit.Button(resetRow.transform, "Cancel", Theme.SurfaceRaised, 110f, 40f, () => menu.CancelReset());
        }
        else
        {
            var (reset, resetLabel) = UiKit.Button(resetRow.transform, string.Empty, Theme.DangerMuted, 190f, 40f, () => menu.RequestReset());
            UiKit.SetButton(reset, resetLabel, "Reset to defaults...", Theme.DangerMuted, menu.CanEdit);
        }

        Heading(c, "Coming in later phases");
        Line(c, "Customer and dealer sales rules (Phases 03/04) and quality/effect rules (Phase 05) are not configurable yet.", Theme.TextFaint, Theme.SmallSize);
    }

    private void RenderDiagnostics(Transform c)
    {
        var menu = _menu!;
        Heading(c, "Diagnostics");
        foreach (var line in menu.HostDiagnostics)
        {
            Line(c, line, Theme.TextMuted);
        }

        Line(c, "Menu: S1API phone app", Theme.TextMuted);
        Line(c, "Catalog: " + menu.CatalogState, Theme.TextMuted);
        var snapshot = menu.CatalogState.Snapshot;
        if (snapshot is not null)
        {
            Line(c, $"Generation {snapshot.Generation}: {snapshot.ProductCount} products in {snapshot.Categories.Count} categories", Theme.TextMuted);
            foreach (var issue in snapshot.Issues.Take(10))
            {
                Line(c, "Issue: " + issue, Theme.Warning, Theme.SmallSize);
            }
        }

        Line(c, "Sales integration: not active (Phases 03/04). No gameplay is changed.", Theme.TextFaint, Theme.SmallSize);
    }

    private void RenderProducts(ModMenuController menu)
    {
        UiKit.Clear(_tabs);
        foreach (var category in menu.GetCategories())
        {
            var key = category.Key;
            var (tab, label) = UiKit.Button(_tabs, category.DisplayName, Theme.SurfaceRaised, 130f, 38f, () => menu.SelectCategory(key), 15);
            UiKit.SetButton(tab, label, category.DisplayName, key == menu.SelectedCategory ? Theme.AccentMuted : Theme.SurfaceRaised, true);
        }

        var summary = menu.GetSelectedCategorySummary();
        UiKit.Clear(_modes);
        var editable = menu.CanEdit && summary is not null;
        foreach (var mode in new[] { PolicyMode.AnyUnlocked, PolicyMode.SelectedOnly, PolicyMode.Disabled })
        {
            var m = mode;
            var (button, label) = UiKit.Button(_modes, MenuText.ModeLabel(m), Theme.SurfaceRaised, 150f, 38f, () => menu.SetMode(menu.SelectedCategory, m), 15);
            var active = summary is not null && summary.Mode == m;
            UiKit.SetButton(button, label, MenuText.ModeLabel(m), active ? Theme.Accent : Theme.SurfaceRaised, editable);
        }

        _modeDescription.text = summary is null ? MenuText.WaitingForProducts : MenuText.ModeDescription(summary.Mode);

        var selectionActive = editable && summary!.Mode != PolicyMode.Disabled;
        UiKit.SetButton(_selectAllButton, _selectAllLabel, "Select all unlocked", Theme.SurfaceRaised, selectionActive);
        UiKit.SetButton(_selectVisibleButton, _selectVisibleLabel, "Select shown", Theme.SurfaceRaised, selectionActive);
        UiKit.SetButton(_clearButton, _clearLabel, "Clear", Theme.SurfaceRaised, editable && summary!.SelectedCount > 0);
        if (!string.Equals(_search.text, menu.SearchText, StringComparison.Ordinal) && !_search.isFocused)
        {
            _search.text = menu.SearchText; // e.g. cleared after a save switch
        }

        if (summary is null)
        {
            _productSummary.text = menu.AvailabilityText;
            _productSummary.color = Theme.Warning;
            UiKit.Clear(_rows);
            UiKit.Clear(_preview);
            _pageLabel.text = string.Empty;
            return;
        }

        _productSummary.text = summary.Warning
            ?? $"{summary.DisplayName}: {summary.ModeLabel}  -  {summary.EligibleLabel}  -  {summary.SelectedCount} selected  -  {summary.UnlockedCount} unlocked";
        _productSummary.color = summary.Warning is null ? Theme.Text : Theme.Warning;

        RenderRows(menu, summary);
        RenderPreview(menu);
    }

    private void RenderRows(ModMenuController menu, CategorySummaryView summary)
    {
        UiKit.Clear(_rows);
        var page = menu.GetProductPage();
        var category = summary.Key;
        var selectionInactive = summary.Mode == PolicyMode.Disabled;

        foreach (var row in page.Rows)
        {
            var key = row.Key;
            var panel = UiKit.Panel("Row", _rows, row.IsSelectable ? Theme.SurfaceRaised : Theme.Surface);
            UiKit.HStack(panel, 10f, 8);
            UiKit.Size(panel, -1f, 52f);
            var button = panel.AddComponent<Button>();
            button.targetGraphic = panel.GetComponent<Image>();
            button.interactable = menu.CanEdit && (row.IsSelectable || row.IsSelected);
            ButtonUtils.AddListener(button, () => menu.ToggleProduct(category, key));

            var box = UiKit.Panel("Check", panel.transform, row.IsSelected ? (selectionInactive ? Theme.AccentMuted : Theme.Accent) : Theme.CheckboxOff);
            UiKit.Size(box, 22f, 22f);
            box.GetComponent<Image>().raycastTarget = false;

            var texts = UiKit.Node("Texts", panel.transform);
            UiKit.VStack(texts, 0f, 0);
            UiKit.Size(texts, -1f, -1f, 1f);
            UiKit.Label(texts.transform, row.DisplayName, Theme.BodySize, row.IsSelectable ? Theme.Text : Theme.TextFaint, FontStyle.Bold);
            UiKit.Label(texts.transform, row.Detail, Theme.SmallSize, Theme.TextFaint);

            var status = UiKit.Label(panel.transform, row.StatusLabel, Theme.SmallSize, row.IsEligible ? Theme.Success : Theme.TextMuted, anchor: TextAnchor.MiddleRight);
            UiKit.Size(status.gameObject, 150f);
        }

        if (page.Rows.Count == 0)
        {
            UiKit.Label(_rows, page.TotalCount == 0 ? "No products discovered in this category yet." : "No products match your search.", Theme.BodySize, Theme.TextMuted);
        }

        _pageLabel.text = page.PageCount <= 1
            ? $"{page.MatchCount} of {page.TotalCount} products"
            : $"Page {page.PageIndex + 1}/{page.PageCount}  ({page.MatchCount} of {page.TotalCount})";
        UiKit.SetButton(_prevButton, _prevLabel, "< Prev", Theme.SurfaceRaised, page.PageIndex > 0);
        UiKit.SetButton(_nextButton, _nextLabel, "Next >", Theme.SurfaceRaised, page.PageIndex + 1 < page.PageCount);

        if (_lastCategory != menu.SelectedCategory || _lastScreen != MenuScreen.Products)
        {
            _rowsScroll.verticalNormalizedPosition = 1f;
        }
    }

    private void RenderPreview(ModMenuController menu)
    {
        UiKit.Clear(_preview);
        var preview = menu.GetPreview();
        if (preview is null)
        {
            return;
        }

        UiKit.Label(_preview, "ELIGIBILITY PREVIEW", Theme.SmallSize, Theme.TextFaint, FontStyle.Bold);
        if (preview.ReflectsUnsavedChanges)
        {
            UiKit.Label(_preview, "Showing unsaved changes", Theme.SmallSize, Theme.Warning);
        }

        UiKit.Label(_preview, preview.Headline, Theme.HeadingSize, Theme.Text, FontStyle.Bold);
        UiKit.Label(_preview, preview.Summary, Theme.BodySize, preview.AnyPermitted ? Theme.Success : Theme.Warning);
        if (preview.PermittedNames.Count > 0)
        {
            UiKit.Label(_preview, "Permitted: " + string.Join(", ", preview.PermittedNames), Theme.SmallSize, Theme.TextMuted);
        }

        foreach (var line in preview.Lines)
        {
            UiKit.Label(_preview, line, Theme.SmallSize, line.EndsWith("Permitted", StringComparison.Ordinal) ? Theme.Success : Theme.TextMuted);
        }

        UiKit.Label(_preview, preview.Disclaimer, Theme.SmallSize, Theme.TextFaint, FontStyle.Italic);
    }

    // ------------------------------------------------------------------ helpers

    private void OpenScreen(MenuScreen screen)
    {
        var menu = _menu!;
        if (screen == MenuScreen.Products)
        {
            if (!menu.SelectedCategory.IsValid)
            {
                var first = menu.GetCategories().FirstOrDefault();
                if (first is not null && menu.SelectCategory(first.Key))
                {
                    return;
                }
            }
        }

        menu.Navigate(screen);
    }

    private void OnSearchChanged(string text) => _menu?.SetSearch(text);

    private void UpdateTyping()
    {
        var focused = _search != null && _productsScreen.activeInHierarchy && _search.isFocused;
        if (focused && !_claimedTyping)
        {
            S1API.Input.Controls.IsTyping = true;
            _claimedTyping = true;
        }
        else if (!focused && _claimedTyping)
        {
            ReleaseTyping();
        }
    }

    private void ReleaseTyping()
    {
        if (_claimedTyping)
        {
            S1API.Input.Controls.IsTyping = false;
            _claimedTyping = false;
        }

        if (_search != null && _search.isFocused)
        {
            _search.DeactivateInputField();
            var events = EventSystem.current;
            if (events != null)
            {
                events.SetSelectedGameObject(null);
            }
        }
    }

    private static void Heading(Transform parent, string text)
        => UiKit.Label(parent, text.ToUpperInvariant(), Theme.SmallSize, Theme.TextFaint, FontStyle.Bold);

    private static void Line(Transform parent, string text, Color color, int size = Theme.BodySize)
        => UiKit.Label(parent, text, size, color);

    private static GameObject Row(Transform parent, float height)
    {
        var row = UiKit.Node("Row", parent);
        UiKit.HStack(row, 10f, 0);
        UiKit.Size(row, -1f, height);
        return row;
    }
}

/// <summary>Draws the app icon at runtime (no image file to install).</summary>
internal static class AppIcon
{
    public static Sprite? Create()
    {
        try
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var background = Theme.Background;
            var accent = Theme.Accent;
            var white = Theme.Text;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var color = InsideRounded(x, y, size, 26) ? background : new Color(0f, 0f, 0f, 0f);
                    var dx = x - 64f;
                    var dy = y - 64f;
                    var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (r < 44f)
                    {
                        color = accent;
                    }

                    // Check mark: two thick strokes.
                    if (r < 44f && (NearSegment(x, y, 42, 66, 58, 50, 7f) || NearSegment(x, y, 58, 50, 88, 82, 7f)))
                    {
                        color = white;
                    }

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"[TakeItOrLeaveIt] App icon could not be drawn: {ex.Message}");
            return null;
        }
    }

    private static bool InsideRounded(int x, int y, int size, int radius)
    {
        var cx = Math.Max(radius, Math.Min(size - 1 - radius, x));
        var cy = Math.Max(radius, Math.Min(size - 1 - radius, y));
        var dx = x - cx;
        var dy = y - cy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static bool NearSegment(float px, float py, float ax, float ay, float bx, float by, float width)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var t = Math.Max(0f, Math.Min(1f, (((px - ax) * abx) + ((py - ay) * aby)) / ((abx * abx) + (aby * aby))));
        var qx = ax + (t * abx) - px;
        var qy = ay + (t * aby) - py;
        return (qx * qx) + (qy * qy) <= width * width;
    }
}
