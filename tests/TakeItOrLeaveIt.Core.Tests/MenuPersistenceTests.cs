using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 02 group F: save, reload, discard, reset, failures, per-save isolation.</summary>
public class MenuPersistenceTests
{
    private static MenuHarness ConfigureWeedSelectedOnly(MenuHarness h)
    {
        h.Menu.SetMode(Weed, PolicyMode.SelectedOnly);
        h.Menu.SetProductSelected(Weed, SourDiesel, true);
        return h;
    }

    [Fact]
    public void Save_stores_mode_and_stable_ids_and_reports_success()
    {
        var h = ConfigureWeedSelectedOnly(new MenuHarness().Load("save-a"));

        Assert.True(h.Menu.Save());

        Assert.Equal(FeedbackKind.Success, h.Menu.Feedback.Kind);
        Assert.Equal(MenuText.Saved, h.Menu.Feedback.Text);
        Assert.False(h.Menu.HasUnsavedChanges);
        var stored = PolicySettingsJson.FromJson(h.Store.Files["save-a"]);
        Assert.Equal("selectedOnly", stored.Categories!["marijuana"].Mode);
        Assert.Equal(new[] { "sourdiesel" }, stored.Categories["marijuana"].SelectedProducts);
    }

    [Fact]
    public void Saved_policy_is_published_to_the_provider_only_after_save()
    {
        var h = ConfigureWeedSelectedOnly(new MenuHarness().Load("save-a"));
        Assert.Equal(PolicyMode.AnyUnlocked, h.Provider.Current.GetEffectivePolicy(Weed).Mode);

        h.Menu.Save();

        Assert.Equal(PolicyMode.SelectedOnly, h.Provider.Current.GetEffectivePolicy(Weed).Mode);
    }

    [Fact]
    public void Restart_restores_saved_settings()
    {
        var store = new MemoryPolicyStore();
        ConfigureWeedSelectedOnly(new MenuHarness(store: store).Load("save-a")).Menu.Save();

        var restarted = new MenuHarness(store: store).Load("save-a");

        Assert.Equal(PolicyMode.SelectedOnly, restarted.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(new[] { SourDiesel }, restarted.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.False(restarted.Menu.HasUnsavedChanges);
        Assert.Equal(PolicyMode.SelectedOnly, restarted.Provider.Current.GetEffectivePolicy(Weed).Mode);
    }

    [Fact]
    public void Failed_write_does_not_report_success_or_change_saved_state()
    {
        var h = ConfigureWeedSelectedOnly(new MenuHarness().Load("save-a"));
        h.Store.FailWrites = true;

        Assert.False(h.Menu.Save());

        Assert.Equal(FeedbackKind.Error, h.Menu.Feedback.Kind);
        Assert.Contains("NOT saved", h.Menu.Feedback.Text);
        Assert.True(h.Menu.HasUnsavedChanges);
        Assert.Equal(PolicyMode.AnyUnlocked, h.Provider.Current.GetEffectivePolicy(Weed).Mode);
    }

    [Fact]
    public void Discard_restores_the_last_saved_configuration()
    {
        var h = ConfigureWeedSelectedOnly(new MenuHarness().Load("save-a"));
        h.Menu.Save();
        h.Menu.SetMode(Meth, PolicyMode.Disabled);
        h.Menu.ClearSelection(Weed);

        h.Menu.Discard();

        Assert.False(h.Menu.HasUnsavedChanges);
        Assert.Equal(new[] { SourDiesel }, h.Menu.Draft.GetEffectivePolicy(Weed).SelectedProducts);
        Assert.Equal(PolicyMode.AnyUnlocked, h.Menu.Draft.GetEffectivePolicy(Meth).Mode);
    }

    [Fact]
    public void Reset_needs_confirmation_and_then_saving()
    {
        var h = ConfigureWeedSelectedOnly(new MenuHarness().Load("save-a"));
        h.Menu.Save();

        Assert.False(h.Menu.ConfirmReset()); // not requested yet
        h.Menu.RequestReset();
        h.Menu.CancelReset();
        Assert.False(h.Menu.ConfirmReset());
        Assert.Equal(PolicyMode.SelectedOnly, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);

        h.Menu.RequestReset();
        Assert.True(h.Menu.ConfirmReset());
        Assert.Equal(PolicyMode.AnyUnlocked, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.True(h.Menu.Draft.Enabled);
        Assert.True(h.Menu.HasUnsavedChanges);

        h.Menu.Save();
        var stored = PolicySettingsJson.FromJson(h.Store.Files["save-a"]);
        Assert.Empty(stored.Categories!);
    }

    [Fact]
    public void Saves_do_not_share_settings()
    {
        var store = new MemoryPolicyStore();
        var h = ConfigureWeedSelectedOnly(new MenuHarness(store: store).Load("save-a"));
        h.Menu.Save();

        h.Load("save-b");
        Assert.Equal(PolicyMode.AnyUnlocked, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        h.Menu.SetMode(Weed, PolicyMode.Disabled);
        h.Menu.Save();

        h.Load("save-a");
        Assert.Equal(PolicyMode.SelectedOnly, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(new[] { "save-a", "save-b" }, store.Files.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Stale_selections_are_kept_but_inactive()
    {
        var store = new MemoryPolicyStore();
        store.Files["save-a"] = "{\"schemaVersion\":1,\"enabled\":true,\"categories\":{\"marijuana\":{\"mode\":\"selectedOnly\",\"selectedProducts\":[\"removed-mix\",\"ogkush\"]}}}";

        var h = new MenuHarness(store: store).Load("save-a");
        h.Menu.SelectCategory(Weed);

        Assert.Equal(new[] { ProductKey.Create("removed-mix") }, h.Menu.GetInactiveSelections());
        Assert.Equal(1, h.Menu.GetSelectedCategorySummary()!.EligibleCount);
        Assert.Equal(1, h.Menu.GetSelectedCategorySummary()!.InactiveSelectionCount);
        Assert.False(h.Menu.HasUnsavedChanges);
    }

    [Fact]
    public void Unreadable_file_shows_defaults_warns_and_is_not_overwritten_until_save()
    {
        var store = new MemoryPolicyStore();
        store.Files["save-a"] = "{ not json";

        var h = new MenuHarness(store: store).Load("save-a");

        Assert.Equal(FeedbackKind.Warning, h.Menu.Feedback.Kind);
        Assert.Equal(PolicyMode.AnyUnlocked, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.Equal("{ not json", store.Files["save-a"]);
    }

    [Fact]
    public void Invalid_values_in_the_file_are_made_safe_and_reported()
    {
        var store = new MemoryPolicyStore();
        store.Files["save-a"] = "{\"schemaVersion\":1,\"categories\":{\"marijuana\":{\"mode\":\"everything\"}}}";

        var h = new MenuHarness(store: store).Load("save-a");

        Assert.Equal(PolicyMode.Disabled, h.Menu.Draft.GetEffectivePolicy(Weed).Mode);
        Assert.Equal(FeedbackKind.Warning, h.Menu.Feedback.Kind);
    }

    [Fact]
    public void Catalog_failure_never_writes_settings()
    {
        var store = new MemoryPolicyStore();
        ConfigureWeedSelectedOnly(new MenuHarness(store: store).Load("save-a")).Menu.Save();
        var before = store.Files["save-a"];

        var h = new MenuHarness(store: store);
        h.Source.Next = () => Discovery.DiscoveryResult.Failure("member missing");
        h.Load("save-a");
        h.Menu.Save();
        h.Menu.Discard();

        Assert.Equal(before, store.Files["save-a"]);
    }

    // ------------------------------------------------------------- JSON store on disk

    [Fact]
    public void Json_store_round_trips_on_disk_atomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiol-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonPolicyStore(root);
            var settings = PolicyConfiguration.Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(Gdp)).ToSettings();

            Assert.Equal(PolicyLoadStatus.NotFound, store.Load("save-a").Status);
            store.Save("save-a", settings);
            store.Save("save-a", settings);

            var loaded = store.Load("save-a");
            Assert.Equal(PolicyLoadStatus.Loaded, loaded.Status);
            Assert.Equal(new[] { "granddaddypurple" }, loaded.Settings!.Categories!["marijuana"].SelectedProducts);
            // Phase 06: the previous valid file is kept as the known-good backup; no temporary file is left.
            Assert.Equal(new[] { "save-a.json", "save-a.json.bak" }, Directory.GetFiles(Path.Combine(root, "saves")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Json_store_backs_up_an_unreadable_file_before_replacing_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiol-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonPolicyStore(root);
            Directory.CreateDirectory(Path.Combine(root, "saves"));
            File.WriteAllText(store.PathFor("save-a"), "garbage");

            Assert.Equal(PolicyLoadStatus.Unreadable, store.Load("save-a").Status);
            store.Save("save-a", PolicyConfiguration.Default.ToSettings());

            // Phase 06: kept as <file>.corrupt-<UTC time> (replaces the single, overwritable .unreadable.bak).
            var copy = Assert.Single(Directory.GetFiles(Path.Combine(root, "saves"), "save-a.json.corrupt-*"));
            Assert.Equal("garbage", File.ReadAllText(copy));
            Assert.Equal(PolicyLoadStatus.Loaded, store.Load("save-a").Status);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Failed_write_leaves_the_previous_file_intact()
    {
        var fs = new FlakyFileSystem();
        var store = new JsonPolicyStore("root", fs);
        store.Save("save-a", PolicyConfiguration.Default.ToSettings());
        var before = fs.Files[store.PathFor("save-a")];

        fs.FailReplace = true;
        Assert.Throws<IOException>(() => store.Save("save-a", PolicyConfiguration.Default.WithEnabled(false).ToSettings()));

        Assert.Equal(before, fs.Files[store.PathFor("save-a")]);
        Assert.DoesNotContain(fs.Files.Keys, k => k.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("C:\\x")]
    public void Json_store_rejects_unsafe_identities(string identity)
    {
        Assert.Throws<ArgumentException>(() => new JsonPolicyStore("root").PathFor(identity));
    }

    private sealed class FlakyFileSystem : ISettingsFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public bool FailReplace { get; set; }

        public bool FileExists(string path) => Files.ContainsKey(path);

        public string ReadAllText(string path) => Files[path];

        public void WriteAllText(string path, string contents) => Files[path] = contents;

        public void ReplaceFile(string source, string destination)
        {
            if (FailReplace)
            {
                throw new IOException("replace failed (simulated)");
            }

            Files[destination] = Files[source];
            Files.Remove(source);
        }

        public void CopyFile(string source, string destination) => Files[destination] = Files[source];

        public void DeleteFile(string path) => Files.Remove(path);

        public void CreateDirectory(string path)
        {
        }

        public IEnumerable<string> ListFiles(string directory, string prefix)
            => Files.Keys.Where(k => string.Equals(Path.GetDirectoryName(k), directory, StringComparison.Ordinal) &&
                                     Path.GetFileName(k).StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }
}
