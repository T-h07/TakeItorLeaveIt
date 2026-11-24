using System.Linq;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 05 suite E: schema 2 settings, migration from schema 1, backups and fail-safe parsing.</summary>
public class SettingsMigrationTests
{
    private const string Schema1 = "{\"schemaVersion\":1,\"enabled\":true,\"defaultMode\":\"anyUnlocked\",\"mixedDeliveries\":false," +
                                   "\"categories\":{\"marijuana\":{\"mode\":\"selectedOnly\",\"selectedProducts\":[\"ogkush\",\"zz_alpha\"]}}}";

    private static PolicyConfiguration Load(string json, out System.Collections.Generic.IReadOnlyList<ConfigurationIssue> issues)
        => PolicyConfiguration.FromSettings(PolicySettingsJson.FromJson(json), out issues);

    [Fact]
    public void P05_E01_schema_1_selections_migrate_as_exact_recipes()
    {
        var policy = Load(Schema1, out var issues);
        var weed = policy.GetEffectivePolicy(Weed);

        Assert.Empty(issues);
        Assert.Equal(PolicyMode.SelectedOnly, weed.Mode);
        Assert.Equal(SelectionMatch.ExactProduct, weed.Match); // never re-read as base strains
        Assert.Equal(new[] { OgKush, MixA }.OrderBy(k => k), weed.SelectedProducts);
        Assert.Equal(MixingMode.SingleProduct, policy.Mixing);
        Assert.True(policy.PlayerSalesEnabled);
        Assert.True(policy.DealerSalesEnabled);
        Assert.Empty(policy.DealerOverrides);
        Assert.False(weed.HasAdvancedRules);

        // Behaviour is unchanged: the mix's descendants are not pulled in.
        Assert.Equal(EligibilityReason.ProductNotSelected, Reason(policy, MixB));
    }

    [Fact]
    public void P05_E02_schema_2_round_trips_every_field()
    {
        var weed = new CategoryPolicy(PolicyMode.Blocklist, new[] { SourDiesel }, SelectionMatch.BaseStrain, new[] { OgKush },
            new EffectRule(EffectMatch.Any, new[] { Calming, Energizing }, new[] { Paranoia }), QualityTier.Premium);
        var policy = PolicyConfiguration.Default
            .WithCategory(Weed, weed)
            .WithCategory(Meth, CategoryPolicy.Disabled)
            .WithMixing(MixingMode.SingleBaseStrain)
            .WithPlayerSales(false)
            .WithDealerCategory(Dealers.Benji, Weed, CategoryPolicy.SelectedOnly(Gdp).WithMinimumQuality(QualityTier.Standard));

        var json = PolicySettingsJson.ToJson(policy.ToSettings());
        var back = Load(json, out var issues);

        Assert.Empty(issues);
        Assert.Contains("\"schemaVersion\":2", json.Replace(" ", string.Empty));
        Assert.True(back.GetEffectivePolicy(Weed).SameAs(weed));
        Assert.Equal(PolicyMode.Disabled, back.GetEffectivePolicy(Meth).Mode);
        Assert.Equal(MixingMode.SingleBaseStrain, back.Mixing);
        Assert.False(back.PlayerSalesEnabled);
        Assert.True(back.DealerSalesEnabled);
        Assert.True(back.GetDealerOverride(Dealers.Benji, Weed)!.SameAs(policy.GetDealerOverride(Dealers.Benji, Weed)));
        Assert.Equal(PolicySettingsJson.ToJson(back.ToSettings()), json);
    }

    [Fact]
    public void P05_E03_first_save_over_schema_1_keeps_a_one_time_backup()
    {
        var fs = new MemoryFileSystem();
        var store = new JsonPolicyStore("root", fs);
        var path = store.PathFor("save-a");
        fs.Files[path] = Schema1;

        var loaded = Load(store.Load("save-a").Settings is { } s ? PolicySettingsJson.ToJson(s) : Schema1, out _);
        store.Save("save-a", loaded.ToSettings());
        store.Save("save-a", loaded.WithMixing(MixingMode.AnyPermitted).ToSettings());

        Assert.Equal(Schema1, fs.Files[path + ".schema1.bak"]);
        Assert.Contains("\"schemaVersion\":2", fs.Files[path].Replace(" ", string.Empty));
        Assert.Single(fs.Files.Keys, k => k.EndsWith(".schema1.bak", System.StringComparison.Ordinal));
        Assert.Contains("\"schemaVersion\":2", fs.Files[path + ".bak"].Replace(" ", string.Empty)); // Phase 06 known-good copy
    }

    [Fact]
    public void P05_E04_newer_schema_disables_every_category()
    {
        var policy = Load("{\"schemaVersion\":3,\"enabled\":true,\"categories\":{}}", out var issues);

        Assert.Contains(issues, i => i.Kind == ConfigurationIssueKind.UnsupportedSchemaVersion);
        Assert.Empty(Eligible(policy));
    }

    [Theory]
    [InlineData("\"minQuality\":\"shiny\"", ConfigurationIssueKind.UnknownQuality)]
    [InlineData("\"match\":\"fuzzy\"", ConfigurationIssueKind.UnknownMatch)]
    [InlineData("\"effects\":{\"match\":\"all\",\"required\":[],\"excluded\":[\"\"]}", ConfigurationIssueKind.InvalidEffect)]
    public void P05_E05_unknown_rule_values_disable_the_category(string field, ConfigurationIssueKind kind)
    {
        var policy = Load("{\"schemaVersion\":2,\"categories\":{\"marijuana\":{\"mode\":\"anyUnlocked\",\"selectedProducts\":[]," + field + "}}}", out var issues);

        Assert.Contains(issues, i => i.Kind == kind);
        Assert.Equal(PolicyMode.Disabled, policy.GetEffectivePolicy(Weed).Mode);
    }

    [Fact]
    public void P05_E06_invalid_dealer_ids_are_dropped_with_an_issue()
    {
        var policy = Load("{\"schemaVersion\":2,\"dealers\":{\"has space\":{\"categories\":{\"marijuana\":{\"mode\":\"disabled\"}}}}}", out var issues);

        Assert.Contains(issues, i => i.Kind == ConfigurationIssueKind.InvalidDealerId);
        Assert.Empty(policy.DealerOverrides);
    }

    [Fact]
    public void P05_E07_missing_switches_default_on()
    {
        var policy = Load("{\"schemaVersion\":2}", out _);

        Assert.True(policy.PlayerSalesEnabled);
        Assert.True(policy.DealerSalesEnabled);
        Assert.Equal(MixingMode.AnyPermitted, policy.Mixing);
    }
}
