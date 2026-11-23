using System;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 02 groups A (navigation), B (selection), C (modes), E (preview), G (lifecycle).</summary>
public class ModMenuControllerTests
{
    // ---------------------------------------------------------------- A: navigation

    [Fact]
    public void Opens_on_overview_and_waits_for_a_save()
    {
        var h = new MenuHarness();

        Assert.Equal(MenuScreen.Overview, h.Menu.Screen);
        Assert.Equal(MenuAvailability.WaitingForSave, h.Menu.Availability);
        Assert.Equal(MenuText.WaitingForSave, h.Menu.AvailabilityText);
        Assert.False(h.Menu.CanEdit);
        Assert.Empty(h.Menu.GetCategories());
    }

    [Fact]
    public void Categories_come_from_the_catalog_with_friendly_names()
    {
        var h = new MenuHarness().Load();

        var categories = h.Menu.GetCategories();

        Assert.Equal(new[] { "Cocaine", "Weed", "Meth" }, categories.Select(c => c.DisplayName));
        Assert.All(categories, c => Assert.Equal("Any Unlocked", c.ModeLabel));
    }

    [Fact]
    public void Selecting_a_category_opens_products_and_unknown_categories_are_refused()
    {
        var h = new MenuHarness().Load();

        Assert.True(h.Menu.SelectCategory(Weed));
        Assert.Equal(MenuScreen.Products, h.Menu.Screen);
        Assert.False(h.Menu.SelectCategory(CategoryKey.Create("heroin")));
        Assert.Equal(Weed, h.Menu.SelectedCategory);
    }

    [Fact]
    public void Navigation_keeps_the_draft()
    {
        var h = new MenuHarness().Load();
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Weed, OgKush, true);

        h.Menu.Navigate(MenuScreen.Settings);
        h.Menu.Navigate(MenuScreen.Diagnostics);
        h.Menu.SelectCategory(Meth);
        h.Menu.SelectCategory(Weed);

        Assert.True(h.Row(OgKush).IsSelected);
        Assert.True(h.Menu.HasUnsavedChanges);
    }

    [Fact]
    public void View_version_changes_only_on_visible_changes()
    {
        var h = new MenuHarness().Load();
        var before = h.Menu.ViewVersion;

        h.Menu.Navigate(MenuScreen.Overview); // already there
        h.Menu.SetSearch("  ");               // same (empty) search
        Assert.Equal(before, h.Menu.ViewVersion);

        h.Menu.SetSearch("kush");
        Assert.True(h.Menu.ViewVersion > before);
    }

    // ---------------------------------------------------------------- B: selection

    [Fact]
    public void Only_unlocked_products_are_selectable()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        Assert.False(h.Row(Gdp).IsSelectable);
        Assert.True(h.Row(OgKush).IsSelectable);
        Assert.False(h.Menu.SetProductSelected(Weed, Gdp, true));
        Assert.False(h.Menu.Draft.GetEffectivePolicy(Weed).IsSelected(Gdp));
        Assert.Contains("Locked", h.Row(Gdp).Detail);
    }

    [Fact]
    public void Products_of_another_category_cannot_be_selected()
    {
        var h = new MenuHarness().Load();

        Assert.False(h.Menu.SetProductSelected(Weed, Meth1, true));
        Assert.False(h.Menu.SetProductSelected(Weed, ProductKey.Create("not-in-catalog"), true));
    }

    [Fact]
    public void Multiple_products_can_be_selected_and_toggled()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        h.Menu.ToggleProduct(Weed, OgKush);
        h.Menu.ToggleProduct(Weed, SourDiesel);
        Assert.Equal(new[] { OgKush, SourDiesel }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);

        h.Menu.ToggleProduct(Weed, OgKush);
        Assert.Equal(new[] { SourDiesel }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void Category_selections_are_independent_and_clear_affects_one_category()
    {
        var h = new MenuHarness().Load();
        h.Menu.SetProductSelected(Weed, OgKush, true);
        h.Menu.SetProductSelected(Meth, Meth1, true);

        h.Menu.ClearSelection(Weed);

        Assert.Empty(h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(new[] { Meth1 }, h.Menu.Draft.GetEffectivePolicy(Meth).SelectedProducts);
    }

    [Fact]
    public void Select_all_unlocked_covers_the_category_but_never_locked_products()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetSearch("og");

        h.Menu.SelectAllUnlocked(Weed);

        Assert.Equal(new[] { OgKush, SourDiesel }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void Select_all_visible_only_takes_search_matches()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetSearch("sour");

        h.Menu.SelectAllVisible();

        Assert.Equal(new[] { SourDiesel }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
    }

    [Fact]
    public void Same_name_products_stay_distinct_and_show_their_ids()
    {
        var a = ProductKey.Create("mix-1");
        var b = ProductKey.Create("mix-2");
        var h = new MenuHarness(() => Result(Product(a, Weed, name: "Purple Haze", effects: "calming"), Product(b, Weed, name: "Purple Haze"))).Load();
        h.Menu.SelectCategory(Weed);

        h.Menu.SetProductSelected(Weed, a, true);

        Assert.True(h.Row(a).IsSelected);
        Assert.False(h.Row(b).IsSelected);
        Assert.Contains("ID mix-1", h.Row(a).Detail);
        Assert.Contains("ID mix-2", h.Row(b).Detail);
    }

    // ---------------------------------------------------------------- C: modes

    [Fact]
    public void Any_unlocked_permits_every_unlocked_product()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        Assert.True(h.Row(OgKush).IsEligible);
        Assert.True(h.Row(SourDiesel).IsEligible);
        Assert.False(h.Row(Gdp).IsEligible);
        Assert.Equal("Not unlocked", h.Row(Gdp).StatusLabel);
        Assert.Equal(2, h.Menu.GetSelectedCategorySummary()!.EligibleCount);
    }

    [Fact]
    public void Selected_only_permits_just_the_selection()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Weed, SourDiesel, true);

        Assert.True(h.Row(SourDiesel).IsEligible);
        Assert.Equal(EligibilityReason.ProductNotSelected, h.Row(OgKush).Decision);
        Assert.Equal("1 permitted product", h.Menu.GetSelectedCategorySummary()!.EligibleLabel);
    }

    [Fact]
    public void Disabled_permits_nothing_and_keeps_the_selection()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Weed, OgKush, true);

        h.Menu.SetMode(Weed, PolicyMode.Disabled);
        Assert.Equal(0, h.Menu.GetSelectedCategorySummary()!.EligibleCount);
        Assert.All(h.Rows(), r => Assert.False(r.IsEligible));

        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        Assert.True(h.Row(OgKush).IsEligible);
    }

    [Fact]
    public void Empty_selection_warns_and_never_falls_back_to_any()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);

        var summary = h.Menu.GetSelectedCategorySummary()!;
        Assert.Equal(MenuText.EmptySelectionWarning, summary.Warning);
        Assert.Equal(0, summary.EligibleCount);
        Assert.Equal(PolicyMode.SelectedOnly, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
    }

    [Fact]
    public void Global_disable_is_represented_and_rules_still_preview()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        h.Menu.SetModEnabled(false);

        Assert.False(h.Menu.IsModEnabled);
        Assert.Equal(EligibilityReason.ModDisabled, h.Menu.GetPreview()!.Reason);
        Assert.Equal(2, h.Menu.GetSelectedCategorySummary()!.EligibleCount); // what the rule would permit once enabled
    }

    [Fact]
    public void Setting_a_rule_back_to_default_removes_the_override()
    {
        var h = new MenuHarness().Load();
        h.Menu.SetMode(Weed, PolicyMode.Disabled);
        h.Menu.SetMode(Weed, PolicyMode.AnyUnlocked);

        Assert.Empty(h.Menu.Draft.CategoryOverrides);
        Assert.False(h.Menu.HasUnsavedChanges);
    }

    // ---------------------------------------------------------------- E: preview

    [Fact]
    public void Preview_matches_the_core_policy()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Weed, SourDiesel, true);

        var preview = h.Menu.GetPreview()!;
        var core = SaleEligibilityPolicy.EvaluateCategory(h.Menu.Draft, h.Catalog.State.Snapshot, Weed);

        Assert.Equal(core.Reason, preview.Reason);
        Assert.Equal(core.EligibleProducts.Select(p => p.DisplayName), preview.PermittedNames);
        Assert.Equal("Hypothetical order: 5 marijuana", preview.Headline);
        Assert.Contains("sourdiesel: Permitted", preview.Lines);
        Assert.Contains("ogkush: Not permitted - Not selected", preview.Lines);
        Assert.Contains("granddaddypurple: Not permitted - Not unlocked", preview.Lines);
        Assert.True(preview.ReflectsUnsavedChanges);
        Assert.Contains("does not check stock", preview.Disclaimer);
    }

    [Fact]
    public void Preview_updates_with_draft_changes_without_rescanning_the_game()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        var calls = h.Source.Calls;

        h.Menu.SetMode(Weed, PolicyMode.Disabled);
        Assert.Equal(EligibilityReason.CategoryDisabled, h.Menu.GetPreview()!.Reason);
        h.Menu.SetMode(Weed, PolicyMode.AnyUnlocked);
        Assert.Equal(2, h.Menu.GetPreview()!.PermittedCount);

        Assert.Equal(calls, h.Source.Calls);
    }

    [Fact]
    public void Not_ready_catalog_is_never_an_empty_valid_catalog()
    {
        var h = new MenuHarness(() => DiscoveryResult.Failure("boom"));
        h.Catalog.Refresh(h.Catalog.BeginSave("save-a"));

        Assert.Equal(MenuAvailability.CatalogFailed, h.Menu.Availability);
        Assert.StartsWith(MenuText.CatalogFailed, h.Menu.AvailabilityText);
        Assert.Empty(h.Menu.GetCategories());
        Assert.False(h.Menu.SetModEnabled(false));
        Assert.False(h.Menu.Save());
        Assert.Equal(0, h.Store.Writes);
    }

    // ---------------------------------------------------------------- G: lifecycle

    [Fact]
    public void Catalog_refresh_shows_newly_unlocked_products()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);
        Assert.False(h.Row(Gdp).IsSelectable);

        h.Source.Next = AllWeedUnlockedResult;
        h.Catalog.Refresh(h.Catalog.State.Session);

        Assert.True(h.Row(Gdp).IsSelectable);
        Assert.True(h.Menu.SetProductSelected(Weed, Gdp, true));
    }

    [Fact]
    public void Switching_saves_resets_the_editing_state_and_drops_unsaved_changes()
    {
        var h = new MenuHarness().Load("save-a");
        h.Menu.SelectCategory(Weed);
        h.Menu.SetSearch("kush");
        h.Menu.SetMode(Weed, PolicyMode.Disabled);

        h.Load("save-b");

        Assert.False(h.Menu.HasUnsavedChanges);
        Assert.Equal(PolicyMode.AnyUnlocked, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(string.Empty, h.Menu.SearchText);
        Assert.False(h.Menu.SelectedCategory.IsValid);
        Assert.Equal(FeedbackKind.Warning, h.Menu.Feedback.Kind);
        Assert.Empty(h.Store.Files); // the old draft was not written anywhere
    }

    [Fact]
    public void Returning_to_menu_clears_the_catalog_view()
    {
        var h = new MenuHarness().Load();
        h.Menu.SelectCategory(Weed);

        h.Catalog.EndSave();

        Assert.Equal(MenuAvailability.WaitingForSave, h.Menu.Availability);
        Assert.Equal(MenuScreen.Overview, h.Menu.Screen);
        Assert.Null(h.Menu.ActiveSaveLabel);
    }

    [Fact]
    public void Disposing_unsubscribes_from_the_catalog()
    {
        var h = new MenuHarness();
        var changes = 0;
        h.Menu.Changed += () => changes++;

        h.Menu.Dispose();
        h.Load();

        Assert.Equal(0, changes);
        Assert.Null(h.Menu.ActiveSaveLabel); // the new save was never picked up
        Assert.Equal(0, h.Store.Writes);
    }

    [Fact]
    public void Faulty_change_handler_is_contained()
    {
        var h = new MenuHarness();
        h.Menu.Changed += () => throw new InvalidOperationException("renderer bug");

        h.Load();

        Assert.Equal(MenuAvailability.Ready, h.Menu.Availability);
        Assert.NotEmpty(h.Log.Errors);
    }

    private static DiscoveryResult AllWeedUnlockedResult()
        => Result(Product(OgKush, Weed), Product(SourDiesel, Weed), Product(Gdp, Weed), Product(Meth1, Meth), Product(Coke1, Cocaine));
}
