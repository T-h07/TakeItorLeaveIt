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
    private bool _disposed;

    private CategoryKey _category;
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
        var policy = _draft.Enabled ? _draft : _draft.WithEnabled(true);
        return snapshot.Categories.Select(c =>
        {
            var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, c.Key);
            return new CategorySummaryView(c.Key, MenuText.CategoryLabel(c.Key, c.DisplayName), summary.Mode, summary.UnlockedCount,
                summary.EligibleProducts.Count, summary.SelectedCount, summary.InactiveSelections.Count, summary.Reason);
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
        var policy = _draft.Enabled ? _draft : _draft.WithEnabled(true);
        var rule = _draft.GetEffectivePolicy(_category);

        var rows = filtered.Skip(_page * size).Take(size).Select(p => new ProductRowView(
            p,
            rule.IsSelected(p.Key),
            p.IsUnlocked,
            SaleEligibilityPolicy.Evaluate(policy, snapshot, _category, p.Key),
            duplicateNames.Contains(p.DisplayName))).ToArray();

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

        var summary = SaleEligibilityPolicy.EvaluateCategory(_draft, snapshot, _category);
        var names = summary.EligibleProducts.Select(p => p.DisplayName).Take(PreviewNameLimit).ToList();
        if (summary.EligibleProducts.Count > PreviewNameLimit)
        {
            names.Add($"and {summary.EligibleProducts.Count - PreviewNameLimit} more");
        }

        var lines = new List<string>();
        if (snapshot is not null && snapshot.HasCategory(_category))
        {
            // Selected products first, then the rest, so the player sees the effect of their choices.
            var rule = _draft.GetEffectivePolicy(_category);
            foreach (var product in snapshot.GetProducts(_category)
                         .OrderByDescending(p => rule.IsSelected(p.Key))
                         .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .Take(PreviewLineLimit))
            {
                var decision = SaleEligibilityPolicy.Evaluate(_draft, snapshot, _category, product.Key);
                lines.Add($"{product.DisplayName}: {(decision.IsEligible ? "Permitted" : "Not permitted - " + MenuText.ShortReason(decision.Reason))}");
            }
        }

        return new PolicyPreviewView(_category, quantity, summary.Reason, names, summary.EligibleProducts.Count, lines, HasUnsavedChanges);
    }

    /// <summary>Saved selections for the selected category that this save cannot use.</summary>
    public IReadOnlyList<ProductKey> GetInactiveSelections()
        => _category.IsValid
            ? SaleEligibilityPolicy.EvaluateCategory(_draft.WithEnabled(true), _catalog.State.Snapshot, _category).InactiveSelections
            : Array.Empty<ProductKey>();

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

        Screen = MenuScreen.Products;
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

    public bool SetMode(CategoryKey category, PolicyMode mode)
        => EditCategory(category, rule => rule.WithMode(mode));

    public bool SetProductSelected(CategoryKey category, ProductKey product, bool selected)
    {
        if (!IsSelectable(category, product))
        {
            // Deselecting is always allowed (e.g. removing a stale selection); selecting needs an unlocked product.
            if (selected)
            {
                return false;
            }
        }

        return EditCategory(category, rule =>
        {
            var keys = new HashSet<ProductKey>(rule.SelectedProducts);
            var changed = selected ? keys.Add(product) : keys.Remove(product);
            return changed ? rule.WithSelection(keys) : rule;
        });
    }

    public bool ToggleProduct(CategoryKey category, ProductKey product)
        => SetProductSelected(category, product, !_draft.GetEffectivePolicy(category).IsSelected(product));

    /// <summary>Selects every unlocked product of the whole category (not just the visible search results).</summary>
    public bool SelectAllUnlocked(CategoryKey category)
    {
        var snapshot = _catalog.State.Snapshot;
        if (snapshot is null || !snapshot.HasCategory(category))
        {
            return false;
        }

        var unlocked = snapshot.GetUnlockedProducts(category).Select(p => p.Key);
        return EditCategory(category, rule => rule.WithSelection(rule.SelectedProducts.Concat(unlocked)));
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
        return EditCategory(_category, rule => rule.WithSelection(rule.SelectedProducts.Concat(visible)));
    }

    /// <summary>Clears the selection of one category only (including stale selections).</summary>
    public bool ClearSelection(CategoryKey category)
        => EditCategory(category, rule => rule.SelectedProducts.Count == 0 ? rule : rule.WithSelection(Array.Empty<ProductKey>()));

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
        SetFeedback(FeedbackKind.Info, "Unsaved changes discarded.");
    }

    public void RequestReset()
    {
        if (!CanEdit)
        {
            return;
        }

        IsResetPending = true;
        SetFeedback(FeedbackKind.Warning, "Reset every category to Any Unlocked and clear all selections? Confirm to continue.");
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
        IsResetPending = false;
        _settingsLoaded = false;

        if (session.IsNone)
        {
            ApplySaved(PolicyConfiguration.Default);
            _draft = _saved;
            return;
        }

        PolicyConfiguration loaded;
        var result = SafeLoad(session.SaveIdentity);
        switch (result.Status)
        {
            case PolicyLoadStatus.Loaded:
                loaded = PolicyConfiguration.FromSettings(result.Settings, out var issues);
                foreach (var issue in issues)
                {
                    _log?.Warning($"Menu settings for {session.SaveIdentity}: {issue}");
                }

                if (issues.Count > 0)
                {
                    Feedback = new MenuFeedback(FeedbackKind.Warning, $"{issues.Count} setting(s) were invalid and were ignored or made safer. See the log.");
                }

                break;
            case PolicyLoadStatus.Unreadable:
                _log?.Warning($"Menu settings for {session.SaveIdentity} are unreadable; defaults are shown and the file is kept. {result.Message}");
                Feedback = new MenuFeedback(FeedbackKind.Warning, "Saved settings could not be read; defaults are shown. The file is kept until you save.");
                loaded = PolicyConfiguration.Default;
                break;
            default:
                loaded = PolicyConfiguration.Default;
                break;
        }

        ApplySaved(loaded);
        _draft = _saved;
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
            var current = d.GetEffectivePolicy(category);
            var next = change(current);
            if (ReferenceEquals(next, current))
            {
                return d;
            }

            // Keep the configuration minimal: a rule equal to the default is stored as "no override".
            return next.Mode == d.DefaultMode && next.SelectedProducts.Count == 0
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
