using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 06 suite D: settings files — newer schemas, damage, recovery, wrong types, determinism.</summary>
public class SettingsFileTests
{
    private static PolicyConfiguration Parse(string json, out IReadOnlyList<ConfigurationIssue> issues)
        => PolicyConfiguration.FromSettings(PolicySettingsJson.FromJson(json), out issues);

    private static string Weed(string body) => "{\"schemaVersion\":2,\"categories\":{\"marijuana\":{" + body + "}}}";

    [Fact]
    public void P06_D01_newer_settings_are_never_rewritten_and_keep_mod_sales_off()
    {
        var h = new DiskMenuHarness();
        const string future = "{\"schemaVersion\":3,\"enabled\":true,\"categories\":{},\"newThing\":{\"a\":1}}";
        h.Fs[h.PathOf("save-a")] = future;

        h.Load("save-a");

        Assert.Equal(PolicyLoadStatus.NewerSchema, h.Store.Load("save-a").Status);
        Assert.True(h.Menu.SettingsFromNewerVersion);
        Assert.False(h.Menu.PolicyReadyForSales);
        Assert.True(h.Menu.SetMode(Catalog.Weed, PolicyMode.Disabled));
        Assert.False(h.Menu.Save());
        Assert.Contains("newer version", h.Menu.Feedback.Text);
        Assert.Throws<NewerSchemaException>(() => h.Store.Save("save-a", PolicyConfiguration.Default.ToSettings()));
        Assert.Equal(future, h.Fs[h.PathOf("save-a")]);
        Assert.Equal(new[] { h.PathOf("save-a") }, h.Fs.Paths);
    }

    [Fact]
    public void P06_D02_damaged_settings_offer_the_verified_backup_as_unsaved_changes()
    {
        var h = new DiskMenuHarness();
        var good = PolicyConfiguration.Default.WithCategory(Catalog.Weed, CategoryPolicy.SelectedOnly(Gdp));
        h.Store.Save("save-a", good.ToSettings());
        h.Store.Save("save-a", good.WithMixing(MixingMode.SingleProduct).ToSettings()); // .bak = first save
        h.Fs[h.PathOf("save-a")] = "{\"schemaVersion\":2,\"categories\":{\"mari";       // torn write

        h.Load("save-a");

        Assert.False(h.Menu.PolicyReadyForSales);                  // never silently unrestricted
        Assert.True(h.Menu.HasUnsavedChanges);
        Assert.True(h.Menu.Draft.GetEffectivePolicy(Catalog.Weed).SameAs(CategoryPolicy.SelectedOnly(Gdp)));
        Assert.Contains("backup", h.Menu.Feedback.Text);
        Assert.Contains("verified backup", h.Menu.SettingsFileState);

        Assert.True(h.Menu.Save());
        Assert.True(h.Menu.PolicyReadyForSales);
        Assert.Equal(PolicyMode.SelectedOnly, h.Provider.Current.GetEffectivePolicy(Catalog.Weed).Mode);
        var copy = Assert.Single(SafeDocumentFile.CorruptCopies(h.Fs, h.PathOf("save-a")));
        Assert.StartsWith("{\"schemaVersion\":2,\"categories\":{\"mari", h.Fs[copy]);
    }

    [Fact]
    public void P06_D03_a_backup_written_for_another_save_is_not_offered()
    {
        var h = new DiskMenuHarness();
        h.Fs[h.PathOf("save-a") + ".bak"] = PolicySettingsJson.ToJson(PolicyConfiguration.Default.ToSettings(), "save-b");
        h.Fs[h.PathOf("save-a")] = "garbage";

        var result = h.Store.Load("save-a");
        h.Load("save-a");

        Assert.Equal(PolicyLoadStatus.Unreadable, result.Status);
        Assert.Null(result.Recovery);
        Assert.Contains("could not be verified", result.RecoveryNote);
        Assert.False(h.Menu.HasUnsavedChanges);
        Assert.False(h.Menu.PolicyReadyForSales);
        Assert.Contains("no verified backup", h.Menu.SettingsFileState);
    }

    [Fact]
    public void P06_D04_a_settings_file_of_another_save_is_unreadable_here()
    {
        var h = new DiskMenuHarness();
        h.Fs[h.PathOf("save-a")] = PolicySettingsJson.ToJson(PolicyConfiguration.Default.ToSettings(), "save-b");

        var result = h.Store.Load("save-a");

        Assert.Equal(PolicyLoadStatus.Unreadable, result.Status);
        Assert.Contains("different save", result.Message);
    }

    [Fact]
    public void P06_D05_a_locked_file_is_reported_and_never_replaced()
    {
        var h = new DiskMenuHarness();
        h.Store.Save("save-a", PolicyConfiguration.Default.WithEnabled(false).ToSettings());
        var before = h.Fs[h.PathOf("save-a")];
        h.Fs.FailReadsOf.Add(h.PathOf("save-a"));

        var result = h.Store.Load("save-a");
        Assert.Equal(PolicyLoadStatus.Unreadable, result.Status);
        Assert.Null(result.Recovery);
        Assert.ThrowsAny<IOException>(() => h.Store.Save("save-a", PolicyConfiguration.Default.ToSettings()));

        h.Fs.FailReadsOf.Clear();
        Assert.Equal(before, h.Fs[h.PathOf("save-a")]);
        Assert.Empty(SafeDocumentFile.CorruptCopies(h.Fs, h.PathOf("save-a")));
    }

    [Theory]
    [InlineData("\"mode\":\"blocklist\",\"blockedProducts\":[\"ogkush\",5]")]
    [InlineData("\"mode\":\"blocklist\",\"blockedProducts\":\"ogkush\"")]
    [InlineData("\"mode\":\"selectedOnly\",\"selectedProducts\":[\"ogkush\",{}]")]
    [InlineData("\"mode\":\"anyUnlocked\",\"minQuality\":3")]
    [InlineData("\"mode\":\"anyUnlocked\",\"effects\":\"calming\"")]
    [InlineData("\"mode\":\"anyUnlocked\",\"effects\":{\"required\":[\"calming\",7]}")]
    [InlineData("\"mode\":1")]
    [InlineData("\"mode\":\"blocklist\",\"match\":true,\"blockedProducts\":[\"ogkush\"]")]
    [InlineData("\"mode\":\"blocklist\",\"blockedProducts\":[\"ogkush\",\"\"]")]
    public void P06_D06_wrongly_typed_or_unreadable_rule_fields_never_loosen_a_rule(string body)
    {
        var policy = Parse(Weed(body), out var issues);

        Assert.NotEmpty(issues);
        Assert.Equal(PolicyMode.Disabled, policy.GetEffectivePolicy(Catalog.Weed).Mode);
    }

    [Fact]
    public void P06_D07_wrongly_typed_global_fields_fail_closed()
    {
        var policy = Parse("{\"schemaVersion\":2,\"defaultMode\":0,\"mixing\":2,\"categories\":{}}", out var issues);

        Assert.Equal(PolicyMode.Disabled, policy.DefaultMode);
        Assert.Equal(MixingMode.SingleProduct, policy.Mixing);
        Assert.Equal(2, issues.Count);
        Assert.Throws<InvalidDataException>(() => PolicySettingsJson.FromJson("{\"schemaVersion\":2,\"categories\":{\"marijuana\":\"selectedOnly\"}}"));
        Assert.Throws<InvalidDataException>(() => PolicySettingsJson.FromJson("{\"schemaVersion\":2,\"dealers\":{\"benji_coleman\":[]}}"));
        Assert.Throws<InvalidDataException>(() => PolicySettingsJson.FromJson("{\"schemaVersion\":2,\"save\":5}"));
        Assert.Throws<InvalidDataException>(() => PolicySettingsJson.FromJson("{\"schemaVersion\":2,\"playerSales\":\"no\"}"));
    }

    [Fact]
    public void P06_D08_missing_optional_fields_keep_their_documented_defaults()
    {
        var policy = Parse("{\"schemaVersion\":2,\"categories\":{\"marijuana\":{\"mode\":\"selectedOnly\",\"selectedProducts\":[\"ogkush\"]}}}", out var issues);

        Assert.Empty(issues);
        var weed = policy.GetEffectivePolicy(Catalog.Weed);
        Assert.Equal(SelectionMatch.ExactProduct, weed.Match);
        Assert.Null(weed.MinimumQuality);
        Assert.True(weed.Effects.IsEmpty);
        Assert.True(policy.PlayerSalesEnabled && policy.DealerSalesEnabled && policy.Enabled);
        Assert.Equal(MixingMode.AnyPermitted, policy.Mixing);
    }

    [Fact]
    public void P06_D09_unknown_fields_are_reported_and_do_not_block_loading()
    {
        var settings = PolicySettingsJson.FromJson("{\"schemaVersion\":2,\"theme\":\"dark\",\"categories\":{\"marijuana\":{\"mode\":\"anyUnlocked\",\"note\":\"x\",\"effects\":{\"match\":\"all\",\"weight\":2}}},\"dealers\":{\"benji_coleman\":{\"categories\":{},\"nick\":\"B\"}}}");

        Assert.Equal(new[] { "theme", "categories.marijuana.effects.weight", "categories.marijuana.note", "dealers.benji_coleman.nick" }, settings.UnknownFields);
        PolicyConfiguration.FromSettings(settings, out var issues);
        Assert.Empty(issues);

        var h = new DiskMenuHarness();
        h.Fs[h.PathOf("save-a")] = PolicySettingsJson.ToJson(new PolicySettings()).Replace("\"schemaVersion\"", "\"theme\": 1, \"schemaVersion\"");
        h.Load("save-a");
        Assert.True(h.Menu.PolicyReadyForSales);
        Assert.Contains(h.Log.Warnings, w => w.Contains("theme") && w.Contains(".bak"));
    }

    [Fact]
    public void P06_D10_serialisation_is_deterministic_and_round_trips_every_rule()
    {
        var weed = new CategoryPolicy(PolicyMode.Blocklist, new[] { SourDiesel, Gdp }, SelectionMatch.BaseStrain, new[] { OgKush, Gdp },
            new EffectRule(EffectMatch.Any, new[] { "sedating", "calming" }, new[] { "paranoia" }), QualityTier.Premium);
        var policy = PolicyConfiguration.Default
            .WithCategory(Catalog.Weed, weed)
            .WithCategory(Meth, CategoryPolicy.Disabled)
            .WithMixing(MixingMode.RequestedOnly)
            .WithDealerSales(false)
            .WithDealerCategory(Dealers.Benji, Catalog.Weed, CategoryPolicy.SelectedOnly(Gdp))
            .WithDealerCategory("retired_dealer", Cocaine, CategoryPolicy.Disabled); // orphaned override: kept

        var fs = new FaultyFileSystem();
        var store = new JsonPolicyStore("root", fs);
        store.Save("save-a", policy.ToSettings());
        var first = fs[store.PathFor("save-a")];
        var back = PolicyConfiguration.FromSettings(store.Load("save-a").Settings, out var issues);
        store.Save("save-a", back.ToSettings());

        Assert.Empty(issues);
        Assert.Equal(first, fs[store.PathFor("save-a")]);          // byte-identical on re-save
        Assert.True(back.GetEffectivePolicy(Catalog.Weed).SameAs(weed));
        Assert.Equal(MixingMode.RequestedOnly, back.Mixing);
        Assert.False(back.DealerSalesEnabled);
        Assert.Equal(PolicyMode.SelectedOnly, back.GetDealerOverride(Dealers.Benji, Catalog.Weed)!.Mode);
        Assert.Equal(PolicyMode.Disabled, back.GetDealerOverride("retired_dealer", Cocaine)!.Mode);
        var json = first.Replace(" ", string.Empty);
        Assert.True(json.IndexOf("\"granddaddypurple\",\"sourdiesel\"", StringComparison.Ordinal) >= 0); // sorted list
        Assert.True(PolicyPresets.Detect(back) == PolicyPresets.Detect(policy));
    }

    [Fact]
    public void P06_D11_schema_1_migration_is_idempotent_and_its_backup_is_never_overwritten()
    {
        const string schema1 = "{\"schemaVersion\":1,\"enabled\":false,\"mixedDeliveries\":false,\"categories\":{\"marijuana\":{\"mode\":\"selectedOnly\",\"selectedProducts\":[\"granddaddypurple\"]}}}";
        var h = new DiskMenuHarness(discovery: () => Result(Product(OgKush, Catalog.Weed), Product(Gdp, Catalog.Weed)));
        var path = h.PathOf("save-a");
        h.Fs[path] = schema1;

        h.Load("save-a");
        h.Menu.Save();
        var migrated = h.Fs[path];
        for (var i = 0; i < 3; i++)
        {
            var again = h.Restart().Load("save-a");
            Assert.True(again.Menu.PolicyReadyForSales);
            again.Menu.Save();
        }

        Assert.Equal(schema1, h.Fs[path + ".schema1.bak"]);
        Assert.Equal(migrated, h.Fs[path]);
        var policy = h.Restart().Load("save-a").Menu.Saved;
        Assert.False(policy.Enabled);
        Assert.Equal(MixingMode.SingleProduct, policy.Mixing);
        var weed = policy.GetEffectivePolicy(Catalog.Weed);
        Assert.Equal(SelectionMatch.ExactProduct, weed.Match);
        Assert.Equal(new[] { Gdp }, weed.SelectedProducts);
    }
}

/// <summary>Phase 06 suite E: one isolated profile per save, across switches and restarts.</summary>
public class SaveProfileIsolationTests
{
    private static DiscoveryResult AllUnlocked() => Result(Product(OgKush, Catalog.Weed), Product(SourDiesel, Catalog.Weed), Product(Gdp, Catalog.Weed),
        Product(Meth1, Meth), Product(Coke1, Cocaine));

    [Fact]
    public void P06_E01_each_save_keeps_its_own_rules_across_switches_and_restarts()
    {
        var h = new DiskMenuHarness(discovery: AllUnlocked);

        h.Load("save-a");
        Assert.True(h.Menu.SetMode(Catalog.Weed, PolicyMode.SelectedOnly));
        Assert.True(h.Menu.SetProductSelected(Catalog.Weed, Gdp, true));
        Assert.True(h.Menu.Save());

        h.Load("save-b");
        Assert.Equal(PolicyMode.AnyUnlocked, h.Provider.Current.GetEffectivePolicy(Catalog.Weed).Mode);
        Assert.True(h.Menu.SetDealerSales(false));
        Assert.True(h.Menu.Save());

        h.Load("save-a");
        var a = h.Provider.Current;
        Assert.Equal(new[] { Gdp }, a.GetEffectivePolicy(Catalog.Weed).SelectedProducts);
        Assert.True(a.DealerSalesEnabled);

        var restarted = h.Restart().Load("save-b");
        Assert.Equal(PolicyMode.AnyUnlocked, restarted.Provider.Current.GetEffectivePolicy(Catalog.Weed).Mode);
        Assert.False(restarted.Provider.Current.DealerSalesEnabled);
        restarted.Load("save-a");
        Assert.Equal(new[] { Gdp }, restarted.Provider.Current.GetEffectivePolicy(Catalog.Weed).SelectedProducts);
    }

    [Fact]
    public void P06_E02_a_selection_of_one_save_never_permits_a_product_in_another_save()
    {
        var h = new DiskMenuHarness(discovery: AllUnlocked);
        h.Load("save-a");
        h.Menu.SetMode(Catalog.Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Catalog.Weed, Gdp, true);
        h.Menu.Save();

        // Save B has not unlocked GDP and has its own (default) rules.
        h.Source.Next = Standard;
        h.Load("save-b");
        var snapshot = h.Catalog.State.Snapshot!;
        var eligible = SaleEligibilityPolicy.EvaluateCategory(h.Provider.Current, snapshot, Catalog.Weed).EligibleProducts.Keys().ToList();

        Assert.DoesNotContain(Gdp, eligible);
        Assert.Contains(OgKush, eligible);
    }

    [Fact]
    public void P06_E03_a_failed_catalog_never_rewrites_or_loses_a_saved_selection()
    {
        var h = new DiskMenuHarness(discovery: AllUnlocked);
        h.Load("save-a");
        h.Menu.SetMode(Catalog.Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Catalog.Weed, Gdp, true);
        h.Menu.Save();
        var saved = h.Fs[h.PathOf("save-a")];

        h.Source.Next = () => DiscoveryResult.Failure("product manager not ready");
        var broken = h.Restart().Load("save-a");
        Assert.False(broken.Menu.CanEdit);
        Assert.False(broken.Menu.Save());
        Assert.Equal(saved, h.Fs[h.PathOf("save-a")]);

        h.Source.Next = AllUnlocked;
        var healed = h.Restart().Load("save-a");
        Assert.Equal(new[] { Gdp }, healed.Provider.Current.GetEffectivePolicy(Catalog.Weed).SelectedProducts);
    }

    [Fact]
    public void P06_E04_an_undiscovered_selection_is_kept_inactive_and_returns_when_discovered()
    {
        var h = new DiskMenuHarness(discovery: AllUnlocked);
        h.Load("save-a");
        h.Menu.SetMode(Catalog.Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Catalog.Weed, Gdp, true);
        h.Menu.SetProductSelected(Catalog.Weed, OgKush, true);
        h.Menu.Save();

        // A later session where GDP is not discovered (e.g. game update): the entry is kept, never permitted, never renamed.
        h.Source.Next = () => Result(Product(OgKush, Catalog.Weed), Product(SourDiesel, Catalog.Weed));
        var later = h.Restart().Load("save-a");
        var eligible = SaleEligibilityPolicy.EvaluateCategory(later.Provider.Current, later.Catalog.State.Snapshot!, Catalog.Weed).EligibleProducts.Keys();
        Assert.Equal(new[] { OgKush }, eligible);
        Assert.True(later.Menu.SetMixing(MixingMode.SingleProduct));
        Assert.True(later.Menu.Save());                                   // an unrelated edit...
        Assert.Contains(Gdp, later.Menu.Saved.GetEffectivePolicy(Catalog.Weed).SelectedProducts); // ...keeps the stale entry

        h.Source.Next = AllUnlocked;
        var again = h.Restart().Load("save-a");
        var back = SaleEligibilityPolicy.EvaluateCategory(again.Provider.Current, again.Catalog.State.Snapshot!, Catalog.Weed).EligibleProducts.Keys();
        Assert.Equal(new[] { Gdp, OgKush }.OrderBy(k => k), back.OrderBy(k => k));
    }

    [Fact]
    public void P06_E05_dealer_overrides_stay_with_their_save_and_their_dealer()
    {
        var h = new DiskMenuHarness(discovery: AllUnlocked);
        var withOverride = PolicyConfiguration.Default.WithDealerCategory(Dealers.Benji, Catalog.Weed, CategoryPolicy.SelectedOnly(Gdp));
        h.Store.Save("save-a", withOverride.ToSettings());

        h.Load("save-b");
        Assert.Empty(h.Provider.Current.DealerOverrides);

        h.Load("save-a");
        var a = h.Provider.Current;
        Assert.Equal(PolicyMode.SelectedOnly, a.ForScope(SalesScope.Dealer(Dealers.Benji)).GetEffectivePolicy(Catalog.Weed).Mode);
        Assert.Equal(PolicyMode.AnyUnlocked, a.ForScope(SalesScope.Dealer("someone_else")).GetEffectivePolicy(Catalog.Weed).Mode);
    }
}

/// <summary>Phase 06 suite F: accepted contracts keep their acceptance-time rules across save and reload.</summary>
public class ContractRecoveryTests
{
    [Fact]
    public void P06_F01_an_accepted_order_keeps_its_permissions_after_the_rules_change_and_a_reload()
    {
        var provider = new InMemoryPolicyProvider(PolicyConfiguration.Default.WithCategory(Catalog.Weed, CategoryPolicy.SelectedOnly(OgKush, Gdp)));
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var coordinator = new PlayerSaleCoordinator(provider, () => AllWeedUnlocked()) { Prices = Flat };
        coordinator.SetAuthority(true, 1);
        files.Bind("save-a");
        coordinator.BindSave("save-a", null);
        var original = coordinator.OnContractAccepted(Request(ContractA, (Gdp, 5))).Order!;
        Assert.Contains(OgKush, original.AllowedProducts);

        // Weed becomes GDP only; save and quit; reload.
        provider.Update(p => p.WithCategory(Catalog.Weed, CategoryPolicy.SelectedOnly(Gdp)));
        files.Submit("save-a", ActiveOrderStore.ToJson(coordinator.Book.Orders, "save-a"), null, null);
        Assert.True(files.Queue.Flush(1_000));

        var reloaded = new PlayerSaleCoordinator(provider, () => AllWeedUnlocked()) { Prices = Flat };
        reloaded.SetAuthority(true, 1);
        var load = new SalesPersistence("root", fs).Bind("save-a")!;
        reloaded.BindSave("save-a", load.Orders);

        var restored = reloaded.Book.Orders.Single().Order;
        Assert.Equal(original.AllowedProducts.OrderBy(k => k), restored.AllowedProducts.OrderBy(k => k));
        Assert.Equal(original.PolicyRevision, restored.PolicyRevision);
        Assert.Equal(original.PricingBasis, restored.PricingBasis);
        Assert.Equal(original.Mixing, restored.Mixing);
        Assert.Equal(original.RequiredUnits, restored.RequiredUnits);

        var fresh = reloaded.OnContractAccepted(Request(ContractB, (Gdp, 5))).Order!;
        Assert.Equal(new[] { Gdp }, fresh.AllowedProducts);
    }

    [Fact]
    public void P06_F02_an_order_whose_snapshot_cannot_be_read_is_not_rebuilt_from_current_rules()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var book = new ActiveOrderBook();
        book.TryAdd(new CategoryOrder(ContractA, Catalog.Weed, 5, new[] { OgKush }, true, 1, minimumQuality: QualityTier.Premium));
        var json = ActiveOrderStore.ToJson(book.Orders, "save-a").Replace("\"premium\"", "\"legendary\"");
        fs[files.OrderStore.PathFor("save-a")] = json;

        var load = files.Bind("save-a")!;

        Assert.Empty(load.Orders);
        Assert.Contains(load.Issues, i => i.Contains("unknown minimum quality"));
    }
}

/// <summary>Phase 06 suite G: save identity rules.</summary>
public class SaveIdentityTests
{
    [Fact]
    public void P06_G01_identity_is_deterministic_anonymous_and_file_safe()
    {
        var a = SaveIdentity.FromFolderPath(@"C:\Users\x\AppData\LocalLow\TVGS\Schedule I\Saves\76561198000000000\SaveGame_1");
        var same = SaveIdentity.FromFolderPath("c:/users/x/appdata/locallow/tvgs/schedule i/saves/76561198000000000/savegame_1/");
        var other = SaveIdentity.FromFolderPath(@"C:\Users\x\AppData\LocalLow\TVGS\Schedule I\Saves\76561198000000000\SaveGame_2");

        Assert.Equal(a, same);
        Assert.NotEqual(a, other);
        Assert.Matches("^save-[0-9a-f]{12}$", a);
        Assert.True(SaveFileNames.IsValid(a));
        Assert.DoesNotContain("SaveGame", a);
        Assert.Null(SaveIdentity.FromFolderPath(null));
        Assert.Null(SaveIdentity.FromFolderPath("   "));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("save-a/../b")]
    [InlineData("save.a")]
    [InlineData("C:save")]
    [InlineData("save-é")]
    [InlineData("")]
    public void P06_G02_unsafe_identities_never_reach_the_file_system(string identity)
    {
        Assert.False(SaveFileNames.IsValid(identity));
        Assert.Throws<ArgumentException>(() => new ActiveOrderStore("root", new FaultyFileSystem()).PathFor(identity));
        Assert.Throws<ArgumentException>(() => new JsonPolicyStore("root", new FaultyFileSystem()).PathFor(identity));
    }

    [Fact]
    public void P06_G03_without_a_save_identity_nothing_is_loaded_or_written()
    {
        var lifecycle = new SaveLifecycleCoordinator(new ProductCatalogService(new FakeDiscoverySource { Next = Standard }));

        Assert.Equal(LifecycleAction.None, lifecycle.Observe(SaveObservation.Of(isLoaded: true, isLoading: false, saveIdentity: null)));
        var files = new SalesPersistence("root", new FaultyFileSystem());
        Assert.Null(files.Bind(null));
        Assert.Contains(files.StatusLines(), l => l.Contains("nothing is written"));
    }
}
