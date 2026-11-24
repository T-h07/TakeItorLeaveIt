using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Policies;

/// <summary>How one order may combine products (Phase 05; extends the Phase 03 mixed-deliveries switch).</summary>
public enum MixingMode
{
    /// <summary>Any permitted products may be combined (Phase 03 "mixed deliveries ON").</summary>
    AnyPermitted = 0,

    /// <summary>One product identity per order (Phase 03 "mixed deliveries OFF").</summary>
    SingleProduct = 1,

    /// <summary>Several products only when they share a base strain (the game's recorded mix ancestry).</summary>
    SingleBaseStrain = 2,

    /// <summary>No substitution: only the product(s) the customer asked for, if your rules permit them.</summary>
    RequestedOnly = 3,
}

/// <summary>Which sales path a policy is resolved for: the player, or one hired dealer (by the game's NPC id).</summary>
public readonly struct SalesScope : IEquatable<SalesScope>
{
    private SalesScope(string? dealerId)
    {
        DealerId = dealerId;
    }

    public static SalesScope Player => default;

    public static SalesScope Dealer(string? dealerId) => new(string.IsNullOrWhiteSpace(dealerId) ? string.Empty : dealerId!.Trim());

    /// <summary>Null for the player; the dealer's id (possibly empty when unknown) for a dealer.</summary>
    public string? DealerId { get; }

    public bool IsDealer => DealerId is not null;

    public bool Equals(SalesScope other) => string.Equals(DealerId, other.DealerId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SalesScope other && Equals(other);

    public override int GetHashCode() => DealerId is null ? 0 : StringComparer.Ordinal.GetHashCode(DealerId);

    public override string ToString() => IsDealer ? $"dealer {DealerId}" : "player";
}

/// <summary>
/// Immutable, validated policy configuration. Every change produces a new instance with a higher
/// <see cref="Revision"/>, so decisions can be tied to the configuration they were made with.
/// Categories without an explicit rule use <see cref="DefaultMode"/> (AnyUnlocked by default).
/// <para>Phase 05 adds the mixing mode, separate player and dealer sales switches, and per-dealer category overrides.
/// Precedence for a dealer: the dealer's category override, then the global category rule, then the default mode
/// (<see cref="ForScope"/>).</para>
/// </summary>
public sealed class PolicyConfiguration
{
    /// <summary>Schema written by this build. Schema 1 (Phases 01-04) is read unchanged.</summary>
    public const int CurrentSchemaVersion = 2;

    public static readonly PolicyConfiguration Default = new(new State
    {
        Enabled = true,
        DefaultMode = PolicyMode.AnyUnlocked,
        Categories = new Dictionary<CategoryKey, CategoryPolicy>(),
        Mixing = MixingMode.AnyPermitted,
        PlayerSales = true,
        DealerSales = true,
        Dealers = new Dictionary<string, Dictionary<CategoryKey, CategoryPolicy>>(StringComparer.Ordinal),
    });

    private readonly State _s;
    private readonly CategoryPolicy _defaultPolicy;

    private PolicyConfiguration(State state)
    {
        _s = state;
        _defaultPolicy = new CategoryPolicy(state.DefaultMode, null);
    }

    /// <summary>Global switch. When false, every integration falls back to vanilla behaviour.</summary>
    public bool Enabled => _s.Enabled;

    public PolicyMode DefaultMode => _s.DefaultMode;

    /// <summary>How one order may combine products (Phase 05).</summary>
    public MixingMode Mixing => _s.Mixing;

    /// <summary>
    /// Phase 03 switch, kept for compatibility: true when any permitted products may be combined
    /// (<see cref="MixingMode.AnyPermitted"/>).
    /// </summary>
    public bool MixedDeliveries => _s.Mixing == MixingMode.AnyPermitted;

    /// <summary>Whether customer contracts you deliver yourself use the mod's rules (Phase 05). Default true.</summary>
    public bool PlayerSalesEnabled => _s.PlayerSales;

    /// <summary>Whether hired dealers use the mod's rules (Phase 05). Default true.</summary>
    public bool DealerSalesEnabled => _s.DealerSales;

    public long Revision => _s.Revision;

    /// <summary>Explicit per-category rules, keyed by category.</summary>
    public IReadOnlyDictionary<CategoryKey, CategoryPolicy> CategoryOverrides => _s.Categories;

    /// <summary>Per-dealer category overrides, keyed by the dealer's NPC id (sorted on output).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<CategoryKey, CategoryPolicy>> DealerOverrides
        => _s.Dealers.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<CategoryKey, CategoryPolicy>)p.Value, StringComparer.Ordinal);

    /// <summary>The rule that applies to <paramref name="category"/> (explicit rule or the default).</summary>
    public CategoryPolicy GetEffectivePolicy(CategoryKey category)
        => category.IsValid && _s.Categories.TryGetValue(category, out var policy) ? policy : _defaultPolicy;

    /// <summary>The dealer's own override for a category, or null when the dealer follows the global rule.</summary>
    public CategoryPolicy? GetDealerOverride(string? dealerId, CategoryKey category)
        => dealerId is not null && _s.Dealers.TryGetValue(dealerId, out var map) && map.TryGetValue(category, out var rule) ? rule : null;

    /// <summary>Whether the mod and the path's own switch are both on.</summary>
    public bool IsPathEnabled(SalesScope scope) => Enabled && (scope.IsDealer ? DealerSalesEnabled : PlayerSalesEnabled);

    /// <summary>
    /// The configuration a path decides with: for a dealer, the global category rules overlaid with that dealer's
    /// overrides (an override replaces the global rule of its category). Same revision and switches; the result has
    /// no dealer overrides of its own. The player scope returns this instance.
    /// </summary>
    public PolicyConfiguration ForScope(SalesScope scope)
    {
        if (!scope.IsDealer || !_s.Dealers.TryGetValue(scope.DealerId!, out var overrides) || overrides.Count == 0)
        {
            return _s.Dealers.Count == 0 ? this : new PolicyConfiguration(_s.With(dealers: Empty()));
        }

        var merged = new Dictionary<CategoryKey, CategoryPolicy>(_s.Categories);
        foreach (var pair in overrides)
        {
            merged[pair.Key] = pair.Value;
        }

        return new PolicyConfiguration(_s.With(categories: merged, dealers: Empty()));
    }

    public PolicyConfiguration WithEnabled(bool enabled) => Next(_s.With(enabled: enabled));

    public PolicyConfiguration WithMixedDeliveries(bool allowed) => WithMixing(allowed ? MixingMode.AnyPermitted : MixingMode.SingleProduct);

    public PolicyConfiguration WithMixing(MixingMode mixing)
        => Next(_s.With(mixing: Enum.IsDefined(typeof(MixingMode), mixing) ? mixing : throw new ArgumentOutOfRangeException(nameof(mixing))));

    public PolicyConfiguration WithPlayerSales(bool enabled) => Next(_s.With(playerSales: enabled));

    public PolicyConfiguration WithDealerSales(bool enabled) => Next(_s.With(dealerSales: enabled));

    public PolicyConfiguration WithDefaultMode(PolicyMode mode) => Next(_s.With(defaultMode: new CategoryPolicy(mode, null).Mode));

    public PolicyConfiguration WithCategory(CategoryKey category, CategoryPolicy policy)
    {
        if (!category.IsValid)
        {
            throw new ArgumentException("Category key is invalid.", nameof(category));
        }

        var copy = new Dictionary<CategoryKey, CategoryPolicy>(_s.Categories) { [category] = policy ?? throw new ArgumentNullException(nameof(policy)) };
        return Next(_s.With(categories: copy));
    }

    public PolicyConfiguration WithoutCategory(CategoryKey category)
    {
        var copy = new Dictionary<CategoryKey, CategoryPolicy>(_s.Categories);
        return copy.Remove(category) ? Next(_s.With(categories: copy)) : this;
    }

    /// <summary>Sets a dealer's override for one category (the dealer then ignores the global rule of that category).</summary>
    public PolicyConfiguration WithDealerCategory(string dealerId, CategoryKey category, CategoryPolicy policy)
    {
        if (!IsValidDealerId(dealerId))
        {
            throw new ArgumentException("Dealer id must be a short non-empty token.", nameof(dealerId));
        }

        if (!category.IsValid)
        {
            throw new ArgumentException("Category key is invalid.", nameof(category));
        }

        var dealers = CopyDealers();
        if (!dealers.TryGetValue(dealerId, out var map))
        {
            map = new Dictionary<CategoryKey, CategoryPolicy>();
            dealers[dealerId] = map;
        }

        map[category] = policy ?? throw new ArgumentNullException(nameof(policy));
        return Next(_s.With(dealers: dealers));
    }

    /// <summary>Removes a dealer's override for one category (back to the global rule).</summary>
    public PolicyConfiguration WithoutDealerCategory(string? dealerId, CategoryKey category)
    {
        if (dealerId is null || !_s.Dealers.TryGetValue(dealerId, out var existing) || !existing.ContainsKey(category))
        {
            return this;
        }

        var dealers = CopyDealers();
        dealers[dealerId].Remove(category);
        if (dealers[dealerId].Count == 0)
        {
            dealers.Remove(dealerId);
        }

        return Next(_s.With(dealers: dealers));
    }

    /// <summary>Removes every override of one dealer.</summary>
    public PolicyConfiguration WithoutDealer(string? dealerId)
    {
        if (dealerId is null || !_s.Dealers.ContainsKey(dealerId))
        {
            return this;
        }

        var dealers = CopyDealers();
        dealers.Remove(dealerId);
        return Next(_s.With(dealers: dealers));
    }

    /// <summary>Removes every dealer override.</summary>
    public PolicyConfiguration WithoutDealerOverrides() => _s.Dealers.Count == 0 ? this : Next(_s.With(dealers: Empty()));

    /// <summary>Dealer ids are the game's NPC ids: short tokens (letters, digits, '-', '_', '.').</summary>
    public static bool IsValidDealerId(string? id)
        => !string.IsNullOrWhiteSpace(id) && id!.Length <= 64 &&
           id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');

    private PolicyConfiguration Next(State state)
    {
        state.Revision = _s.Revision + 1;
        return new PolicyConfiguration(state);
    }

    private Dictionary<string, Dictionary<CategoryKey, CategoryPolicy>> CopyDealers()
        => _s.Dealers.ToDictionary(p => p.Key, p => new Dictionary<CategoryKey, CategoryPolicy>(p.Value), StringComparer.Ordinal);

    private static Dictionary<string, Dictionary<CategoryKey, CategoryPolicy>> Empty() => new(StringComparer.Ordinal);

    private sealed class State
    {
        public bool Enabled;
        public PolicyMode DefaultMode;
        public Dictionary<CategoryKey, CategoryPolicy> Categories = null!;
        public long Revision;
        public MixingMode Mixing;
        public bool PlayerSales;
        public bool DealerSales;
        public Dictionary<string, Dictionary<CategoryKey, CategoryPolicy>> Dealers = null!;

        public State With(
            bool? enabled = null,
            PolicyMode? defaultMode = null,
            Dictionary<CategoryKey, CategoryPolicy>? categories = null,
            MixingMode? mixing = null,
            bool? playerSales = null,
            bool? dealerSales = null,
            Dictionary<string, Dictionary<CategoryKey, CategoryPolicy>>? dealers = null)
            => new()
            {
                Enabled = enabled ?? Enabled,
                DefaultMode = defaultMode ?? DefaultMode,
                Categories = categories ?? Categories,
                Revision = Revision,
                Mixing = mixing ?? Mixing,
                PlayerSales = playerSales ?? PlayerSales,
                DealerSales = dealerSales ?? DealerSales,
                Dealers = dealers ?? Dealers,
            };
    }

    /// <summary>
    /// Converts untrusted settings (loaded from a file) into a valid configuration. Never throws for bad input:
    /// invalid entries are dropped or made more restrictive, and every change is reported in <paramref name="issues"/>.
    /// Schema 1 (Phases 01-04) loads with the same permissions as before: exact-product allowlists, mixing from
    /// <c>mixedDeliveries</c>, both sales paths on, no effect or quality rules and no dealer overrides.
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
            return new PolicyConfiguration(Default._s.With(enabled: settings.Enabled, defaultMode: PolicyMode.Disabled,
                mixing: settings.MixedDeliveries ? MixingMode.AnyPermitted : MixingMode.SingleProduct));
        }

        var defaultMode = PolicyMode.AnyUnlocked;
        if (settings.DefaultMode is not null && !TryParseMode(settings.DefaultMode, out defaultMode))
        {
            defaultMode = PolicyMode.Disabled;
            found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownMode, null, settings.DefaultMode,
                "Unknown default mode; treated as Disabled."));
        }

        var mixing = settings.MixedDeliveries ? MixingMode.AnyPermitted : MixingMode.SingleProduct;
        if (settings.Mixing is not null && !TryParseMixing(settings.Mixing, out mixing))
        {
            mixing = MixingMode.SingleProduct;
            found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownMixing, null, settings.Mixing, "Unknown mixing mode; single product used."));
        }

        var categories = ParseCategories(settings.Categories, defaultMode, null, found);
        var dealers = Empty();
        foreach (var pair in settings.Dealers ?? new Dictionary<string, DealerPolicySettings>())
        {
            if (!IsValidDealerId(pair.Key) || dealers.ContainsKey(pair.Key))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidDealerId, null, pair.Key, "Invalid or duplicate dealer id; its overrides are ignored."));
                continue;
            }

            var map = ParseCategories(pair.Value?.Categories, defaultMode, pair.Key, found);
            if (map.Count > 0)
            {
                dealers.Add(pair.Key, map);
            }
        }

        return new PolicyConfiguration(new State
        {
            Enabled = settings.Enabled,
            DefaultMode = defaultMode,
            Categories = categories,
            Mixing = mixing,
            PlayerSales = settings.PlayerSales,
            DealerSales = settings.DealerSales,
            Dealers = dealers,
        });
    }

    private static Dictionary<CategoryKey, CategoryPolicy> ParseCategories(
        Dictionary<string, CategoryPolicySettings>? input,
        PolicyMode defaultMode,
        string? dealer,
        List<ConfigurationIssue> found)
    {
        var categories = new Dictionary<CategoryKey, CategoryPolicy>();
        var where = dealer is null ? string.Empty : $" (dealer {dealer})";
        foreach (var pair in input ?? new Dictionary<string, CategoryPolicySettings>())
        {
            if (!CategoryKey.TryCreate(pair.Key, out var category))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidCategoryKey, pair.Key, pair.Key, "Invalid category key; entry ignored." + where));
                continue;
            }

            if (categories.ContainsKey(category))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.DuplicateCategory, category.Value, pair.Key,
                    "Category listed more than once (keys differ only by case); later entry ignored." + where));
                continue;
            }

            var entry = pair.Value ?? new CategoryPolicySettings();
            var mode = defaultMode;
            if (entry.Mode is not null && !TryParseMode(entry.Mode, out mode))
            {
                mode = PolicyMode.Disabled;
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownMode, category.Value, entry.Mode, "Unknown mode; category treated as Disabled." + where));
            }

            if (entry.Damaged is { Count: > 0 } damaged)
            {
                mode = PolicyMode.Disabled;
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.DamagedField, category.Value, string.Join(", ", damaged),
                    $"Field(s) {string.Join(", ", damaged)} have the wrong type; category treated as Disabled." + where));
            }

            var match = SelectionMatch.ExactProduct;
            if (entry.Match is not null && !TryParseMatch(entry.Match, out match))
            {
                // Exact matching would block less on a blocklist, so an unreadable match fails closed.
                match = SelectionMatch.ExactProduct;
                mode = PolicyMode.Disabled;
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownMatch, category.Value, entry.Match, "Unknown match; category treated as Disabled." + where));
            }

            QualityTier? minimum = null;
            if (entry.MinQuality is not null)
            {
                if (QualityTiers.TryParse(entry.MinQuality, out var tier))
                {
                    minimum = tier;
                }
                else
                {
                    mode = PolicyMode.Disabled;
                    found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownQuality, category.Value, entry.MinQuality,
                        "Unknown minimum quality; category treated as Disabled." + where));
                }
            }

            var effects = EffectRule.None;
            if (entry.Effects is not null)
            {
                var effectMatch = EffectMatch.All;
                if (entry.Effects.Match is not null && !TryParseEffectMatch(entry.Effects.Match, out effectMatch))
                {
                    effectMatch = EffectMatch.All;
                    found.Add(new ConfigurationIssue(ConfigurationIssueKind.UnknownEffectMatch, category.Value, entry.Effects.Match,
                        "Unknown effect match; \"all\" used." + where));
                }

                var dropped = false;
                effects = new EffectRule(effectMatch, CleanEffects(entry.Effects.Required, category, found, ref dropped), CleanEffects(entry.Effects.Excluded, category, found, ref dropped));
                if (dropped)
                {
                    // A dropped required or excluded effect would loosen the rule: fail closed instead.
                    mode = PolicyMode.Disabled;
                    found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidEffect, category.Value, null, "Effect rule could not be read in full; category treated as Disabled." + where));
                }
            }

            var blockedIssues = found.Count;
            var blocked = CleanProducts(entry.BlockedProducts, category, found, where);
            if (found.Skip(blockedIssues).Any(i => i.Kind == ConfigurationIssueKind.InvalidProductKey))
            {
                // Dropping a blocked product would permit it: fail closed (an invalid allowed entry only narrows).
                mode = PolicyMode.Disabled;
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidProductKey, category.Value, null,
                    "Blocked list could not be read in full; category treated as Disabled." + where));
            }

            categories.Add(category, new CategoryPolicy(
                mode,
                CleanProducts(entry.SelectedProducts, category, found, where),
                match,
                blocked,
                effects,
                minimum));
        }

        return categories;
    }

    private static List<ProductKey> CleanProducts(List<string>? raw, CategoryKey category, List<ConfigurationIssue> found, string where)
    {
        var list = new List<ProductKey>();
        var seen = new HashSet<ProductKey>();
        foreach (var text in raw ?? new List<string>())
        {
            if (!ProductKey.TryCreate(text, out var product))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidProductKey, category.Value, text, "Invalid product key; ignored." + where));
                continue;
            }

            if (!seen.Add(product))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.DuplicateProduct, category.Value, text, "Duplicate product; counted once." + where));
                continue;
            }

            list.Add(product);
        }

        return list;
    }

    private static List<string> CleanEffects(List<string>? raw, CategoryKey category, List<ConfigurationIssue> found, ref bool dropped)
    {
        var list = new List<string>();
        foreach (var text in raw ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 64)
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidEffect, category.Value, text, "Invalid effect id."));
                dropped = true;
                continue;
            }

            if (list.Contains(text.Trim(), StringComparer.Ordinal))
            {
                found.Add(new ConfigurationIssue(ConfigurationIssueKind.InvalidEffect, category.Value, text, "Duplicate effect id; counted once."));
                continue;
            }

            list.Add(text.Trim());
        }

        return list;
    }

    /// <summary>Plain, serializable form of this configuration (always the current schema).</summary>
    public PolicySettings ToSettings() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Enabled = Enabled,
        DefaultMode = FormatMode(DefaultMode),
        MixedDeliveries = MixedDeliveries,
        Mixing = FormatMixing(Mixing),
        PlayerSales = PlayerSalesEnabled,
        DealerSales = DealerSalesEnabled,
        Categories = ToSettings(_s.Categories),
        Dealers = _s.Dealers
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => new DealerPolicySettings { Categories = ToSettings(p.Value) }, StringComparer.Ordinal),
    };

    private static Dictionary<string, CategoryPolicySettings> ToSettings(Dictionary<CategoryKey, CategoryPolicy> map)
        => map.OrderBy(p => p.Key).ToDictionary(p => p.Key.Value, p => ToSettings(p.Value));

    private static CategoryPolicySettings ToSettings(CategoryPolicy rule) => new()
    {
        Mode = FormatMode(rule.Mode),
        Match = rule.Match == SelectionMatch.BaseStrain ? "baseStrain" : "exact",
        SelectedProducts = rule.SelectedProducts.Select(k => k.Value).ToList(),
        BlockedProducts = rule.BlockedProducts.Select(k => k.Value).ToList(),
        Effects = rule.Effects.IsEmpty && rule.Effects.Match == EffectMatch.All
            ? null
            : new EffectRuleSettings
            {
                Match = rule.Effects.Match == EffectMatch.Any ? "any" : "all",
                Required = rule.Effects.Required.ToList(),
                Excluded = rule.Effects.Excluded.ToList(),
            },
        MinQuality = rule.MinimumQuality is { } q ? QualityTiers.Id(q) : null,
    };

    /// <summary>Parses "anyUnlocked" / "selectedOnly" (or "allowlist") / "blocklist" / "disabled" (case-insensitive).</summary>
    public static bool TryParseMode(string? text, out PolicyMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "anyunlocked":
                mode = PolicyMode.AnyUnlocked;
                return true;
            case "selectedonly":
            case "allowlist":
                mode = PolicyMode.SelectedOnly;
                return true;
            case "blocklist":
                mode = PolicyMode.Blocklist;
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
        PolicyMode.Blocklist => "blocklist",
        _ => "disabled",
    };

    public static bool TryParseMixing(string? text, out MixingMode mixing)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "anypermitted":
                mixing = MixingMode.AnyPermitted;
                return true;
            case "singleproduct":
                mixing = MixingMode.SingleProduct;
                return true;
            case "singlebasestrain":
                mixing = MixingMode.SingleBaseStrain;
                return true;
            case "requestedonly":
                mixing = MixingMode.RequestedOnly;
                return true;
            default:
                mixing = MixingMode.SingleProduct;
                return false;
        }
    }

    public static string FormatMixing(MixingMode mixing) => mixing switch
    {
        MixingMode.AnyPermitted => "anyPermitted",
        MixingMode.SingleBaseStrain => "singleBaseStrain",
        MixingMode.RequestedOnly => "requestedOnly",
        _ => "singleProduct",
    };

    private static bool TryParseMatch(string text, out SelectionMatch match)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "exact":
                match = SelectionMatch.ExactProduct;
                return true;
            case "basestrain":
                match = SelectionMatch.BaseStrain;
                return true;
            default:
                match = SelectionMatch.ExactProduct;
                return false;
        }
    }

    private static bool TryParseEffectMatch(string text, out EffectMatch match)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "all":
                match = EffectMatch.All;
                return true;
            case "any":
                match = EffectMatch.Any;
                return true;
            default:
                match = EffectMatch.All;
                return false;
        }
    }
}

/// <summary>Untrusted, serializable settings (shape in docs/PHASE_05_ANALYSIS.md §4). Validate with <see cref="PolicyConfiguration.FromSettings"/>.</summary>
public sealed class PolicySettings
{
    public int SchemaVersion { get; set; } = PolicyConfiguration.CurrentSchemaVersion;

    public bool Enabled { get; set; } = true;

    public string? DefaultMode { get; set; }

    /// <summary>Optional in the file (added in Phase 03); missing means true. Used when <see cref="Mixing"/> is absent.</summary>
    public bool MixedDeliveries { get; set; } = true;

    /// <summary>Phase 05 mixing mode; null in schema 1 files.</summary>
    public string? Mixing { get; set; }

    /// <summary>Phase 05; missing means true.</summary>
    public bool PlayerSales { get; set; } = true;

    /// <summary>Phase 05; missing means true.</summary>
    public bool DealerSales { get; set; } = true;

    public Dictionary<string, CategoryPolicySettings>? Categories { get; set; }

    /// <summary>Phase 05 dealer overrides, keyed by the dealer's NPC id.</summary>
    public Dictionary<string, DealerPolicySettings>? Dealers { get; set; }

    /// <summary>
    /// Phase 06: the save identity the file was written for (optional; files of older builds have none). A file whose
    /// identity differs from the save it is loaded for is treated as unreadable.
    /// </summary>
    public string? Save { get; set; }

    /// <summary>Phase 06: JSON paths of fields this build does not know (reported; not kept when the file is next written).</summary>
    public List<string> UnknownFields { get; } = new();
}

public sealed class CategoryPolicySettings
{
    public string? Mode { get; set; }

    public List<string>? SelectedProducts { get; set; }

    /// <summary>Phase 05: "exact" (default) or "baseStrain".</summary>
    public string? Match { get; set; }

    public List<string>? BlockedProducts { get; set; }

    public EffectRuleSettings? Effects { get; set; }

    /// <summary>Phase 05: null (any) or a quality id such as "standard".</summary>
    public string? MinQuality { get; set; }

    /// <summary>
    /// Phase 06: fields that were present in the file with the wrong JSON type (for example a number in a product
    /// list). A damaged category is made Disabled rather than read with part of its rule missing.
    /// </summary>
    public List<string>? Damaged { get; set; }
}

public sealed class EffectRuleSettings
{
    public string? Match { get; set; }

    public List<string>? Required { get; set; }

    public List<string>? Excluded { get; set; }
}

public sealed class DealerPolicySettings
{
    public Dictionary<string, CategoryPolicySettings>? Categories { get; set; }
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
    UnknownMatch,
    UnknownMixing,
    UnknownQuality,
    UnknownEffectMatch,
    InvalidEffect,
    InvalidDealerId,

    /// <summary>Phase 06: a field had the wrong JSON type; the category is Disabled.</summary>
    DamagedField,
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
