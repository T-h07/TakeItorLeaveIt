using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group G: configuration.</summary>
public class PolicyConfigurationTests
{
    [Fact]
    public void Defaults_are_enabled_and_AnyUnlocked_for_every_category()
    {
        var config = PolicyConfiguration.Default;

        Assert.True(config.Enabled);
        Assert.Equal(PolicyMode.AnyUnlocked, config.DefaultMode);
        Assert.Equal(PolicyMode.AnyUnlocked, config.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(PolicyMode.AnyUnlocked, config.GetEffectivePolicy(CategoryKey.Create("future-category")).Mode);
        Assert.Empty(config.CategoryOverrides);
    }

    [Fact]
    public void Missing_settings_fall_back_to_defaults_with_an_issue()
    {
        var config = PolicyConfiguration.FromSettings(null, out var issues);

        Assert.Same(PolicyConfiguration.Default, config);
        Assert.Equal(ConfigurationIssueKind.MissingSettings, Assert.Single(issues).Kind);
    }

    [Fact]
    public void Valid_settings_are_loaded()
    {
        var settings = new PolicySettings
        {
            Categories = new Dictionary<string, CategoryPolicySettings>
            {
                ["Marijuana"] = new() { Mode = "selectedOnly", SelectedProducts = new List<string> { "sourdiesel", "ogkush" } },
                ["methamphetamine"] = new() { Mode = "DISABLED" },
            },
        };

        var config = PolicyConfiguration.FromSettings(settings, out var issues);

        Assert.Empty(issues);
        Assert.Equal(PolicyMode.SelectedOnly, config.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(new[] { OgKush, SourDiesel }, config.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(PolicyMode.Disabled, config.GetEffectivePolicy(Meth).Mode);
        Assert.Equal(PolicyMode.AnyUnlocked, config.GetEffectivePolicy(Cocaine).Mode);
    }

    [Fact]
    public void Unknown_modes_fail_safe_to_disabled()
    {
        var settings = new PolicySettings
        {
            Categories = new Dictionary<string, CategoryPolicySettings> { ["marijuana"] = new() { Mode = "everything" } },
        };

        var config = PolicyConfiguration.FromSettings(settings, out var issues);

        Assert.Equal(PolicyMode.Disabled, config.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(ConfigurationIssueKind.UnknownMode, Assert.Single(issues).Kind);
    }

    [Fact]
    public void Unknown_default_mode_fails_safe_to_disabled()
    {
        var config = PolicyConfiguration.FromSettings(new PolicySettings { DefaultMode = "??" }, out var issues);

        Assert.Equal(PolicyMode.Disabled, config.DefaultMode);
        Assert.Equal(PolicyMode.Disabled, config.GetEffectivePolicy(Weed).Mode);
        Assert.Contains(issues, i => i.Kind == ConfigurationIssueKind.UnknownMode);
    }

    [Fact]
    public void Invalid_and_duplicate_entries_are_dropped_and_reported()
    {
        var settings = new PolicySettings
        {
            Categories = new Dictionary<string, CategoryPolicySettings>
            {
                [" "] = new() { Mode = "disabled" },
                ["marijuana"] = new() { Mode = "selectedOnly", SelectedProducts = new List<string> { "ogkush", "ogkush", "", "bad\u0002id" } },
                ["MARIJUANA"] = new() { Mode = "disabled" },
            },
        };

        var config = PolicyConfiguration.FromSettings(settings, out var issues);

        Assert.Equal(new[] { OgKush }, config.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(PolicyMode.SelectedOnly, config.GetEffectivePolicy(Weed).Mode);
        Assert.Contains(issues, i => i.Kind == ConfigurationIssueKind.InvalidCategoryKey);
        Assert.Contains(issues, i => i.Kind == ConfigurationIssueKind.DuplicateCategory);
        Assert.Contains(issues, i => i.Kind == ConfigurationIssueKind.DuplicateProduct);
        Assert.Equal(2, issues.Count(i => i.Kind == ConfigurationIssueKind.InvalidProductKey));
    }

    [Fact]
    public void Newer_schema_disables_everything()
    {
        var settings = new PolicySettings { SchemaVersion = PolicyConfiguration.CurrentSchemaVersion + 1, DefaultMode = "anyUnlocked" };

        var config = PolicyConfiguration.FromSettings(settings, out var issues);

        Assert.Equal(PolicyMode.Disabled, config.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(ConfigurationIssueKind.UnsupportedSchemaVersion, Assert.Single(issues).Kind);
    }

    [Fact]
    public void Global_disable_is_loaded()
    {
        var config = PolicyConfiguration.FromSettings(new PolicySettings { Enabled = false }, out _);

        Assert.False(config.Enabled);
        Assert.Equal(EligibilityReason.ModDisabled, SaleEligibilityPolicy.Evaluate(config, Snapshot(Standard()), Weed, OgKush).Reason);
    }

    [Fact]
    public void Stale_selected_ids_are_kept_but_inactive()
    {
        var settings = new PolicySettings
        {
            Categories = new Dictionary<string, CategoryPolicySettings>
            {
                ["marijuana"] = new() { Mode = "selectedOnly", SelectedProducts = new List<string> { "removed-in-update", "ogkush" } },
            },
        };
        var config = PolicyConfiguration.FromSettings(settings, out var issues);

        var summary = SaleEligibilityPolicy.EvaluateCategory(config, Snapshot(Standard()), Weed);

        Assert.Empty(issues); // well-formed id: kept for when it comes back, never silently deleted
        Assert.Equal(new[] { OgKush }, summary.EligibleProducts.Keys());
        Assert.Equal(new[] { ProductKey.Create("removed-in-update") }, summary.InactiveSelections);
        Assert.Equal(2, summary.SelectedCount);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var config = PolicyConfiguration.Default
            .WithDefaultMode(PolicyMode.SelectedOnly)
            .WithCategory(Weed, CategoryPolicy.SelectedOnly(SourDiesel, OgKush))
            .WithCategory(Meth, CategoryPolicy.Disabled);

        var restored = PolicyConfiguration.FromSettings(config.ToSettings(), out var issues);

        Assert.Empty(issues);
        Assert.Equal(config.Enabled, restored.Enabled);
        Assert.Equal(config.DefaultMode, restored.DefaultMode);
        Assert.Equal(config.GetEffectivePolicy(Weed).SelectedProducts, restored.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(PolicyMode.Disabled, restored.GetEffectivePolicy(Meth).Mode);
    }

    [Fact]
    public void Changes_produce_new_revisions_and_leave_originals_untouched()
    {
        var original = PolicyConfiguration.Default;
        var changed = original.WithCategory(Weed, CategoryPolicy.Disabled);

        Assert.True(changed.Revision > original.Revision);
        Assert.Equal(PolicyMode.AnyUnlocked, original.GetEffectivePolicy(Weed).Mode);
        Assert.Same(changed, changed.WithoutCategory(Cocaine));
        Assert.Equal(PolicyMode.AnyUnlocked, changed.WithoutCategory(Weed).GetEffectivePolicy(Weed).Mode);
    }

    [Theory]
    [InlineData("anyUnlocked", PolicyMode.AnyUnlocked)]
    [InlineData(" SELECTEDONLY ", PolicyMode.SelectedOnly)]
    [InlineData("disabled", PolicyMode.Disabled)]
    public void Modes_parse_case_insensitively(string text, PolicyMode expected)
    {
        Assert.True(PolicyConfiguration.TryParseMode(text, out var mode));
        Assert.Equal(expected, mode);
        Assert.True(PolicyConfiguration.TryParseMode(PolicyConfiguration.FormatMode(expected), out _));
    }

    [Fact]
    public void Invalid_policy_values_are_rejected_in_code()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategoryPolicy((PolicyMode)42, null));
        Assert.Throws<ArgumentException>(() => new CategoryPolicy(PolicyMode.SelectedOnly, new[] { default(ProductKey) }));
        Assert.Throws<ArgumentException>(() => PolicyConfiguration.Default.WithCategory(default, CategoryPolicy.Disabled));
    }

    [Fact]
    public void Provider_publishes_updates()
    {
        var provider = new InMemoryPolicyProvider();
        PolicyConfiguration? seen = null;
        provider.Changed += c => seen = c;

        var next = provider.Update(c => c.WithCategory(Weed, CategoryPolicy.Disabled));

        Assert.Same(next, provider.Current);
        Assert.Same(next, seen);
        Assert.Throws<InvalidOperationException>(() => provider.Update(_ => null!));
        Assert.Same(next, provider.Current);
    }
}
