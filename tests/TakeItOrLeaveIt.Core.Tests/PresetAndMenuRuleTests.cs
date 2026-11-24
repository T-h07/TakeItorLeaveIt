using System.Linq;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 05 suite G: presets (preview, confirm, Custom label, persistence) and the menu's advanced rule actions.</summary>
public class PresetAndMenuRuleTests
{
    private static MenuHarness Menu() => new MenuHarness(Advanced.Discovery).Load();

    [Fact]
    public void P05_G01_defaults_are_anything_goes()
    {
        Assert.Equal(PolicyPreset.AnythingGoes, PolicyPresets.Detect(PolicyConfiguration.Default));
        Assert.Equal("Anything Goes", Menu().Menu.ActivePresetLabel);
    }

    [Fact]
    public void P05_G02_preset_needs_confirmation_and_changes_only_the_draft()
    {
        var h = Menu();

        Assert.True(h.Menu.RequestPreset(PolicyPreset.VanillaPreferences));
        Assert.True(h.Menu.PlayerSalesEnabled); // nothing applied yet
        Assert.Contains("Player sales: on -> off", h.Menu.PresetPreview);
        Assert.Equal(FeedbackKind.Warning, h.Menu.Feedback.Kind);

        Assert.True(h.Menu.ConfirmPreset());
        Assert.False(h.Menu.PlayerSalesEnabled);
        Assert.False(h.Menu.DealerSalesEnabled);
        Assert.True(h.Menu.IsModEnabled);
        Assert.True(h.Provider.Current.PlayerSalesEnabled); // live rules unchanged until Save
        Assert.Equal("Vanilla Preferences", h.Menu.ActivePresetLabel);

        h.Menu.Discard();
        Assert.True(h.Menu.PlayerSalesEnabled);
    }

    [Fact]
    public void P05_G03_cancel_and_edits_drop_a_pending_preset()
    {
        var h = Menu();
        h.Menu.RequestPreset(PolicyPreset.SelectedProductsOnly);
        h.Menu.CancelPreset();
        Assert.Null(h.Menu.PendingPreset);
        Assert.False(h.Menu.ConfirmPreset());

        h.Menu.RequestPreset(PolicyPreset.SelectedProductsOnly);
        h.Menu.SetMixing(MixingMode.SingleProduct);
        Assert.Null(h.Menu.PendingPreset);
        Assert.False(h.Menu.HasUnsavedChanges && h.Menu.Draft.DefaultMode == PolicyMode.SelectedOnly);
    }

    [Fact]
    public void P05_G04_selected_products_only_keeps_lists_and_warns_about_empty_ones()
    {
        var h = Menu();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMode(Weed, PolicyMode.Blocklist);
        h.Menu.ToggleProduct(Weed, MixS); // blocked list
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.ToggleProduct(Weed, OgKush); // allowed list

        h.Menu.RequestPreset(PolicyPreset.SelectedProductsOnly);
        Assert.Contains(h.Menu.PresetPreview, l => l.StartsWith("Warning: Meth", System.StringComparison.Ordinal));
        h.Menu.ConfirmPreset();

        var weed = h.Menu.Draft.GetEffectivePolicy(Weed);
        Assert.Equal(PolicyMode.SelectedOnly, weed.Mode);
        Assert.Equal(new[] { OgKush }, weed.SelectedProducts);
        Assert.Equal(new[] { MixS }, weed.BlockedProducts);
        Assert.Equal(PolicyMode.SelectedOnly, h.Menu.Draft.GetEffectivePolicy(Meth).Mode);
        Assert.Equal("Selected Products Only", h.Menu.ActivePresetLabel);
    }

    [Fact]
    public void P05_G05_editing_after_a_preset_shows_custom()
    {
        var h = Menu();
        h.Menu.RequestPreset(PolicyPreset.AnythingGoes);
        h.Menu.ConfirmPreset();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMinimumQuality(Weed, QualityTier.Premium);

        Assert.Equal(PolicyPresets.CustomLabel, h.Menu.ActivePresetLabel);
    }

    [Fact]
    public void P05_G06_anything_goes_clears_advanced_rules_and_overrides_but_keeps_lists()
    {
        var policy = With(new CategoryPolicy(PolicyMode.SelectedOnly, new[] { OgKush }, SelectionMatch.BaseStrain, new[] { MixS },
                new EffectRule(EffectMatch.All, new[] { Calming }, null), QualityTier.Premium))
            .WithMixing(MixingMode.RequestedOnly)
            .WithDealerSales(false)
            .WithDealerCategory(Dealers.Benji, Weed, CategoryPolicy.Disabled);

        var next = PolicyPresets.Apply(policy, PolicyPreset.AnythingGoes);
        var weed = next.GetEffectivePolicy(Weed);

        Assert.Equal(PolicyPreset.AnythingGoes, PolicyPresets.Detect(next));
        Assert.Equal(PolicyMode.AnyUnlocked, weed.Mode);
        Assert.True(weed.Effects.IsEmpty);
        Assert.Null(weed.MinimumQuality);
        Assert.Equal(new[] { OgKush }, weed.SelectedProducts);
        Assert.Equal(new[] { MixS }, weed.BlockedProducts);
        Assert.Empty(next.DealerOverrides);
        Assert.Equal(MixingMode.AnyPermitted, next.Mixing);
        Assert.True(next.DealerSalesEnabled);
        Assert.Equal(SaleEligibilityPolicy.EvaluateCategory(PolicyConfiguration.Default, Snapshot(), Weed).EligibleProducts.Count,
            SaleEligibilityPolicy.EvaluateCategory(next, Snapshot(), Weed).EligibleProducts.Count);
    }

    [Fact]
    public void P05_G07_saved_preset_survives_reload()
    {
        var store = new MemoryPolicyStore();
        var h = new MenuHarness(Advanced.Discovery, store).Load();
        h.Menu.RequestPreset(PolicyPreset.VanillaPreferences);
        h.Menu.ConfirmPreset();
        Assert.True(h.Menu.Save());

        var again = new MenuHarness(Advanced.Discovery, store).Load();

        Assert.Equal("Vanilla Preferences", again.Menu.ActivePresetLabel);
        Assert.False(again.Provider.Current.PlayerSalesEnabled);
    }

    [Fact]
    public void P05_G08_blocklist_toggles_edit_the_blocked_list_and_keep_advanced_rules_stored()
    {
        var h = Menu();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMode(Weed, PolicyMode.Blocklist);
        h.Menu.SetMatch(Weed, SelectionMatch.BaseStrain);
        h.Menu.ToggleProduct(Weed, OgKush);

        var row = h.Menu.GetProductPage().Rows.Single(r => r.Key == MixA);
        Assert.False(row.IsEligible);
        Assert.Equal("Blocked", row.StatusLabel);
        Assert.Equal("from ogkush", row.Lineage);

        // Back to the default mode with an empty allowlist: the blocklist and match must not be thrown away.
        h.Menu.SetMode(Weed, PolicyMode.AnyUnlocked);
        Assert.Equal(new[] { OgKush }, h.Menu.Draft.GetEffectivePolicy(Weed).BlockedProducts);
        Assert.Equal(SelectionMatch.BaseStrain, h.Menu.Draft.GetEffectivePolicy(Weed).Match);
    }

    [Fact]
    public void P05_G09_rules_screen_actions()
    {
        var h = Menu();
        h.Menu.SelectCategory(Weed);
        h.Menu.Navigate(MenuScreen.Rules);
        h.Menu.SelectCategory(Weed);
        Assert.Equal(MenuScreen.Rules, h.Menu.Screen); // picking a category keeps the Rules screen

        h.Menu.CycleEffect(Weed, Calming); // required
        h.Menu.CycleEffect(Weed, Paranoia);
        h.Menu.CycleEffect(Weed, Paranoia); // excluded
        h.Menu.SetMinimumQuality(Weed, QualityTier.Standard);

        var rule = h.Menu.GetSelectedRule()!;
        Assert.Equal(new[] { Calming }, rule.Effects.Required);
        Assert.Equal(new[] { Paranoia }, rule.Effects.Excluded);
        Assert.Equal(QualityTier.Standard, rule.MinimumQuality);
        Assert.Equal(new[] { Calming, Energizing, Paranoia }, h.Menu.GetEffectRows().Select(e => e.Id));
        Assert.Equal(EffectState.Excluded, h.Menu.GetEffectRows().Single(e => e.Id == Paranoia).State);
        Assert.Equal("Standard+ - 2 effect rules", h.Menu.GetSelectedCategorySummary()!.RuleDetail);

        h.Menu.ClearEffects(Weed);
        h.Menu.SetMinimumQuality(Weed, null);
        Assert.False(h.Menu.HasUnsavedChanges);
    }

    [Fact]
    public void P05_G10_contradictory_effects_are_flagged()
    {
        var store = new MemoryPolicyStore();
        store.Save("save-a", With(new CategoryPolicy(PolicyMode.AnyUnlocked, null, SelectionMatch.ExactProduct, null,
            new EffectRule(EffectMatch.All, new[] { Calming }, new[] { Calming }), null)).ToSettings());
        var h = new MenuHarness(Advanced.Discovery, store).Load();
        h.Menu.SelectCategory(Weed);

        var summary = h.Menu.GetSelectedCategorySummary()!;
        Assert.Equal(EligibilityReason.EffectRuleContradictory, summary.Reason);
        Assert.Contains("never be met", summary.Warning);
        Assert.True(h.Menu.GetEffectRows().Single(e => e.Id == Calming).IsConflicting);
    }

    [Fact]
    public void P05_G11_dealer_scope_edits_only_that_dealers_overrides()
    {
        var h = Menu();
        h.Menu.Dealers = () => new[] { new DealerInfo(Dealers.Benji, "Benji", true), new DealerInfo(Dealers.Molly, "Molly", false) };
        h.Menu.SelectCategory(Weed);

        Assert.True(h.Menu.EditDealer(Dealers.Benji));
        h.Menu.SetMode(Weed, PolicyMode.Disabled);

        Assert.Equal(PolicyMode.AnyUnlocked, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(PolicyMode.Disabled, h.Menu.Draft.GetDealerOverride(Dealers.Benji, Weed)!.Mode);
        Assert.True(h.Menu.GetSelectedCategorySummary()!.IsDealerOverride);
        Assert.Equal("Overrides for Benji", h.Menu.ScopeLabel);
        var dealers = h.Menu.GetDealers();
        Assert.Equal("1 category override", dealers.Single(d => d.Id == Dealers.Benji).Status);
        Assert.Contains("not hired", dealers.Single(d => d.Id == Dealers.Molly).Status);

        Assert.True(h.Menu.UseGlobalRule(Weed));
        Assert.Empty(h.Menu.Draft.DealerOverrides);
        h.Menu.EditDealer(null);
        Assert.Equal("Global rules", h.Menu.ScopeLabel);
    }

    [Fact]
    public void P05_G12_overrides_for_an_unseen_dealer_are_listed_as_inactive_and_removable()
    {
        var h = Menu();
        h.Menu.EditDealer("ghost_dealer");
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMode(Weed, PolicyMode.Disabled);
        h.Menu.EditDealer(null);

        var ghost = h.Menu.GetDealers().Single();
        Assert.False(ghost.Known);
        Assert.Contains("inactive", ghost.Status);
        Assert.True(h.Menu.RemoveDealerOverrides("ghost_dealer"));
        Assert.Empty(h.Menu.GetDealers());
    }

    [Fact]
    public void P05_G13_switch_and_mixing_edits_save_and_reach_the_live_rules()
    {
        var h = Menu();
        h.Menu.SetDealerSales(false);
        h.Menu.SetMixing(MixingMode.SingleBaseStrain);
        Assert.True(h.Menu.Save());

        Assert.False(h.Provider.Current.DealerSalesEnabled);
        Assert.Equal(MixingMode.SingleBaseStrain, h.Provider.Current.Mixing);
        Assert.Equal(PolicyConfiguration.CurrentSchemaVersion, PolicySettingsJson.FromJson(h.Store.Files["save-a"]).SchemaVersion);
    }
}
