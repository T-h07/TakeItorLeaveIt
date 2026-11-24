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
    private ModMenuController? _menu;
    private long _renderedVersion = -1;
    private bool _built;
    private bool _wasOpen;
    private bool _claimedTyping;

    // Measured layout (see MenuLayout in Core). Regions are placed in UI units of the container S1API gives us.
    private RectTransform _rootRect = null!;
    private Vector2 _laidOutSize = new(-1f, -1f);
    private MenuLayoutPlan? _plan;
    private string? _lastProbeKey;
    private static int _probeLogs;

    // Persistent chrome
    private GameObject _topBar = null!;
    private GameObject _navPanel = null!;
    private GameObject _body = null!;
    private Text _status = null!;
    private Button _saveButton = null!;
    private Text _saveLabel = null!;
    private Button _discardButton = null!;
    private Text _discardLabel = null!;
    private static readonly MenuScreen[] NavScreens =
    {
        MenuScreen.Overview, MenuScreen.Products, MenuScreen.Rules, MenuScreen.Sales, MenuScreen.Presets, MenuScreen.Settings, MenuScreen.Diagnostics,
    };

    private readonly (Button Button, Text Label, MenuScreen Screen)[] _nav = new (Button, Text, MenuScreen)[NavScreens.Length];

    // Screens
    private GameObject _listScreen = null!;
    private Transform _listContent = null!;
    private ScrollRect _listScroll = null!;
    private GameObject _productsScreen = null!;
    private GameObject _productsHeader = null!;
    private GameObject _listPanel = null!;
    private GameObject _pagerPanel = null!;
    private GameObject _previewPanel = null!;
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
    private ScrollRect _previewScroll = null!;
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
            _built = _menu is not null;
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

        // The container's real size is only known once the phone has laid it out; re-measure every frame
        // (two float compares) and re-place all regions when it changes (resolution / UI scale changes).
        var size = _rootRect.rect.size;
        if (size.x > 1f && size.y > 1f && (Mathf.Abs(size.x - _laidOutSize.x) > 0.5f || Mathf.Abs(size.y - _laidOutSize.y) > 0.5f))
        {
            ApplyLayout(size);
            _renderedVersion = -1;
        }

        if (_plan is null)
        {
            return;
        }

        if (_menu.ViewVersion != _renderedVersion)
        {
            _renderedVersion = _menu.ViewVersion;
            Render();
            ProbeLayout();
        }
    }

    // ------------------------------------------------------------------ layout

    private void Build(GameObject container)
    {
        var root = UiKit.Panel("TIOL_Root", container.transform, Theme.Background);
        UiKit.Stretch(root);
        _rootRect = root.GetComponent<RectTransform>();

        if (_menu is null)
        {
            var label = UiKit.Label(root.transform, "Take It Or Leave It did not initialise (see MelonLoader/Latest.log). The menu is unavailable.", Theme.BodySize, Theme.Warning, anchor: TextAnchor.MiddleCenter);
            UiKit.Stretch(label.gameObject, 24f);
            return;
        }

        BuildTopBar(root.transform);
        BuildNav(root.transform);

        // Body clips its children so nothing can ever draw over the header or sidebar.
        _body = UiKit.Node("Body", root.transform);
        _body.AddComponent<RectMask2D>();

        _listScreen = UiKit.Node("ListScreen", _body.transform);
        _listContent = UiKit.ScrollList(_listScreen.transform, out _listScroll);

        BuildProductsScreen(_body.transform);
    }

    /// <summary>Places every region from <see cref="MenuLayout"/> for the measured container size.</summary>
    private void ApplyLayout(Vector2 size)
    {
        var plan = MenuLayout.Compute(size.x, size.y);
        _plan = plan;
        _laidOutSize = size;
        _lastProbeKey = null;

        UiKit.Place(_topBar, plan.Header);
        UiKit.Place(_navPanel, plan.Nav);
        UiKit.Place(_body, plan.Body);
        UiKit.Place(_listScreen, plan.BodyContent);
        UiKit.Stretch(_productsScreen);
        UiKit.Place(_productsHeader, plan.ProductsHeader);
        UiKit.Place(_listPanel, plan.ProductList);
        UiKit.Place(_pagerPanel, plan.Pager);
        UiKit.Place(_previewPanel, plan.Preview);

        var scale = _rootRect.lossyScale.x;
        MelonLoader.MelonLogger.Msg($"[TakeItOrLeaveIt] Phone app layout: {plan} (canvas scale {scale:0.###}).");
        if (size.x < MenuLayout.MinUsableWidth || size.y < MenuLayout.MinUsableHeight)
        {
            MelonLoader.MelonLogger.Warning($"[TakeItOrLeaveIt] Phone app viewport {size.x:0}x{size.y:0} is smaller than the designed minimum {MenuLayout.MinUsableWidth:0}x{MenuLayout.MinUsableHeight:0}; some areas will be cramped.");
        }
    }

    private void BuildTopBar(Transform root)
    {
        _topBar = UiKit.Panel("TopBar", root, Theme.Surface);
        UiKit.HStack(_topBar, 12f, 8);

        var title = UiKit.Label(_topBar.transform, "TAKE IT OR LEAVE IT", 20, Theme.Text, FontStyle.Bold);
        title.verticalOverflow = VerticalWrapMode.Truncate;
        UiKit.Size(title.gameObject, 250f, -1f, -1f, -1f, 150f);
        var version = UiKit.Label(_topBar.transform, "v" + ModInfo.Version, Theme.SmallSize, Theme.TextFaint);
        version.verticalOverflow = VerticalWrapMode.Truncate;
        UiKit.Size(version.gameObject, 80f, -1f, -1f, -1f, 0f);

        _status = UiKit.Label(_topBar.transform, string.Empty, Theme.SmallSize, Theme.TextMuted);
        _status.verticalOverflow = VerticalWrapMode.Truncate;
        UiKit.Size(_status.gameObject, -1f, -1f, 1f, -1f, 80f);

        (_discardButton, _discardLabel) = UiKit.Button(_topBar.transform, "Discard", Theme.SurfaceRaised, 100f, 38f, () => _menu!.Discard(), 15, 76f);
        (_saveButton, _saveLabel) = UiKit.Button(_topBar.transform, "Save", Theme.Accent, 100f, 38f, () => _menu!.Save(), 15, 76f);
    }

    private void BuildNav(Transform root)
    {
        _navPanel = UiKit.Panel("Nav", root, Theme.Surface);
        // Seven entries (Phase 05) at 34 px fit the 450 px minimum height; the sales notice lives on the Overview screen.
        UiKit.VStack(_navPanel, 6f, 8);
        _navPanel.AddComponent<RectMask2D>();

        for (var i = 0; i < NavScreens.Length; i++)
        {
            var screen = NavScreens[i];
            var (button, label) = UiKit.Button(_navPanel.transform, screen.ToString(), Theme.SurfaceRaised, -1f, 34f, () => OpenScreen(screen), 15, 0f);
            label.alignment = TextAnchor.MiddleLeft;
            var rt = label.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(12f, 0f);
            rt.offsetMax = new Vector2(-6f, 0f);
            _nav[i] = (button, label, screen);
        }

        var spacer = UiKit.Node("Spacer", _navPanel.transform);
        UiKit.Size(spacer).flexibleHeight = 1f;
    }

    private void BuildProductsScreen(Transform body)
    {
        _productsScreen = UiKit.Node("ProductsScreen", body);

        // Header: category tabs, mode buttons, description, search + bulk actions, summary.
        // Its height is MenuLayout.ProductsHeaderHeight; it clips rather than drawing over the list.
        _productsHeader = UiKit.Node("Header", _productsScreen.transform);
        UiKit.VStack(_productsHeader, 6f, 0);
        _productsHeader.AddComponent<RectMask2D>();

        var tabs = UiKit.Node("Tabs", _productsHeader.transform);
        UiKit.HStack(tabs, 6f, 0);
        UiKit.Size(tabs, -1f, 36f, -1f, 36f);
        _tabs = tabs.transform;

        var modes = UiKit.Node("Modes", _productsHeader.transform);
        UiKit.HStack(modes, 6f, 0);
        UiKit.Size(modes, -1f, 36f, -1f, 36f);
        _modes = modes.transform;

        _modeDescription = UiKit.Label(_productsHeader.transform, string.Empty, Theme.SmallSize, Theme.TextMuted);

        var searchRow = UiKit.Node("SearchRow", _productsHeader.transform);
        UiKit.HStack(searchRow, 6f, 0);
        UiKit.Size(searchRow, -1f, 38f, -1f, 38f);
        _search = UiKit.TextField(searchRow.transform, "Search name, effect or ID...", ProductSearch.MaxQueryLength);
        UiKit.Size(_search.gameObject, -1f, 38f, 1f, 38f, 120f);
        EventHelper.AddListener<string>(OnSearchChanged, _search.onValueChanged);
        (_selectAllButton, _selectAllLabel) = UiKit.Button(searchRow.transform, "Select all unlocked", Theme.SurfaceRaised, 160f, 38f,
            () => _menu!.SelectAllUnlocked(_menu.SelectedCategory), 14, 96f);
        (_selectVisibleButton, _selectVisibleLabel) = UiKit.Button(searchRow.transform, "Select shown", Theme.SurfaceRaised, 120f, 38f,
            () => _menu!.SelectAllVisible(), 14, 80f);
        (_clearButton, _clearLabel) = UiKit.Button(searchRow.transform, "Clear", Theme.SurfaceRaised, 80f, 38f,
            () => _menu!.ClearSelection(_menu.SelectedCategory), 14, 60f);

        _productSummary = UiKit.Label(_productsHeader.transform, string.Empty, 15, Theme.Text);

        _listPanel = UiKit.Panel("List", _productsScreen.transform, Theme.Surface);
        _rows = UiKit.ScrollList(_listPanel.transform, out _rowsScroll);

        _pagerPanel = UiKit.Node("Pager", _productsScreen.transform);
        UiKit.HStack(_pagerPanel, 8f, 0, TextAnchor.MiddleCenter);
        (_prevButton, _prevLabel) = UiKit.Button(_pagerPanel.transform, "< Prev", Theme.SurfaceRaised, 90f, 32f, () => _menu!.PreviousPage(), 14, 70f);
        _pageLabel = UiKit.Label(_pagerPanel.transform, string.Empty, Theme.SmallSize, Theme.TextMuted, anchor: TextAnchor.MiddleCenter);
        UiKit.Size(_pageLabel.gameObject, 220f, -1f, -1f, -1f, 90f);
        (_nextButton, _nextLabel) = UiKit.Button(_pagerPanel.transform, "Next >", Theme.SurfaceRaised, 90f, 32f, () => _menu!.NextPage(), 14, 70f);

        _previewPanel = UiKit.Panel("Preview", _productsScreen.transform, Theme.Surface);
        _preview = UiKit.ScrollList(_previewPanel.transform, out _previewScroll);
    }

    /// <summary>
    /// Runtime bounds check written to the log once per screen and size: every visible element must lie
    /// horizontally inside its clipping parent (and fully inside it outside scroll areas), and every button
    /// must have a usable size. This is the evidence for the physical layout retest.
    /// </summary>
    private void ProbeLayout()
    {
        var menu = _menu!;
        var key = $"{menu.Screen}|{_laidOutSize.x:0}x{_laidOutSize.y:0}";
        if (key == _lastProbeKey || _probeLogs >= 40)
        {
            return;
        }

        _lastProbeKey = key;
        _probeLogs++;
        try
        {
            var result = LayoutProbe.Check(_rootRect);
            var text = $"[TakeItOrLeaveIt] Layout check {menu.Screen} ({_laidOutSize.x:0}x{_laidOutSize.y:0}): {result.Summary}";
            if (result.Problems.Count == 0)
            {
                MelonLoader.MelonLogger.Msg(text);
            }
            else
            {
                MelonLoader.MelonLogger.Warning(text + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", result.Problems));
            }
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"[TakeItOrLeaveIt] Layout check failed: {ex.Message}");
        }
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
            case MenuScreen.Rules:
                RenderList(RenderRules);
                break;
            case MenuScreen.Sales:
                RenderList(RenderSales);
                break;
            case MenuScreen.Presets:
                RenderList(RenderPresets);
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
        else if (menu.HostControlText is { } hostText)
        {
            // Phase 07 client: read-only, the host decides.
            _status.text = hostText;
            _status.color = menu.ShowsHostRules ? Theme.TextMuted : Theme.Warning;
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
            var enabled = (screen != MenuScreen.Products && screen != MenuScreen.Rules) || menu.Availability == MenuAvailability.Ready;
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
        UiKit.Size(UiKit.Label(toggleRow.transform, "Mod enabled for this save", Theme.BodySize, Theme.Text).gameObject, -1f, -1f, 1f, -1f, 0f);
        var (toggle, toggleLabel) = UiKit.Button(toggleRow.transform, string.Empty, Theme.SurfaceRaised, 100f, 36f, () => menu.SetModEnabled(!menu.IsModEnabled), 15, 80f);
        UiKit.SetButton(toggle, toggleLabel, menu.IsModEnabled ? "ON" : "OFF", menu.IsModEnabled ? Theme.Accent : Theme.DangerMuted, menu.CanEdit);

        if (menu.HostControlText is { } hostText)
        {
            Line(c, hostText, menu.ShowsHostRules ? Theme.Accent : Theme.Warning);
            Line(c, menu.ShowsHostRules
                    ? "These are the host's rules. Only the host can change them; nothing is saved on this computer."
                    : "Nothing can be changed here. The host's game decides every sale.",
                Theme.TextMuted, Theme.SmallSize);
        }

        Line(c, menu.ActiveSaveLabel is null ? "Active save: none" : $"Active save: {menu.ActiveSaveLabel}", Theme.TextMuted);
        Line(c, "Status: " + menu.AvailabilityText, menu.Availability == MenuAvailability.Ready ? Theme.Success : Theme.Warning);
        Line(c, MenuText.SalesNotice, Theme.TextFaint, Theme.SmallSize);
        Line(c, $"Preset: {menu.ActivePresetLabel}  -  Player sales {(menu.PlayerSalesEnabled ? "on" : "off")}  -  Dealer sales {(menu.DealerSalesEnabled ? "on" : "off")}  -  Mixing: {PolicyPresets.MixingLabel(menu.Mixing)}",
            Theme.TextMuted, Theme.SmallSize);
        if (menu.EditingDealer is not null)
        {
            Line(c, $"Showing {menu.ScopeLabel}. Switch back to the global rules on the Sales screen.", Theme.Warning, Theme.SmallSize);
        }

        if (!menu.IsModEnabled)
        {
            Line(c, "The mod is OFF for this save: new customer orders use vanilla rules. Orders already accepted keep their rules.", Theme.Warning);
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
            if (category.RuleDetail.Length > 0)
            {
                UiKit.Label(card.transform, "Rules: " + category.RuleDetail, Theme.SmallSize, Theme.TextMuted);
            }

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
        var (save, saveLabel) = UiKit.Button(row.transform, string.Empty, Theme.Accent, 150f, 38f, () => menu.Save(), 15, 110f);
        UiKit.SetButton(save, saveLabel, "Save changes", Theme.Accent, menu.CanEdit && menu.HasUnsavedChanges);
        var (discard, discardLabel) = UiKit.Button(row.transform, string.Empty, Theme.SurfaceRaised, 160f, 38f, () => menu.Discard(), 15, 110f);
        UiKit.SetButton(discard, discardLabel, "Discard changes", Theme.SurfaceRaised, menu.HasUnsavedChanges);

        Line(c, "Mixing, player and dealer sales and dealer overrides are on the Sales screen; quality and effect rules on the Rules screen; presets on the Presets screen.", Theme.TextMuted, Theme.SmallSize);

        Heading(c, "Reset");
        Line(c, "Sets every category to Any Unlocked, clears all lists, effect and quality rules and dealer overrides, and turns every switch on. Press Save afterwards to keep it.", Theme.TextMuted, Theme.SmallSize);
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

        Heading(c, "Rule engine (Phase 05)");
        var draft = menu.Saved;
        Line(c, $"Settings schema {PolicyConfiguration.CurrentSchemaVersion}; preset {PolicyPresets.DetectLabel(draft)}; mixing {PolicyPresets.MixingLabel(draft.Mixing)}", Theme.TextMuted, Theme.SmallSize);
        Line(c, $"Scopes: player sales {(draft.PlayerSalesEnabled ? "on" : "off")}, dealer sales {(draft.DealerSalesEnabled ? "on" : "off")}, {draft.DealerOverrides.Count} dealer(s) with overrides", Theme.TextMuted, Theme.SmallSize);
        if (snapshot is not null)
        {
            var all = snapshot.Categories.SelectMany(cat => snapshot.GetProducts(cat.Key)).ToList();
            var noAncestry = all.Count(p => !p.AncestryKnown);
            var noEffects = all.Count(p => !p.EffectsKnown);
            Line(c, $"Lineage: {all.Count - noAncestry} of {all.Count} products from recorded mix recipes" + (noAncestry > 0 ? $"; {noAncestry} unknown (base-strain rules match them only directly)" : string.Empty),
                noAncestry > 0 ? Theme.Warning : Theme.TextMuted, Theme.SmallSize);
            if (noEffects > 0)
            {
                Line(c, $"Effects unreadable for {noEffects} product(s): they never pass an effect rule.", Theme.Warning, Theme.SmallSize);
            }
        }

        Heading(c, "Save data (Phase 06)");
        Line(c, $"Profile: {menu.ActiveSaveLabel ?? "none"}; settings file {menu.SettingsFileState}",
            menu.PolicyReadyForSales || menu.ActiveSaveLabel is null ? Theme.TextMuted : Theme.Warning, Theme.SmallSize);

        Heading(c, "Session and sales (Phase 07)");
        foreach (var line in menu.SalesStatus?.Invoke() ?? new[] { "Player Sales: status unavailable" })
        {
            Line(c, line, Theme.TextMuted, Theme.SmallSize);
        }
    }

    private void RenderProducts(ModMenuController menu)
    {
        UiKit.Clear(_tabs);
        foreach (var category in menu.GetCategories())
        {
            var key = category.Key;
            var (tab, label) = UiKit.Button(_tabs, category.DisplayName, Theme.SurfaceRaised, 120f, 36f, () => menu.SelectCategory(key), 14, 64f);
            UiKit.SetButton(tab, label, category.DisplayName, key == menu.SelectedCategory ? Theme.AccentMuted : Theme.SurfaceRaised, true);
        }

        var summary = menu.GetSelectedCategorySummary();
        UiKit.Clear(_modes);
        var editable = menu.CanEdit && summary is not null;
        foreach (var mode in new[] { PolicyMode.AnyUnlocked, PolicyMode.SelectedOnly, PolicyMode.Blocklist, PolicyMode.Disabled })
        {
            var m = mode;
            var (button, label) = UiKit.Button(_modes, MenuText.ModeLabel(m), Theme.SurfaceRaised, 140f, 36f, () => menu.SetMode(menu.SelectedCategory, m), 14, 90f);
            var active = summary is not null && summary.Mode == m;
            UiKit.SetButton(button, label, MenuText.ModeLabel(m), active ? Theme.Accent : Theme.SurfaceRaised, editable);
        }

        _modeDescription.text = summary is null ? MenuText.WaitingForProducts : MenuText.ModeDescription(summary.Mode);

        var selectionActive = editable && summary!.Mode != PolicyMode.Disabled;
        var blocking = summary?.Mode == PolicyMode.Blocklist;
        var listed = menu.GetSelectedRule() is { } rule ? (blocking ? rule.BlockedProducts.Count : rule.SelectedProducts.Count) : 0;
        UiKit.SetButton(_selectAllButton, _selectAllLabel, blocking ? "Block all unlocked" : "Select all unlocked", Theme.SurfaceRaised, selectionActive);
        UiKit.SetButton(_selectVisibleButton, _selectVisibleLabel, blocking ? "Block shown" : "Select shown", Theme.SurfaceRaised, selectionActive);
        UiKit.SetButton(_clearButton, _clearLabel, "Clear", Theme.SurfaceRaised, editable && listed > 0);
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

        var scope = menu.EditingDealer is null ? string.Empty : $"[{menu.ScopeLabel}{(summary.IsDealerOverride ? string.Empty : ", global rule")}]  ";
        var listWord = summary.Mode == PolicyMode.Blocklist ? "blocked" : "selected";
        var rules = summary.RuleDetail.Length > 0 ? $"  -  {summary.RuleDetail}" : string.Empty;
        _productSummary.text = scope + (summary.Warning
            ?? $"{summary.DisplayName}: {summary.ModeLabel}  -  {summary.EligibleLabel}  -  {listed} {listWord}  -  {summary.UnlockedCount} unlocked{rules}");
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
            UiKit.Size(panel, -1f, -1f, -1f, 52f);
            var button = panel.AddComponent<Button>();
            button.targetGraphic = panel.GetComponent<Image>();
            button.interactable = menu.CanEdit && (row.IsSelectable || row.IsSelected);
            ButtonUtils.AddListener(button, () => menu.ToggleProduct(category, key));

            var box = UiKit.Panel("Check", panel.transform, row.IsSelected ? (selectionInactive ? Theme.AccentMuted : Theme.Accent) : Theme.CheckboxOff);
            UiKit.Size(box, 22f, 22f);
            box.GetComponent<Image>().raycastTarget = false;

            var texts = UiKit.Node("Texts", panel.transform);
            UiKit.VStack(texts, 0f, 0);
            UiKit.Size(texts, -1f, -1f, 1f, -1f, 100f);
            UiKit.Label(texts.transform, row.DisplayName, Theme.BodySize, row.IsSelectable ? Theme.Text : Theme.TextFaint, FontStyle.Bold);
            UiKit.Label(texts.transform, row.Lineage.Length > 0 ? row.Lineage + " · " + row.Detail : row.Detail, Theme.SmallSize, Theme.TextFaint);

            var status = UiKit.Label(panel.transform, row.StatusLabel, Theme.SmallSize, row.IsEligible ? Theme.Success : Theme.TextMuted, anchor: TextAnchor.MiddleRight);
            UiKit.Size(status.gameObject, 130f, -1f, -1f, -1f, 80f);
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

    private void RenderRules(Transform c)
    {
        var menu = _menu!;
        Heading(c, "Rules - " + menu.ScopeLabel);
        if (menu.Availability != MenuAvailability.Ready)
        {
            Line(c, menu.AvailabilityText, Theme.Warning);
            return;
        }

        var tabs = Row(c, 40f);
        foreach (var category in menu.GetCategories())
        {
            var key = category.Key;
            var (tab, label) = UiKit.Button(tabs.transform, category.DisplayName, Theme.SurfaceRaised, 120f, 34f, () => menu.SelectCategory(key), 14, 64f);
            UiKit.SetButton(tab, label, category.DisplayName, key == menu.SelectedCategory ? Theme.AccentMuted : Theme.SurfaceRaised, true);
        }

        var summary = menu.GetSelectedCategorySummary();
        var rule = menu.GetSelectedRule();
        if (summary is null || rule is null)
        {
            Line(c, "Pick a category.", Theme.TextMuted);
            return;
        }

        var cat = summary.Key;
        var edit = menu.CanEdit;
        Line(c, $"{summary.DisplayName}: {summary.ModeLabel}  -  {summary.EligibleLabel}", summary.EligibleCount > 0 ? Theme.Text : Theme.Warning);
        if (summary.Warning is not null)
        {
            Line(c, summary.Warning, Theme.Warning, Theme.SmallSize);
        }

        if (menu.EditingDealer is not null)
        {
            var scopeRow = Row(c, 44f);
            UiKit.Size(UiKit.Label(scopeRow.transform, summary.IsDealerOverride ? "This dealer has its own rule for this category." : "This dealer uses the global rule; any change creates an override.",
                Theme.SmallSize, Theme.TextMuted).gameObject, -1f, -1f, 1f, -1f, 0f);
            var (useGlobal, useGlobalLabel) = UiKit.Button(scopeRow.transform, string.Empty, Theme.SurfaceRaised, 150f, 34f, () => menu.UseGlobalRule(cat), 14, 100f);
            UiKit.SetButton(useGlobal, useGlobalLabel, "Use global rule", Theme.SurfaceRaised, edit && summary.IsDealerOverride);
        }

        Heading(c, "List matching");
        Line(c, MenuText.MatchDescription(rule.Match), Theme.TextMuted, Theme.SmallSize);
        var matchRow = Row(c, 44f);
        foreach (var match in new[] { SelectionMatch.ExactProduct, SelectionMatch.BaseStrain })
        {
            var m = match;
            var (button, label) = UiKit.Button(matchRow.transform, string.Empty, Theme.SurfaceRaised, 220f, 34f, () => menu.SetMatch(cat, m), 14, 120f);
            UiKit.SetButton(button, label, MenuText.MatchLabel(m), rule.Match == m ? Theme.Accent : Theme.SurfaceRaised, edit);
        }

        if (rule.Mode is not (PolicyMode.SelectedOnly or PolicyMode.Blocklist))
        {
            Line(c, "Matching applies to the allowed or blocked list (Selected Only or Blocklist mode on the Products screen).", Theme.TextFaint, Theme.SmallSize);
        }

        Heading(c, "Minimum quality");
        Line(c, "Items below this tier are refused at handover and never reserved from dealer stock. Customers keep their own quality standard.", Theme.TextMuted, Theme.SmallSize);
        var qualityRow = Row(c, 44f);
        foreach (var tier in new QualityTier?[] { null, QualityTier.Poor, QualityTier.Standard, QualityTier.Premium, QualityTier.Heavenly })
        {
            var t = tier;
            var (button, label) = UiKit.Button(qualityRow.transform, string.Empty, Theme.SurfaceRaised, 110f, 34f, () => menu.SetMinimumQuality(cat, t), 14, 70f);
            UiKit.SetButton(button, label, t is null ? "Any" : QualityTiers.Label(t.Value) + "+", rule.MinimumQuality == t ? Theme.Accent : Theme.SurfaceRaised, edit);
        }

        Heading(c, "Effects");
        Line(c, "Tap an effect to cycle Any -> Required -> Excluded. Excluded effects always win. Products whose effects could not be read never pass an effect rule.", Theme.TextMuted, Theme.SmallSize);
        var effectMatchRow = Row(c, 44f);
        foreach (var match in new[] { EffectMatch.All, EffectMatch.Any })
        {
            var m = match;
            var (button, label) = UiKit.Button(effectMatchRow.transform, string.Empty, Theme.SurfaceRaised, 190f, 34f, () => menu.SetEffectMatch(cat, m), 14, 110f);
            UiKit.SetButton(button, label, m == EffectMatch.All ? "Require all" : "Require any", rule.Effects.Match == m ? Theme.Accent : Theme.SurfaceRaised, edit);
        }

        var (clear, clearLabel) = UiKit.Button(effectMatchRow.transform, string.Empty, Theme.SurfaceRaised, 110f, 34f, () => menu.ClearEffects(cat), 14, 70f);
        UiKit.SetButton(clear, clearLabel, "Clear", Theme.SurfaceRaised, edit && !rule.Effects.IsEmpty);

        var effects = menu.GetEffectRows();
        if (effects.Count == 0)
        {
            Line(c, "No effects are known for this category yet.", Theme.TextFaint, Theme.SmallSize);
        }

        foreach (var effect in effects)
        {
            var id = effect.Id;
            var row = Row(c, 40f);
            var name = effect.OnDiscoveredProduct ? id : id + " (no discovered product has it)";
            UiKit.Size(UiKit.Label(row.transform, name, Theme.BodySize, effect.IsConflicting ? Theme.Warning : effect.OnDiscoveredProduct ? Theme.Text : Theme.TextFaint).gameObject,
                -1f, -1f, 1f, -1f, 0f);
            var color = effect.State switch
            {
                EffectState.Required => Theme.Accent,
                EffectState.Excluded => Theme.DangerMuted,
                _ => Theme.SurfaceRaised,
            };
            var (button, label) = UiKit.Button(row.transform, string.Empty, color, 120f, 32f, () => menu.CycleEffect(cat, id), 14, 90f);
            UiKit.SetButton(button, label, effect.StateLabel, color, edit);
        }
    }

    private void RenderSales(Transform c)
    {
        var menu = _menu!;
        var edit = menu.CanEdit;
        Heading(c, "Sales");
        Line(c, "Switches apply to contracts accepted after saving. Accepted orders keep their rules, and dealer appointments keep their reservations.", Theme.TextMuted, Theme.SmallSize);

        Switch(c, "Player sales (your own customers)", menu.PlayerSalesEnabled, () => menu.SetPlayerSales(!menu.PlayerSalesEnabled), edit);
        Switch(c, "Dealer sales (your hired dealers)", menu.DealerSalesEnabled, () => menu.SetDealerSales(!menu.DealerSalesEnabled), edit);
        if (!menu.IsModEnabled)
        {
            Line(c, "The mod is off for this save, so both paths use vanilla rules (Overview screen).", Theme.Warning, Theme.SmallSize);
        }

        Heading(c, "Mixing");
        foreach (var mixing in new[] { MixingMode.AnyPermitted, MixingMode.SingleProduct, MixingMode.SingleBaseStrain, MixingMode.RequestedOnly })
        {
            var m = mixing;
            var row = Row(c, 44f);
            var (button, label) = UiKit.Button(row.transform, string.Empty, Theme.SurfaceRaised, 190f, 34f, () => menu.SetMixing(m), 14, 120f);
            var text = PolicyPresets.MixingLabel(m);
            UiKit.SetButton(button, label, char.ToUpperInvariant(text[0]) + text.Substring(1), menu.Mixing == m ? Theme.Accent : Theme.SurfaceRaised, edit);
            UiKit.Size(UiKit.Label(row.transform, MenuText.MixingDescription(m), Theme.SmallSize, Theme.TextMuted).gameObject, -1f, -1f, 1f, -1f, 0f);
        }

        Line(c, "Fair pricing always applies: a substitute must be worth at least what the customer agreed to pay.", Theme.TextFaint, Theme.SmallSize);

        Heading(c, "Rules being edited: " + menu.ScopeLabel);
        var globalRow = Row(c, 44f);
        var (global, globalLabel) = UiKit.Button(globalRow.transform, string.Empty, Theme.SurfaceRaised, 190f, 34f, () => menu.EditDealer(null), 14, 120f);
        UiKit.SetButton(global, globalLabel, "Edit global rules", menu.EditingDealer is null ? Theme.Accent : Theme.SurfaceRaised, true);
        Line(c, "A dealer override replaces the global rule of one category for that dealer only. It never bypasses the switches, unlocks or fair pricing.", Theme.TextMuted, Theme.SmallSize);

        var dealers = menu.GetDealers();
        if (dealers.Count == 0)
        {
            Line(c, "No dealers seen yet in this session. Dealers appear here once the game has updated them.", Theme.TextFaint, Theme.SmallSize);
        }

        foreach (var dealer in dealers)
        {
            var id = dealer.Id;
            var card = UiKit.Panel("Dealer", c, Theme.SurfaceRaised);
            UiKit.VStack(card, 2f, 8);
            UiKit.Label(card.transform, dealer.Name, Theme.BodySize, Theme.Text, FontStyle.Bold);
            UiKit.Label(card.transform, dealer.Status, Theme.SmallSize, dealer.Known && dealer.Hired ? Theme.TextMuted : Theme.TextFaint);
            var row = Row(card.transform, 40f);
            var (editButton, editLabel) = UiKit.Button(row.transform, string.Empty, Theme.SurfaceRaised, 170f, 32f, () => menu.EditDealer(id), 14, 110f);
            UiKit.SetButton(editButton, editLabel, dealer.IsEditing ? "Editing" : "Edit overrides", dealer.IsEditing ? Theme.Accent : Theme.SurfaceRaised, true);
            var (removeButton, removeLabel) = UiKit.Button(row.transform, string.Empty, Theme.DangerMuted, 170f, 32f, () => menu.RemoveDealerOverrides(id), 14, 110f);
            UiKit.SetButton(removeButton, removeLabel, "Remove overrides", Theme.DangerMuted, edit && dealer.OverrideCount > 0);
        }
    }

    private void RenderPresets(Transform c)
    {
        var menu = _menu!;
        Heading(c, "Presets");
        Line(c, "Current rules: " + menu.ActivePresetLabel, Theme.Text);
        Line(c, "A preset only changes your draft. You see what changes first; press Save to keep it or Discard to undo.", Theme.TextMuted, Theme.SmallSize);

        if (menu.PendingPreset is { } pending)
        {
            var card = UiKit.Panel("Pending", c, Theme.SurfaceRaised);
            UiKit.VStack(card, 2f, 10);
            UiKit.Label(card.transform, "Apply " + PolicyPresets.Label(pending) + "?", Theme.HeadingSize, Theme.Warning, FontStyle.Bold);
            UiKit.Label(card.transform, menu.PresetPreview.Count == 0 ? "Nothing would change." : "This replaces these parts of your draft:", Theme.SmallSize, Theme.TextMuted);
            foreach (var line in menu.PresetPreview)
            {
                UiKit.Label(card.transform, "- " + line, Theme.SmallSize, line.StartsWith("Warning", StringComparison.Ordinal) ? Theme.Warning : Theme.Text);
            }

            var row = Row(card.transform, 44f);
            UiKit.Button(row.transform, "Apply to draft", Theme.Accent, 170f, 36f, () => menu.ConfirmPreset(), 14, 110f);
            UiKit.Button(row.transform, "Cancel", Theme.SurfaceRaised, 110f, 36f, () => menu.CancelPreset(), 14, 80f);
        }

        foreach (var preset in PolicyPresets.All)
        {
            var p = preset;
            var card = UiKit.Panel("Preset", c, Theme.SurfaceRaised);
            UiKit.VStack(card, 2f, 10);
            UiKit.Label(card.transform, PolicyPresets.Label(p), Theme.BodySize, Theme.Text, FontStyle.Bold);
            UiKit.Label(card.transform, PolicyPresets.Description(p), Theme.SmallSize, Theme.TextMuted);
            var row = Row(card.transform, 40f);
            var (button, label) = UiKit.Button(row.transform, string.Empty, Theme.SurfaceRaised, 170f, 32f, () => menu.RequestPreset(p), 14, 110f);
            UiKit.SetButton(button, label, "Preview changes", Theme.SurfaceRaised, menu.CanEdit);
        }
    }

    private static void Switch(Transform c, string text, bool on, Action toggle, bool enabled)
    {
        var row = Row(c, 48f);
        UiKit.Size(UiKit.Label(row.transform, text, Theme.BodySize, Theme.Text).gameObject, -1f, -1f, 1f, -1f, 0f);
        var (button, label) = UiKit.Button(row.transform, string.Empty, Theme.SurfaceRaised, 100f, 36f, toggle, 15, 80f);
        UiKit.SetButton(button, label, on ? "ON" : "OFF", on ? Theme.Accent : Theme.DangerMuted, enabled);
    }

    // ------------------------------------------------------------------ helpers

    private void OpenScreen(MenuScreen screen)
    {
        var menu = _menu!;
        if (screen == MenuScreen.Rules)
        {
            menu.Navigate(screen);
            if (!menu.SelectedCategory.IsValid && menu.GetCategories().FirstOrDefault() is { } firstRules)
            {
                menu.SelectCategory(firstRules.Key);
            }

            return;
        }

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
