using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group F (continued): lifecycle decisions driven by game observations.</summary>
public class SaveLifecycleTests
{
    private readonly FakeDiscoverySource _source = new() { Next = Standard };
    private readonly ProductCatalogService _service;
    private readonly SaveLifecycleCoordinator _coordinator;

    public SaveLifecycleTests()
    {
        _service = new ProductCatalogService(_source);
        _coordinator = new SaveLifecycleCoordinator(_service);
    }

    [Fact]
    public void Main_menu_keeps_no_save()
    {
        Assert.Equal(LifecycleAction.None, _coordinator.Observe(SaveObservation.NoGame()));
        Assert.Equal(CatalogStatus.NoSave, _service.State.Status);
        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public void Loading_then_loaded_discovers_once_after_loading_finishes()
    {
        Assert.Equal(LifecycleAction.BeganSave, _coordinator.Observe(SaveObservation.Of(false, true, "save-a")));
        Assert.Equal(CatalogStatus.Loading, _service.State.Status);
        Assert.Equal(LifecycleAction.None, _coordinator.Observe(SaveObservation.Of(false, true, "save-a")));
        Assert.Equal(0, _source.Calls);

        Assert.Equal(LifecycleAction.Refreshed, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5)));
        Assert.Equal(CatalogStatus.Ready, _service.State.Status);
        Assert.Equal(LifecycleAction.None, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5)));
        Assert.Equal(1, _source.Calls);
    }

    [Fact]
    public void Already_loaded_save_is_discovered_immediately()
    {
        Assert.Equal(LifecycleAction.BeganSave, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5)));
        Assert.Equal(CatalogStatus.Ready, _service.State.Status);
        Assert.Equal(1, _source.Calls);
    }

    [Fact]
    public void Product_count_change_triggers_a_refresh()
    {
        _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5));
        var generation = _service.State.Snapshot!.Generation;

        Assert.Equal(LifecycleAction.Refreshed, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 6)));
        Assert.True(_service.State.Snapshot!.Generation > generation);
        Assert.Equal(LifecycleAction.None, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", null)));
    }

    [Fact]
    public void Switching_saves_starts_a_new_session()
    {
        _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5));
        var first = _service.State.Session;

        Assert.Equal(LifecycleAction.BeganSave, _coordinator.Observe(SaveObservation.Of(true, false, "save-b", 5)));
        Assert.NotEqual(first, _service.State.Session);
        Assert.Equal("save-b", _service.State.Session.SaveIdentity);
    }

    [Fact]
    public void Reloading_the_same_save_restarts_the_session()
    {
        _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5));
        var first = _service.State.Session;

        Assert.Equal(LifecycleAction.BeganSave, _coordinator.Observe(SaveObservation.Of(true, true, "save-a")));
        Assert.NotEqual(first, _service.State.Session);
        Assert.Null(_service.State.Snapshot);
    }

    [Fact]
    public void Returning_to_menu_ends_the_save()
    {
        _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5));

        Assert.Equal(LifecycleAction.EndedSave, _coordinator.Observe(SaveObservation.NoGame()));
        Assert.Equal(CatalogStatus.NoSave, _service.State.Status);
    }

    [Fact]
    public void Loaded_without_identity_fails_closed()
    {
        _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5));

        Assert.Equal(LifecycleAction.EndedSave, _coordinator.Observe(SaveObservation.Of(true, false, null, 5)));
        Assert.Null(_service.State.Snapshot);
    }

    [Fact]
    public void Unsupported_game_marks_catalog_unsupported_once()
    {
        Assert.Equal(LifecycleAction.MarkedUnsupported, _coordinator.Observe(SaveObservation.Unsupported("no LoadManager")));
        Assert.Equal(CatalogStatus.Unsupported, _service.State.Status);
        Assert.Equal(LifecycleAction.None, _coordinator.Observe(SaveObservation.Unsupported("no LoadManager")));
    }

    [Fact]
    public void Failed_discovery_is_retried_when_the_count_changes()
    {
        _source.Next = () => DiscoveryResult.Failure("not initialised yet");
        _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 0));
        Assert.Equal(CatalogStatus.Failed, _service.State.Status);
        Assert.Equal(LifecycleAction.None, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 0)));

        _source.Next = Standard;
        Assert.Equal(LifecycleAction.Refreshed, _coordinator.Observe(SaveObservation.Of(true, false, "save-a", 5)));
        Assert.Equal(CatalogStatus.Ready, _service.State.Status);
    }

    [Fact]
    public void Save_identity_is_stable_anonymised_and_path_insensitive()
    {
        var a = SaveIdentity.FromFolderPath(@"C:\Users\someone\AppData\LocalLow\Game\Saves\SaveGame_1");
        var b = SaveIdentity.FromFolderPath("c:/users/someone/appdata/locallow/game/saves/savegame_1/");
        var c = SaveIdentity.FromFolderPath(@"C:\Users\someone\AppData\LocalLow\Game\Saves\SaveGame_2");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("save-", a);
        Assert.Equal(17, a!.Length);
        Assert.DoesNotContain("someone", a);
        Assert.Null(SaveIdentity.FromFolderPath("  "));
        Assert.Null(SaveIdentity.FromFolderPath(null));
    }

    [Fact]
    public void Report_lists_counts_and_products_without_paths()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = Standard });
        service.Refresh(service.BeginSave("save-abc"));

        var lines = CatalogReport.Describe(service.State, PolicyConfiguration.Default, includeProducts: true);

        Assert.StartsWith("Product catalog: Ready", lines[0]);
        Assert.Contains(lines, l => l.Contains("[marijuana] 3 known, 2 unlocked") && l.Contains("2 eligible (Allowed)"));
        Assert.Contains(lines, l => l.Contains("granddaddypurple") && l.Contains("Locked"));
        Assert.Equal(5, lines.Count(l => l.StartsWith("    - ")));
    }

    [Fact]
    public void Report_for_not_ready_states_says_fail_closed()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource());
        service.BeginSave("save-abc");

        var lines = CatalogReport.Describe(service.State, PolicyConfiguration.Default, includeProducts: false);

        Assert.Contains(lines, l => l.Contains("fail-closed"));
    }
}
