using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Menu;

/// <summary>
/// UI-independent state and actions for the mod menu. A renderer (phone app) only reads the view methods
/// and calls the action methods; it never decides eligibility itself.
/// <list type="bullet">
/// <item><b>Saved vs draft:</b> edits change a draft <see cref="PolicyConfiguration"/>. <see cref="Save"/> validates and
/// persists it, then publishes it through the <see cref="InMemoryPolicyProvider"/> (the single source of truth for
/// the rest of the mod). <see cref="Discard"/> restores the saved configuration.</item>
/// <item><b>Eligibility:</b> every "permitted / not permitted" shown comes from <see cref="SaleEligibilityPolicy"/>
/// evaluated against the draft and the active catalog snapshot.</item>
/// <item><b>Saves:</b> settings are loaded per save identity when a save session starts. Switching saves drops any
/// unsaved draft of the previous save; it is never applied to another save.</item>
/// <item><b>Scope (Phase 05):</b> category edits go to the global rules, or to one dealer's overrides while
/// <see cref="EditingDealer"/> is set; every view then shows that dealer's effective rules.</item>
/// <item><b>Presets (Phase 05):</b> <see cref="RequestPreset"/> shows what would change, <see cref="ConfirmPreset"/> puts
/// it into the draft; nothing is kept until <see cref="Save"/>.</item>
/// <item><b>Rendering:</b> <see cref="ViewVersion"/> increments on every visible change, so the renderer rebuilds only
/// when it changes (never per frame). The product list is paged.</item>
/// </list>
/// All members must be called on one thread (the Unity main thread in game).
/// </summary>
public sealed class ModMenuController : IDisposable
{
    public const int DefaultPageSize = 40;
    public const int PreviewQuantity = 5;
    private const int PreviewLineLimit = 8;
    private const int PreviewNameLimit = 6;

    private readonly ProductCatalogService _catalog;
    private readonly IPolicyStore _store;
    private readonly InMemoryPolicyProvider _provider;
    private readonly IModLog? _log;

    private SaveSession _session;
    private PolicyConfiguration _saved = PolicyConfiguration.Default;
    private PolicyConfiguration _draft = PolicyConfiguration.Default;
    private string _savedFingerprint = Fingerprint(PolicyConfiguration.Default);
    private bool _settingsLoaded;
    private bool _settingsUnreadable;
    private bool _settingsNewer;
    private string _settingsFileState = "not loaded";
    private bool _disposed;

    private CategoryKey _category;
    private string? _dealer;
    private PolicyPreset? _pendingPreset;
    private IReadOnlyList<string> _presetPreview = Array.Empty<string>();
    private string _search = string.Empty;
    private int _page;

    // Filter cache: re-filtered only when catalog generation, category or search text change.
    private long _filterGeneration = -1;
    private CategoryKey _filterCategory;
    private string _filterSearch = string.Empty;
    private List<ProductDescriptor> _filtered = new();

    public ModMenuController(ProductCatalogService catalog, IPolicyStore store, InMemoryPolicyProvider provider, IModLog? log = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _log = log;
        _catalog.StateChanged += OnCatalogStateChanged;
        OnCatalogStateChanged(_catalog.State);
    }

    /// <summary>Raised after any visible change (also reflected by <see cref="ViewVersion"/>).</summary>
    public event Action? Changed;

    public long ViewVersion { get; private set; }

    public MenuScreen Screen { get; private set; } = MenuScreen.Overview;

    public MenuFeedback Feedback { get; private set; } = MenuFeedback.Empty;

    public bool IsResetPending { get; private set; }

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Lines for the Diagnostics screen supplied by the game host (versions, runtime, warnings).</summary>
    public IReadOnlyList<string> HostDiagnostics { get; set; } = Array.Empty<string>();

    /// <summary>Player-sales status lines for the Diagnostics screen, supplied by the host (Phase 03).</summary>
    public Func<IReadOnlyList<string>>? SalesStatus { get; set; }

    /// <summary>Dealers seen in this save, supplied by the game host (Phase 05 overrides). Null when unavailable.</summary>
    public Func<IReadOnlyList<DealerInfo>>? Dealers { get; set; }

    public CatalogState CatalogState => _catalog.State;

    public MenuAvailability Availability => _catalog.State.Status switch
    {
        CatalogStatus.Ready => MenuAvailability.Ready,
        CatalogStatus.Loading => MenuAvailability.LoadingCatalog,
        CatalogStatus.Failed => MenuAvailability.CatalogFailed,
        CatalogStatus.Unsupported => MenuAvailability.Unsupported,
        _ => MenuAvailability.WaitingForSave,
    };

    /// <summary>Settings can be edited only for a loaded save with a ready catalog.</summary>
    public bool CanEdit => Availability == MenuAvailability.Ready && _settingsLoaded;

    /// <summary>
    /// Phase 03: the saved policy of the active save was read successfully (or the save has none yet, so the
    /// documented defaults apply). False before loading and while the file is unreadable: new customer orders
    /// then stay vanilla instead of silently using defaults.
    /// </summary>
    public bool PolicyReadyForSales => _settingsLoaded && !_settingsUnreadable;

    /// <summary>
    /// Phase 06: the active save's settings file was written by a newer build. It is never overwritten or downgraded,
    /// so saving is refused and mod sales stay off.
    /// </summary>
    public bool SettingsFromNewerVersion => _settingsNewer;

    /// <summary>Phase 06: one line describing the settings file of the active save (Diagnostics).</summary>
    public string SettingsFileState => _settingsFileState;

    public string AvailabilityText => Availability switch
    {
        MenuAvailability.Ready => "Product catalog ready",
        MenuAvailability.LoadingCatalog => MenuText.WaitingForProducts,
        MenuAvailability.CatalogFailed => MenuText.CatalogFailed + (CatalogState.Message is null ? string.Empty : " " + CatalogState.Message),
        MenuAvailability.Unsupported => MenuText.Unsupported + (CatalogState.Message is null ? string.Empty : " " + CatalogState.Message),
        _ => MenuText.WaitingForSave,
    };

    /// <summary>Anonymised save label (e.g. "save-3fa2c19b04de"), or null without a save.</summary>
    public string? ActiveSaveLabel => _session.IsNone ? null : _session.SaveIdentity;

    public PolicyConfiguration Draft => _draft;

    public PolicyConfiguration Saved => _saved;

    public bool HasUnsavedChanges => !string.Equals(Fingerprint(_draft), _savedFingerprint, StringComparison.Ordinal);

    public bool IsModEnabled => _draft.Enabled;

    public CategoryKey SelectedCategory => _category;

    /// <summary>The dealer whose overrides are being edited, or null for the global rules.</summary>
    public string? EditingDealer => _dealer;

    /// <summary>"Global rules" or "Overrides for &lt;dealer&gt;".</summary>
    public string ScopeLabel => _dealer is null ? "Global rules" : "Overrides for " + DealerName(_dealer);

    public MixingMode Mixing => _draft.Mixing;

    public bool PlayerSalesEnabled => _draft.PlayerSalesEnabled;

    public bool DealerSalesEnabled => _draft.DealerSalesEnabled;

    /// <summary>The preset the draft matches, or "Custom".</summary>
    public string ActivePresetLabel => PolicyPresets.DetectLabel(_draft);

    public PolicyPreset? PendingPreset => _pendingPreset;

    /// <summary>What the pending preset would change (empty when none is pending or nothing changes).</summary>
    public IReadOnlyList<string> PresetPreview => _presetPreview;

    public string SearchText => _search;

    // ----------------------------------------------------------------- views

    /// <summary>Categories of the active catalog with their draft rule summary (empty until Ready).</summary>
    public IReadOnlyList<CategorySummaryView> GetCategories()
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null)
        {
            return Array.Empty<CategorySummaryView>();
        }

        // Evaluate with the mod "enabled" so a disabled mod still shows what each rule would permit.
        var scoped = ScopedDraft;
        var policy = scoped.Enabled ? scoped : scoped.WithEnabled(true);
        return snapshot.Categories.Select(c =>
        {
            var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, c.Key);
            return new CategorySummaryView(c.Key, MenuText.CategoryLabel(c.Key, c.DisplayName), summary.Mode, summary.UnlockedCount,
                summary.EligibleProducts.Count, summary.SelectedCount, summary.InactiveSelections.Count, summary.Reason)
            {
                RuleDetail = RuleDetail(policy.GetEffectivePolicy(c.Key)),
                IsDealerOverride = _dealer is not null && _draft.GetDealerOverride(_dealer, c.Key) is not null,
            };
        }).ToArray();
    }

    public CategorySummaryView? GetSelectedCategorySummary()
        => _category.IsValid ? GetCategories().FirstOrDefault(c => c.Key == _category) : null;

    /// <summary>The current page of products for the selected category, filtered by the search text.</summary>
    public ProductPageView GetProductPage()
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !_category.IsValid || !snapshot.HasCategory(_category))
        {
            return new ProductPageView(Array.Empty<ProductRowView>(), 0, 0, 0, 0);
        }

        var filtered = Filtered(snapshot);
        var size = Math.Max(1, PageSize);
        var pageCount = Math.Max(1, (filtered.Count + size - 1) / size);
        _page = Math.Min(Math.Max(0, _page), pageCount - 1);

        var all = snapshot.GetProducts(_category);
        var duplicateNames = new HashSet<string>(
            all.GroupBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);
        var scoped = ScopedDraft;
        var policy = scoped.Enabled ? scoped : scoped.WithEnabled(true);
        var rule = scoped.GetEffectivePolicy(_category);

        var rows = filtered.Skip(_page * size).Take(size).Select(p => new ProductRowView(
            p,
            rule.IsListed(p.Key),
            rule.Mode == PolicyMode.Blocklist || p.IsUnlocked,
            SaleEligibilityPolicy.Evaluate(policy, snapshot, _category, p.Key),
            duplicateNames.Contains(p.DisplayName),
            Lineage(snapshot, p))).ToArray();

        return new ProductPageView(rows, _page, pageCount, filtered.Count, all.Count);
    }

    /// <summary>Hypothetical "5 units of this category" preview using the draft rules.</summary>
    public PolicyPreviewView? GetPreview(int quantity = PreviewQuantity)
    {
        var snapshot = _catalog.State.Snapshot;
        if (!_category.IsValid)
        {
            return null;
        }

        var scoped = ScopedDraft;
        var summary = SaleEligibilityPolicy.EvaluateCategory(scoped, snapshot, _category);
        var names = summary.EligibleProducts.Select(p => p.DisplayName).Take(PreviewNameLimit).ToList();
        if (summary.EligibleProducts.Count > PreviewNameLimit)
        {
            names.Add($"and {summary.EligibleProducts.Count - PreviewNameLimit} more");
        }

        var lines = new List<string>();
        if (snapshot is not null && snapshot.HasCategory(_category))
        {
            // Selected products first, then the rest, so the player sees the effect of their choices.
            var rule = scoped.GetEffectivePolicy(_category);
            foreach (var product in snapshot.GetProducts(_category)
                         .OrderByDescending(p => rule.IsListed(p.Key))
                         .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .Take(PreviewLineLimit))
            {
                var decision = SaleEligibilityPolicy.Evaluate(scoped, snapshot, _category, product.Key);
                lines.Add($"{product.DisplayName}: {(decision.IsEligible ? "Permitted" : "Not permitted - " + MenuText.ShortReason(decision.Reason))}");
            }
        }

        return new PolicyPreviewView(_category, quantity, summary.Reason, names, summary.EligibleProducts.Count, lines, HasUnsavedChanges);
    }

    /// <summary>Saved selections for the selected category that this save cannot use.</summary>
    public IReadOnlyList<ProductKey> GetInactiveSelections()
        => _category.IsValid
            ? SaleEligibilityPolicy.EvaluateCategory(ScopedDraft.WithEnabled(true), _catalog.State.Snapshot, _category).InactiveSelections
            : Array.Empty<ProductKey>();

    /// <summary>The selected category's rule in the current scope (null without a category).</summary>
    public CategoryPolicy? GetSelectedRule() => _category.IsValid ? ScopedDraft.GetEffectivePolicy(_category) : null;

    /// <summary>
    /// Effects for the Rules screen: every effect on a discovered product of the selected category, plus saved effect
    /// ids no discovered product has (shown as inactive), sorted by id.
    /// </summary>
    public IReadOnlyList<EffectRowView> GetEffectRows()
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !_category.IsValid)
        {
            return Array.Empty<EffectRowView>();
        }

        var rule = ScopedDraft.GetEffectivePolicy(_category).Effects;
        var discovered = new HashSet<string>(snapshot.GetEffects(_category), StringComparer.Ordinal);
        return discovered.Concat(rule.Required).Concat(rule.Excluded)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .Select(e => new EffectRowView(
                e,
                rule.Required.Contains(e) ? EffectState.Required : rule.Excluded.Contains(e) ? EffectState.Excluded : EffectState.None,
                discovered.Contains(e),
                rule.Conflicts.Contains(e)))
            .ToArray();
    }

    /// <summary>Dealers for the Sales screen: those the host has seen plus any with saved overrides.</summary>
    public IReadOnlyList<DealerScopeView> GetDealers()
    {
        var seen = SafeDealers();
        var ids = seen.Select(d => d.Id).Concat(_draft.DealerOverrides.Keys).Distinct(StringComparer.Ordinal);
        return ids.Select(id =>
            {
                var info = seen.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));
                var count = _draft.DealerOverrides.TryGetValue(id, out var map) ? map.Count : 0;
                return new DealerScopeView(id, info?.Name ?? id, info?.Hired ?? false, info is not null, count, string.Equals(id, _dealer, StringComparison.Ordinal));
            })
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }

    // --------------------------------------------------------------- actions

    public void Navigate(MenuScreen screen)
    {
        if (Screen == screen)
        {
            return;
        }

        Screen = screen;
        IsResetPending = screen == MenuScreen.Settings && IsResetPending;
        Touch();
    }

    public bool SelectCategory(CategoryKey category)
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !category.IsValid || !snapshot.HasCategory(category))
        {
            return false;
        }

        if (_category != category)
        {
            _category = category;
            _page = 0;
        }

        Screen = Screen == MenuScreen.Rules ? MenuScreen.Rules : MenuScreen.Products;
        Touch();
        return true;
    }

    public void SetSearch(string? text)
    {
        var normalized = ProductSearch.Normalize(text);
        if (string.Equals(normalized, _search, StringComparison.Ordinal))
        {
            return;
        }

        _search = normalized;
        _page = 0;
        Touch();
    }

    public void NextPage()
    {
        _page++;
        Touch();
    }

    public void PreviousPage()
    {
        if (_page > 0)
        {
            _page--;
            Touch();
        }
    }

    public bool SetModEnabled(bool enabled)
        => Edit(d => d.Enabled == enabled ? d : d.WithEnabled(enabled));

    /// <summary>Phase 03: whether one order may be filled with several eligible products of its category.</summary>
    public bool MixedDeliveries => _draft.MixedDeliveries;

    public bool SetMixedDeliveries(bool allowed)
        => Edit(d => d.MixedDeliveries == allowed ? d : d.WithMixedDeliveries(allowed));

    /// <summary>Phase 05: how products may be combined in one order.</summary>
    public bool SetMixing(MixingMode mixing)
        => Edit(d => d.Mixing == mixing ? d : d.WithMixing(mixing));

    public bool SetPlayerSales(bool enabled)
        => Edit(d => d.PlayerSalesEnabled == enabled ? d : d.WithPlayerSales(enabled));

    public bool SetDealerSales(bool enabled)
        => Edit(d => d.DealerSalesEnabled == enabled ? d : d.WithDealerSales(enabled));

    public bool SetMode(CategoryKey category, PolicyMode mode)
        => EditCategory(category, rule => rule.WithMode(mode));

    public bool SetMatch(CategoryKey category, SelectionMatch match)
        => EditCategory(category, rule => rule.WithMatch(match));

    /// <summary>Quality minimum for the category (null or Trash: any quality).</summary>
    public bool SetMinimumQuality(CategoryKey category, QualityTier? minimum)
        => EditCategory(category, rule => rule.MinimumQuality == (minimum == QualityTier.Trash ? null : minimum) ? rule : rule.WithMinimumQuality(minimum));

    /// <summary>Cycles one effect: any, required, excluded, any.</summary>
    public bool CycleEffect(CategoryKey category, string effectId)
        => !string.IsNullOrWhiteSpace(effectId) && EditCategory(category, rule => rule.WithEffects(rule.Effects.Cycle(effectId)));

    public bool SetEffectMatch(CategoryKey category, EffectMatch match)
        => EditCategory(category, rule => rule.Effects.Match == match ? rule : rule.WithEffects(rule.Effects.WithMatch(match)));

    public bool ClearEffects(CategoryKey category)
        => EditCategory(category, rule => rule.Effects.IsEmpty ? rule : rule.WithEffects(EffectRule.None));

    /// <summary>Selects a product for the allowed list, or (in Blocklist mode) puts it on the blocked list.</summary>
    public bool SetProductSelected(CategoryKey category, ProductKey product, bool selected)
    {
        var blocking = ScopedDraft.GetEffectivePolicy(category).Mode == PolicyMode.Blocklist;
        if (selected && !(blocking ? InCategory(category, product) : IsSelectable(category, product)))
        {
            // Removing from a list is always allowed (e.g. a stale entry); allowing needs an unlocked product.
            return false;
        }

        return EditCategory(category, rule =>
        {
            var keys = new HashSet<ProductKey>(rule.Mode == PolicyMode.Blocklist ? rule.BlockedProducts : rule.SelectedProducts);
            var changed = selected ? keys.Add(product) : keys.Remove(product);
            return !changed ? rule : rule.Mode == PolicyMode.Blocklist ? rule.WithBlocked(keys) : rule.WithSelection(keys);
        });
    }

    public bool ToggleProduct(CategoryKey category, ProductKey product)
        => SetProductSelected(category, product, !ScopedDraft.GetEffectivePolicy(category).IsListed(product));

    /// <summary>Starts or stops editing one dealer's overrides (null: global rules).</summary>
    public bool EditDealer(string? dealerId)
    {
        if (dealerId is not null && !PolicyConfiguration.IsValidDealerId(dealerId))
        {
            return false;
        }

        if (string.Equals(_dealer, dealerId, StringComparison.Ordinal))
        {
            return false;
        }

        _dealer = dealerId;
        _page = 0;
        SetFeedback(FeedbackKind.Info, dealerId is null
            ? "Editing the global rules."
            : $"Editing overrides for {DealerName(dealerId)}. Changes to a category replace the global rule for this dealer only.");
        return true;
    }

    /// <summary>Removes the edited dealer's override of one category (it uses the global rule again).</summary>
    public bool UseGlobalRule(CategoryKey category)
        => _dealer is { } dealer && Edit(d => d.GetDealerOverride(dealer, category) is null ? d : d.WithoutDealerCategory(dealer, category));

    /// <summary>Removes every override of one dealer.</summary>
    public bool RemoveDealerOverrides(string dealerId)
        => Edit(d => d.DealerOverrides.ContainsKey(dealerId) ? d.WithoutDealer(dealerId) : d);

    /// <summary>Shows what a preset would change; nothing changes until <see cref="ConfirmPreset"/>.</summary>
    public bool RequestPreset(PolicyPreset preset)
    {
        if (!CanEdit)
        {
            SetFeedback(FeedbackKind.Warning, AvailabilityText);
            return false;
        }

        _pendingPreset = preset;
        _presetPreview = PolicyPresets.Preview(_draft, preset, _catalog.State.Snapshot);
        IsResetPending = false;
        SetFeedback(FeedbackKind.Warning, _presetPreview.Count == 0
            ? $"{PolicyPresets.Label(preset)} matches your current draft: nothing would change."
            : $"{PolicyPresets.Label(preset)} replaces rules in your draft ({_presetPreview.Count} change(s) listed). Confirm to apply.");
        return true;
    }

    public bool ConfirmPreset()
    {
        if (_pendingPreset is not { } preset || !CanEdit)
        {
            return false;
        }

        var next = PolicyPresets.Apply(_draft, preset);
        ClearPreset();
        if (!ReferenceEquals(next, _draft))
        {
            _draft = next;
        }

        SetFeedback(FeedbackKind.Warning, $"{PolicyPresets.Label(preset)} applied to the draft. Press Save to keep it or Discard to undo.");
        return true;
    }

    public void CancelPreset()
    {
        if (_pendingPreset is not null)
        {
            ClearPreset();
            SetFeedback(FeedbackKind.Info, "Preset not applied.");
        }
    }

    /// <summary>Selects every unlocked product of the whole category (not just the visible search results).</summary>
    public bool SelectAllUnlocked(CategoryKey category)
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !snapshot.HasCategory(category))
        {
            return false;
        }

        var unlocked = snapshot.GetUnlockedProducts(category).Select(p => p.Key);
        return EditCategory(category, rule => rule.WithActiveList(EditedList(rule).Concat(unlocked)));
    }

    /// <summary>Selects the unlocked products that match the current search.</summary>
    public bool SelectAllVisible()
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !_category.IsValid || !snapshot.HasCategory(_category))
        {
            return false;
        }

        var visible = Filtered(snapshot).Where(p => p.IsUnlocked).Select(p => p.Key).ToArray();
        return EditCategory(_category, rule => rule.WithActiveList(EditedList(rule).Concat(visible)));
    }

    /// <summary>Clears the active list (allowed, or blocked in Blocklist mode) of one category only, including stale entries.</summary>
    public bool ClearSelection(CategoryKey category)
        => EditCategory(category, rule => EditedList(rule).Count == 0 ? rule : rule.WithActiveList(Array.Empty<ProductKey>()));

    /// <summary>The list the Products screen edits: the blocked list in Blocklist mode, otherwise the allowed list (kept in every mode).</summary>
    private static IReadOnlyList<ProductKey> EditedList(CategoryPolicy rule)
        => rule.Mode == PolicyMode.Blocklist ? rule.BlockedProducts : rule.SelectedProducts;

    /// <summary>Validates and persists the draft for the active save. Reports success only after the write succeeded.</summary>
    public bool Save()
    {
        if (!CanEdit || _session.IsNone)
        {
            SetFeedback(FeedbackKind.Error, "Nothing to save: no save is loaded or the product catalog is not ready.");
            return false;
        }

        if (!_catalog.State.Session.Equals(_session))
        {
            SetFeedback(FeedbackKind.Error, "The active save changed; these settings were not saved.");
            return false;
        }

        if (_settingsNewer)
        {
            SetFeedback(FeedbackKind.Error, "Not saved: this save's settings were written by a newer version of the mod and are kept unchanged.");
            return false;
        }

        var settings = _draft.ToSettings();
        var validated = PolicyConfiguration.FromSettings(settings, out var issues);
        if (issues.Count > 0)
        {
            SetFeedback(FeedbackKind.Error, "Settings are invalid and were not saved: " + issues[0].Message);
            return false;
        }

        try
        {
            _store.Save(_session.SaveIdentity, settings);
        }
        catch (Exception ex)
        {
            _log?.Error("Saving menu settings failed.", ex);
            SetFeedback(FeedbackKind.Error, $"Settings were NOT saved: {ex.Message}");
            return false;
        }

        _settingsUnreadable = false;
        _settingsFileState = "saved";

        ApplySaved(validated);
        _draft = _saved;
        IsResetPending = false;
        _log?.Info($"Menu settings saved for {_session.SaveIdentity}.");
        SetFeedback(FeedbackKind.Success, MenuText.Saved);
        return true;
    }

    public void Discard()
    {
        if (!HasUnsavedChanges)
        {
            SetFeedback(FeedbackKind.Info, "No unsaved changes.");
            return;
        }

        _draft = _saved;
        IsResetPending = false;
        _pendingPreset = null;
        _presetPreview = Array.Empty<string>();
        SetFeedback(FeedbackKind.Info, "Unsaved changes discarded.");
    }

    public void RequestReset()
    {
        if (!CanEdit)
        {
            return;
        }

        IsResetPending = true;
        SetFeedback(FeedbackKind.Warning, "Reset every category to Any Unlocked, clear all lists, effect and quality rules and dealer overrides, and turn every switch on? Confirm to continue.");
    }

    public void CancelReset()
    {
        if (IsResetPending)
        {
            IsResetPending = false;
            SetFeedback(FeedbackKind.Info, "Reset cancelled.");
        }
    }

    /// <summary>Puts the defaults into the draft. Like every edit, it is kept only after <see cref="Save"/>.</summary>
    public bool ConfirmReset()
    {
        if (!IsResetPending || !CanEdit)
        {
            return false;
        }

        IsResetPending = false;
        _draft = PolicyConfiguration.Default;
        SetFeedback(FeedbackKind.Warning, "Defaults restored in the draft. Press Save to keep them.");
        return true;
    }

    public void DismissFeedback()
    {
        if (Feedback.Kind != FeedbackKind.None)
        {
            Feedback = MenuFeedback.Empty;
            Touch();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _catalog.StateChanged -= OnCatalogStateChanged;
        Changed = null;
    }

    // -------------------------------------------------------------- internals

    private void OnCatalogStateChanged(CatalogState state)
    {
        if (!state.Session.Equals(_session))
        {
            SwitchSession(state.Session);
        }

        // Keep the category selection only while it exists in the new catalog.
        if (_category.IsValid && (state.Snapshot is null || !state.Snapshot.HasCategory(_category)))
        {
            if (state.Snapshot is not null || state.Session.IsNone)
            {
                _category = default;
                if (Screen == MenuScreen.Products)
                {
                    Screen = MenuScreen.Overview;
                }
            }
        }

        _filterGeneration = -1;
        Touch();
    }

    private void SwitchSession(SaveSession session)
    {
        if (!_session.IsNone && HasUnsavedChanges)
        {
            _log?.Warning($"Unsaved menu changes for {_session.SaveIdentity} were discarded because the active save changed.");
            Feedback = new MenuFeedback(FeedbackKind.Warning, "Unsaved changes for the previous save were discarded.");
        }

        _session = session;
        _category = default;
        if (Screen == MenuScreen.Products)
        {
            Screen = MenuScreen.Overview;
        }

        _search = string.Empty;
        _page = 0;
        _dealer = null;
        _pendingPreset = null;
        _presetPreview = Array.Empty<string>();
        IsResetPending = false;
        _settingsLoaded = false;
        _settingsUnreadable = false;
        _settingsNewer = false;
        _settingsFileState = session.IsNone ? "no save loaded" : "not loaded";

        if (session.IsNone)
        {
            ApplySaved(PolicyConfiguration.Default);
            _draft = _saved;
            return;
        }

        PolicyConfiguration loaded;
        PolicyConfiguration? recovered = null;
        var result = SafeLoad(session.SaveIdentity);
        switch (result.Status)
        {
            case PolicyLoadStatus.Loaded:
                loaded = PolicyConfiguration.FromSettings(result.Settings, out var issues);
                foreach (var issue in issues)
                {
                    _log?.Warning($"Menu settings for {session.SaveIdentity}: {issue}");
                }

                if (result.Settings is { UnknownFields.Count: > 0 } withUnknown)
                {
                    _log?.Warning($"Menu settings for {session.SaveIdentity} contain fields this version does not know ({string.Join(", ", withUnknown.UnknownFields.Take(10))}); " +
                                  "they are not kept when the settings are next saved (the previous file is kept as .bak).");
                }

                if (issues.Count > 0)
                {
                    Feedback = new MenuFeedback(FeedbackKind.Warning, $"{issues.Count} setting(s) were invalid and were ignored or made safer. See the log.");
                }

                _settingsFileState = issues.Count > 0 ? $"loaded with {issues.Count} issue(s) (made safer)" : "loaded";
                break;
            case PolicyLoadStatus.NewerSchema:
                _log?.Warning($"Menu settings for {session.SaveIdentity}: {result.Message}");
                Feedback = new MenuFeedback(FeedbackKind.Warning, "These settings were saved by a newer version of the mod. They are kept unchanged; mod sales are off and saving is disabled.");
                loaded = PolicyConfiguration.Default;
                _settingsUnreadable = true;
                _settingsNewer = true;
                _settingsFileState = "written by a newer version (kept unchanged; mod sales off)";
                break;
            case PolicyLoadStatus.Unreadable:
                _log?.Warning($"Menu settings for {session.SaveIdentity} are unreadable; the file is kept and mod sales stay off. {result.Message} {result.RecoveryNote}");
                loaded = PolicyConfiguration.Default;
                _settingsUnreadable = true;
                if (result.Recovery is not null)
                {
                    recovered = PolicyConfiguration.FromSettings(result.Recovery, out _);
                    Feedback = new MenuFeedback(FeedbackKind.Warning,
                        "Saved settings are damaged (the file is kept). Your last good settings were found in the backup and are shown as unsaved changes: review them and press Save to restore them, or Discard. Mod sales stay off until then.");
                    _settingsFileState = "damaged; verified backup offered as unsaved changes (mod sales off until saved)";
                }
                else
                {
                    Feedback = new MenuFeedback(FeedbackKind.Warning, "Saved settings could not be read; defaults are shown and mod sales stay off. The file is kept; saving replaces it (a copy is kept).");
                    _settingsFileState = "damaged, no verified backup (mod sales off until saved)";
                }

                break;
            default:
                loaded = PolicyConfiguration.Default;
                _settingsFileState = "none yet (defaults)";
                break;
        }

        ApplySaved(loaded);
        _draft = recovered ?? _saved;
        _settingsLoaded = true;
    }

    private PolicyLoadResult SafeLoad(string saveIdentity)
    {
        try
        {
            return _store.Load(saveIdentity);
        }
        catch (Exception ex)
        {
            _log?.Error("Loading menu settings failed.", ex);
            return new PolicyLoadResult(PolicyLoadStatus.Unreadable, null, ex.Message);
        }
    }

    private void ApplySaved(PolicyConfiguration configuration)
    {
        _saved = configuration;
        _savedFingerprint = Fingerprint(configuration);
        _provider.Update(_ => configuration);
    }

    /// <summary>The draft as the current scope sees it (global, or global plus the edited dealer's overrides).</summary>
    private PolicyConfiguration ScopedDraft => _dealer is null ? _draft : _draft.ForScope(SalesScope.Dealer(_dealer));

    private void ClearPreset()
    {
        _pendingPreset = null;
        _presetPreview = Array.Empty<string>();
    }

    private IReadOnlyList<DealerInfo> SafeDealers()
    {
        try
        {
            return Dealers?.Invoke() ?? Array.Empty<DealerInfo>();
        }
        catch (Exception ex)
        {
            _log?.Error("Reading the dealer list failed.", ex);
            return Array.Empty<DealerInfo>();
        }
    }

    private string DealerName(string id)
        => SafeDealers().FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal))?.Name ?? id;

    private static string RuleDetail(CategoryPolicy rule)
    {
        var parts = new List<string>();
        if (rule.Match == SelectionMatch.BaseStrain && rule.Mode is PolicyMode.SelectedOnly or PolicyMode.Blocklist)
        {
            parts.Add("base strain");
        }

        if (rule.MinimumQuality is { } q)
        {
            parts.Add(QualityTiers.Label(q) + "+");
        }

        var effects = rule.Effects.Required.Count + rule.Effects.Excluded.Count;
        if (effects > 0)
        {
            parts.Add(effects == 1 ? "1 effect rule" : $"{effects} effect rules");
        }

        return string.Join(" - ", parts);
    }

    private static string Lineage(ProductCatalogSnapshot snapshot, ProductDescriptor product)
    {
        if (!product.AncestryKnown)
        {
            return "lineage unknown";
        }

        if (product.Parents.Count == 0)
        {
            return "base strain";
        }

        var names = snapshot.GetBaseStrains(product.Key)
            .Select(k => snapshot.TryGetProduct(k, out var d) ? d.DisplayName : k.Value)
            .ToList();
        return "from " + string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" and {names.Count - 3} more" : string.Empty);
    }

    private bool InCategory(CategoryKey category, ProductKey product)
    {
        var snapshot = _catalog.State.Snapshot;
        return snapshot is not null && snapshot.TryGetProduct(product, out var p) && p.Category == category;
    }

    private bool IsSelectable(CategoryKey category, ProductKey product)
    {
        var snapshot = _catalog.State.Snapshot;
        return snapshot is not null && snapshot.TryGetProduct(product, out var p) && p.IsUnlocked && p.Category == category;
    }

    private bool EditCategory(CategoryKey category, Func<CategoryPolicy, CategoryPolicy> change)
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !category.IsValid || !snapshot.HasCategory(category))
        {
            return false;
        }

        return Edit(d =>
        {
            if (_dealer is { } dealer)
            {
                // Dealer scope: the first change copies the global rule into an override for this dealer.
                var inherited = d.GetDealerOverride(dealer, category) ?? d.GetEffectivePolicy(category);
                var changed = change(inherited);
                return ReferenceEquals(changed, inherited) ? d : d.WithDealerCategory(dealer, category, changed);
            }

            var current = d.GetEffectivePolicy(category);
            var next = change(current);
            if (ReferenceEquals(next, current))
            {
                return d;
            }

            // Keep the configuration minimal: a rule equal to the default is stored as "no override".
            return next.Mode == d.DefaultMode && next.SelectedProducts.Count == 0 && next.BlockedProducts.Count == 0 && !next.HasAdvancedRules
                ? d.WithoutCategory(category)
                : d.WithCategory(category, next);
        });
    }

    private bool Edit(Func<PolicyConfiguration, PolicyConfiguration> change)
    {
        if (!CanEdit)
        {
            SetFeedback(FeedbackKind.Warning, AvailabilityText);
            return false;
        }

        var next = change(_draft);
        if (ReferenceEquals(next, _draft))
        {
            return false;
        }

        _draft = next;
        IsResetPending = false;
        ClearPreset(); // a pending preview no longer describes the draft
        Feedback = MenuFeedback.Empty;
        Touch();
        return true;
    }

    private List<ProductDescriptor> Filtered(ProductCatalogSnapshot snapshot)
    {
        if (_filterGeneration != snapshot.Generation || _filterCategory != _category || !string.Equals(_filterSearch, _search, StringComparison.Ordinal))
        {
            _filtered = ProductSearch.Filter(snapshot.GetProducts(_category), _search);
            _filtered.Sort((a, b) =>
            {
                var byName = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.Key.CompareTo(b.Key);
            });
            _filterGeneration = snapshot.Generation;
            _filterCategory = _category;
            _filterSearch = _search;
        }

        return _filtered;
    }

    private void SetFeedback(FeedbackKind kind, string text)
    {
        Feedback = new MenuFeedback(kind, text);
        Touch();
    }

    private void Touch()
    {
        ViewVersion++;
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log?.Error("A menu Changed handler threw.", ex);
        }
    }

    private static string Fingerprint(PolicyConfiguration configuration) => PolicySettingsJson.ToJson(configuration.ToSettings());
}
