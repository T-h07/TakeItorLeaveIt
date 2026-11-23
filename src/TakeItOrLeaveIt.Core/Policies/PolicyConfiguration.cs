using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Policies;

/// <summary>
/// Immutable, validated policy configuration. Every change produces a new instance with a higher
/// <see cref="Revision"/>, so decisions can be tied to the configuration they were made with.
/// Categories without an explicit rule use <see cref="DefaultMode"/> (AnyUnlocked by default).
/// </summary>
public sealed class PolicyConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public static readonly PolicyConfiguration Default = new(true, PolicyMode.AnyUnlocked, new Dictionary<CategoryKey, CategoryPolicy>(), 0, true);

    private readonly Dictionary<CategoryKey, CategoryPolicy> _categories;
    private readonly CategoryPolicy _defaultPolicy;

    private PolicyConfiguration(bool enabled, PolicyMode defaultMode, Dictionary<CategoryKey, CategoryPolicy> categories, long revision, bool mixedDeliveries)
    {
        Enabled = enabled;
        MixedDeliveries = mixedDeliveries;
        DefaultMode = defaultMode;
        _categories = categories;
        _defaultPolicy = new CategoryPolicy(defaultMode, null);
        Revision = revision;
    }

    /// <summary>Global switch. When false, future integrations fall back to vanilla behaviour.</summary>
    public bool Enabled { get; }

    public PolicyMode DefaultMode { get; }

    /// <summary>
    /// Phase 03: whether one customer order may be filled with several different eligible products of its
    /// category (for example 6 OG Kush + 4 Sour Diesel for "10 Weed"). Default true.
    /// </summary>
    public bool MixedDeliveries { get; }

    public long Revision { get; }

    /// <summary>Explicit per-category rules, keyed by category.</summary>
    public IReadOnlyDictionary<CategoryKey, CategoryPolicy> CategoryOverrides => _categories;

    /// <summary>The rule that applies to <paramref name="category"/> (explicit rule or the default).</summary>
    public CategoryPolicy GetEffectivePolicy(CategoryKey category)
        => category.IsValid && _categories.TryGetValue(category, out var policy) ? policy : _defaultPolicy;

    public PolicyConfiguration WithEnabled(bool enabled)
        => new(enabled, DefaultMode, _categories, Revision + 1, MixedDeliveries);

    public PolicyConfiguration WithMixedDeliveries(bool allowed)
        => new(Enabled, DefaultMode, _categories, Revision + 1, allowed);

    public PolicyConfiguration WithDefaultMode(PolicyMode mode)
        => new(Enabled, new CategoryPolicy(mode, null).Mode, _categories, Revision + 1, MixedDeliveries);

    public PolicyConfiguration WithCategory(CategoryKey category, CategoryPolicy policy)
    {
        if (!category.IsValid)
        {
            throw new ArgumentException("Category key is invalid.", nameof(category));
        }

        var copy = new Dictionary<CategoryKey, CategoryPolicy>(_categories) { [category] = policy ?? throw new ArgumentNullException(nameof(policy)) };
        return new PolicyConfiguration(Enabled, DefaultMode, copy, Revision + 1, MixedDeliveries);
    }

    public PolicyConfiguration WithoutCategory(CategoryKey category)
    {
        var copy = new Dictionary<CategoryKey, CategoryPolicy>(_categories);
        return copy.Remove(category) ? new PolicyConfiguration(Enabled, DefaultMode, copy, Revision + 1, MixedDeliveries) : this;
    }

    /// <summary>
    /// Converts untrusted settings (e.g. loaded from a file in Phase 06) into a valid configuration.
    /// Never throws for bad input: invalid entries are dropped or made more restrictive, and every
    /// change is reported in <paramref name="issues"/>.
    /// </summary>
    public static PolicyConfiguration FromSettings(PolicySettings? settings, out IReadOnlyList<ConfigurationIssue> issues)
    {
        var found = new List<ConfigurationIssue>();
        issues = found;
        if (settings is null)
        {
            found.Add(new ConfigurationIssue(ConfigurationIssueKind.MissingSettings, null, null, "No settings supplied; defaults used."));
            return Default;
        }

        if (settings.SchemaVersion > CurrentSchemaVersion)
        {
            found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnsupportedSchemaVersion, null, settings.SchemaVersion.ToString(),
                $"Settings schema {settings.SchemaVersion} is newer than supported ({CurrentSchemaVersion}); every category is disabled to stay safe."));
            return new PolicyConfiguration(settings.Enabled, PolicyMode.Disabled, new Dictionary<CategoryKey, CategoryPolicy>(), 0, settings.MixedDeliveries);
        }

        var defaultMode = PolicyMode.AnyUnlocked;
        if (settings.DefaultMode is not null && !TryParseMode(settings.DefaultMode, out defaultMode))
        {
            defaultMode = PolicyMode.Disabled;
            found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownMode, null, settings.DefaultMode,
                "Unknown default mode; treated as Disabled."));
        }

        var categories = new Dictionary<CategoryKey, CategoryPolicy>();
        foreach (var pair in settings.Categories ?? new Dictionary<string, CategoryPolicySettings>())
        {
            if (!CategoryKey.TryCreate(pair.Key, out var category))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidCategoryKey, pair.Key, pair.Key, "Invalid category key; entry ignored."));
                continue;
            }

            if (categories.ContainsKey(category))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.DuplicateCategory, category.Value, pair.Key,
                    "Category listed more than once (keys differ only by case); later entry ignored."));
                continue;
            }

            var entry = pair.Value ?? new CategoryPolicySettings();
            var mode = defaultMode;
            if (entry.Mode is not null && !TryParseMode(entry.Mode, out mode))
            {
                mode = PolicyMode.Disabled;
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownMode, category.Value, entry.Mode, "Unknown mode; category treated as Disabled."));
            }

            var selected = new List<ProductKey>();
            var seen = new HashSet<ProductKey>();
            foreach (var raw in entry.SelectedProducts ?? new List<string>())
            {
                if (!ProductKey.TryCreate(raw, out var product))
                {
                    found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidProductKey, category.Value, raw, "Invalid product key; ignored."));
                    continue;
                }

                if (!seen.Add(product))
                {
                    found.Add(new ConfigurationIssue(ConfigurationIssueKind.DuplicateProduct, category.Value, raw, "Duplicate selection; counted once."));
                    continue;
                }

                selected.Add(product);
            }

            categories.Add(category, new CategoryPolicy(mode, selected));
        }

        return new PolicyConfiguration(settings.Enabled, defaultMode, categories, 0, settings.MixedDeliveries);
    }

    /// <summary>Plain, serializable form of this configuration.</summary>
    public PolicySettings ToSettings() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Enabled = Enabled,
        DefaultMode = FormatMode(DefaultMode),
        MixedDeliveries = MixedDeliveries,
        Categories = _categories
            .OrderBy(p => p.Key)
            .ToDictionary(
                p => p.Key.Value,
                p => new CategoryPolicySettings
                {
                    Mode = FormatMode(p.Value.Mode),
                    SelectedProducts = p.Value.SelectedProducts.Select(k => k.Value).ToList(),
                }),
    };

    /// <summary>Parses "anyUnlocked" / "selectedOnly" / "disabled" (case-insensitive, as in MOD_MENU_SPEC.md).</summary>
    public static bool TryParseMode(string? text, out PolicyMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "anyunlocked":
                mode = PolicyMode.AnyUnlocked;
                return true;
            case "selectedonly":
                mode = PolicyMode.SelectedOnly;
                return true;
            case "disabled":
                mode = PolicyMode.Disabled;
                return true;
            default:
                mode = PolicyMode.Disabled;
                return false;
        }
    }

    public static string FormatMode(PolicyMode mode) => mode switch
    {
        PolicyMode.AnyUnlocked => "anyUnlocked",
        PolicyMode.SelectedOnly => "selectedOnly",
        _ => "disabled",
    };
}

/// <summary>Untrusted, serializable settings (shape matches docs/MOD_MENU_SPEC.md). Validate with <see cref="PolicyConfiguration.FromSettings"/>.</summary>
public sealed class PolicySettings
{
    public int SchemaVersion { get; set; } = PolicyConfiguration.CurrentSchemaVersion;

    public bool Enabled { get; set; } = true;

    public string? DefaultMode { get; set; }

    /// <summary>Optional in the file (added in Phase 03); missing means true.</summary>
    public bool MixedDeliveries { get; set; } = true;

    public Dictionary<string, CategoryPolicySettings>? Categories { get; set; }
}

public sealed class CategoryPolicySettings
{
    public string? Mode { get; set; }

    public List<string>? SelectedProducts { get; set; }
}

public enum ConfigurationIssueKind
{
    MissingSettings,
    UnsupportedSchemaVersion,
    UnknownMode,
    InvalidCategoryKey,
    DuplicateCategory,
    InvalidProductKey,
    DuplicateProduct,
}

/// <summary>A problem found while validating settings, and what was done about it.</summary>
public sealed class ConfigurationIssue
{
    public ConfigurationIssue(ConfigurationIssueKind kind, string? category, string? value, string message)
    {
        Kind = kind;
        Category = category;
        Value = value;
        Message = message;
    }

    public ConfigurationIssueKind Kind { get; }

    public string? Category { get; }

    public string? Value { get; }

    public string Message { get; }

    public override string ToString() => $"{Kind} [{Category ?? "-"}] '{Value}': {Message}";
}

/// <summary>Read-only access to the active configuration. Phase 06 supplies a per-save, persisted implementation.</summary>
public interface IPolicyProvider
{
    PolicyConfiguration Current { get; }

    event Action<PolicyConfiguration>? Changed;
}

/// <summary>In-memory provider used in Phase 01 (defaults) and in tests.</summary>
public sealed class InMemoryPolicyProvider : IPolicyProvider
{
    private readonly object _gate = new();
    private PolicyConfiguration _current;

    public InMemoryPolicyProvider(PolicyConfiguration? initial = null)
    {
        _current = initial ?? PolicyConfiguration.Default;
    }

    public event Action<PolicyConfiguration>? Changed;

    public PolicyConfiguration Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Atomically replaces the configuration using <paramref name="update"/>.</summary>
    public PolicyConfiguration Update(Func<PolicyConfiguration, PolicyConfiguration> update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        PolicyConfiguration next;
        lock (_gate)
        {
            next = update(_current) ?? throw new InvalidOperationException("Policy update returned null.");
            _current = next;
        }

        Changed?.Invoke(next);
        return next;
    }
}
