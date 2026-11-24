using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Policies;

/// <summary>Named starting points for the rules (Phase 05). They only ever change a draft.</summary>
public enum PolicyPreset
{
    /// <summary>Everything on, every category Any Unlocked, no effect/quality rules, any mix, no dealer overrides.</summary>
    AnythingGoes,

    /// <summary>Everything on, every category on its allowlist; other rule details are kept.</summary>
    SelectedProductsOnly,

    /// <summary>Player and dealer sales off (the mod stays on), every rule kept for later.</summary>
    VanillaPreferences,
}

/// <summary>Applies, detects and previews <see cref="PolicyPreset"/>s. Pure functions over configurations.</summary>
public static class PolicyPresets
{
    public static readonly IReadOnlyList<PolicyPreset> All = new[] { PolicyPreset.AnythingGoes, PolicyPreset.SelectedProductsOnly, PolicyPreset.VanillaPreferences };

    public const string CustomLabel = "Custom";

    public static string Label(PolicyPreset preset) => preset switch
    {
        PolicyPreset.AnythingGoes => "Anything Goes",
        PolicyPreset.SelectedProductsOnly => "Selected Products Only",
        _ => "Vanilla Preferences",
    };

    public static string Description(PolicyPreset preset) => preset switch
    {
        PolicyPreset.AnythingGoes =>
            "Any discovered product of the requested category can fill an order, for you and your dealers. Clears effect and quality rules and dealer overrides; your product lists are kept.",
        PolicyPreset.SelectedProductsOnly =>
            "Only the products on each category's allowed list can be used. Lists, lineage matching, effect and quality rules, mixing and dealer overrides are kept.",
        _ =>
            "Customers and dealers behave as in the unmodded game. Your rules are kept, so you can switch back later.",
    };

    public static PolicyConfiguration Apply(PolicyConfiguration current, PolicyPreset preset)
    {
        current ??= PolicyConfiguration.Default;
        switch (preset)
        {
            case PolicyPreset.AnythingGoes:
            {
                var next = current.WithEnabled(true).WithPlayerSales(true).WithDealerSales(true)
                    .WithDefaultMode(PolicyMode.AnyUnlocked).WithMixing(MixingMode.AnyPermitted).WithoutDealerOverrides();
                foreach (var pair in current.CategoryOverrides)
                {
                    var rule = pair.Value.WithMode(PolicyMode.AnyUnlocked).WithEffects(EffectRule.None).WithMinimumQuality(null);
                    next = rule.SelectedProducts.Count == 0 && rule.BlockedProducts.Count == 0 && rule.Match == SelectionMatch.ExactProduct
                        ? next.WithoutCategory(pair.Key)
                        : next.WithCategory(pair.Key, rule);
                }

                return next;
            }

            case PolicyPreset.SelectedProductsOnly:
            {
                var next = current.WithEnabled(true).WithPlayerSales(true).WithDealerSales(true).WithDefaultMode(PolicyMode.SelectedOnly);
                foreach (var pair in current.CategoryOverrides)
                {
                    next = next.WithCategory(pair.Key, pair.Value.WithMode(PolicyMode.SelectedOnly));
                }

                return next;
            }

            default:
                return current.WithEnabled(true).WithPlayerSales(false).WithDealerSales(false);
        }
    }

    /// <summary>The preset this configuration matches exactly, or null ("Custom").</summary>
    public static PolicyPreset? Detect(PolicyConfiguration configuration)
    {
        if (configuration is null || !configuration.Enabled)
        {
            return null;
        }

        if (!configuration.PlayerSalesEnabled && !configuration.DealerSalesEnabled)
        {
            return PolicyPreset.VanillaPreferences;
        }

        if (!configuration.PlayerSalesEnabled || !configuration.DealerSalesEnabled)
        {
            return null;
        }

        if (configuration.DefaultMode == PolicyMode.AnyUnlocked && configuration.Mixing == MixingMode.AnyPermitted &&
            configuration.DealerOverrides.Count == 0 &&
            configuration.CategoryOverrides.Values.All(r => r.Mode == PolicyMode.AnyUnlocked && r.Effects.IsEmpty && r.MinimumQuality is null))
        {
            return PolicyPreset.AnythingGoes;
        }

        if (configuration.DefaultMode == PolicyMode.SelectedOnly && configuration.CategoryOverrides.Values.All(r => r.Mode == PolicyMode.SelectedOnly))
        {
            return PolicyPreset.SelectedProductsOnly;
        }

        return null;
    }

    public static string DetectLabel(PolicyConfiguration configuration)
        => Detect(configuration) is { } preset ? Label(preset) : CustomLabel;

    /// <summary>
    /// What applying the preset would change in the draft, one line each, plus warnings (for example a category that
    /// would permit nothing). Empty when nothing changes.
    /// </summary>
    public static IReadOnlyList<string> Preview(PolicyConfiguration current, PolicyPreset preset, ProductCatalogSnapshot? snapshot)
    {
        current ??= PolicyConfiguration.Default;
        var next = Apply(current, preset);
        var lines = new List<string>();
        void Switch(string name, bool before, bool after)
        {
            if (before != after)
            {
                lines.Add($"{name}: {(before ? "on" : "off")} -> {(after ? "on" : "off")}");
            }
        }

        Switch("Mod", current.Enabled, next.Enabled);
        Switch("Player sales", current.PlayerSalesEnabled, next.PlayerSalesEnabled);
        Switch("Dealer sales", current.DealerSalesEnabled, next.DealerSalesEnabled);
        if (current.Mixing != next.Mixing)
        {
            lines.Add($"Mixing: {MixingLabel(current.Mixing)} -> {MixingLabel(next.Mixing)}");
        }

        var categories = snapshot?.Categories.Select(c => (c.Key, Name: c.DisplayName)).ToList()
                         ?? current.CategoryOverrides.Keys.Select(k => (Key: k, Name: k.Value)).ToList();
        foreach (var (key, name) in categories)
        {
            var before = current.GetEffectivePolicy(key);
            var after = next.GetEffectivePolicy(key);
            if (before.Mode != after.Mode)
            {
                lines.Add($"{name}: {ModeName(before.Mode)} -> {ModeName(after.Mode)}");
            }

            if (!before.Effects.IsEmpty && after.Effects.IsEmpty)
            {
                lines.Add($"{name}: effect rules cleared");
            }

            if (before.MinimumQuality is not null && after.MinimumQuality is null)
            {
                lines.Add($"{name}: quality minimum cleared");
            }

            if (next.PlayerSalesEnabled && after.Mode == PolicyMode.SelectedOnly && after.SelectedProducts.Count == 0)
            {
                lines.Add($"Warning: {name} has nothing on its allowed list, so no {name} order can be filled until you add products.");
            }
        }

        if (current.DealerOverrides.Count > 0 && next.DealerOverrides.Count == 0)
        {
            lines.Add($"Dealer overrides removed ({current.DealerOverrides.Count} dealer(s))");
        }

        return lines;
    }

    private static string ModeName(PolicyMode mode) => mode switch
    {
        PolicyMode.AnyUnlocked => "Any Unlocked",
        PolicyMode.SelectedOnly => "Selected Only",
        PolicyMode.Blocklist => "Blocklist",
        _ => "Disabled",
    };

    public static string MixingLabel(MixingMode mixing) => mixing switch
    {
        MixingMode.AnyPermitted => "any permitted products",
        MixingMode.SingleProduct => "one product per order",
        MixingMode.SingleBaseStrain => "one base strain per order",
        _ => "requested product only",
    };
}
