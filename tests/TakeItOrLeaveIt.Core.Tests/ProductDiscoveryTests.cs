using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group B: discovery, unlocks and catalog lifecycle.</summary>
public class ProductDiscoveryTests
{
    [Fact]
    public void Starts_without_a_save_and_with_no_snapshot()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource());

        Assert.Equal(CatalogStatus.NoSave, service.State.Status);
        Assert.Null(service.State.Snapshot);
    }

    [Fact]
    public void Loading_state_has_no_snapshot_even_though_nothing_is_unlocked_yet()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource());
        service.BeginSave("a");

        Assert.Equal(CatalogStatus.Loading, service.State.Status);
        Assert.Null(service.State.Snapshot);
        Assert.Equal(EligibilityReason.CatalogNotReady,
            SaleEligibilityPolicy.EvaluateCategory(PolicyConfiguration.Default, service.State.Snapshot, Weed).Reason);
    }

    [Fact]
    public void Unlocked_products_are_selectable_and_locked_are_not()
    {
        var snapshot = Snapshot(Standard());

        var unlocked = snapshot.GetUnlockedProducts(Weed).Keys().ToArray();
        Assert.Equal(new[] { OgKush, SourDiesel }, unlocked);
        Assert.True(snapshot.TryGetProduct(Gdp, out var gdp));
        Assert.Equal(UnlockState.Locked, gdp.UnlockState);
    }

    [Fact]
    public void Unknown_unlock_state_is_not_treated_as_unlocked()
    {
        var snapshot = Snapshot(Product(OgKush, Weed, UnlockState.Unknown));

        Assert.Empty(snapshot.GetUnlockedProducts(Weed));
        Assert.Equal(EligibilityReason.UnlockStateUnknown,
            SaleEligibilityPolicy.Evaluate(PolicyConfiguration.Default, snapshot, Weed, OgKush).Reason);
    }

    [Fact]
    public void New_unlock_appears_after_refresh_as_a_new_generation()
    {
        var source = new FakeDiscoverySource { Next = Standard };
        var service = new ProductCatalogService(source);
        var session = service.BeginSave("a");
        service.Refresh(session);
        var before = service.State.Snapshot!;

        source.Next = () => Result(Product(OgKush, Weed), Product(SourDiesel, Weed), Product(Gdp, Weed), Product(Meth1, Meth), Product(Coke1, Cocaine));
        Assert.Equal(RefreshOutcome.Published, service.Refresh(session));
        var after = service.State.Snapshot!;

        Assert.True(after.Generation > before.Generation);
        Assert.Contains(Gdp, after.GetUnlockedProducts(Weed).Keys());
        Assert.DoesNotContain(Gdp, before.GetUnlockedProducts(Weed).Keys()); // old snapshot is immutable
    }

    [Fact]
    public void Failed_refresh_exposes_no_stale_products()
    {
        var source = new FakeDiscoverySource { Next = Standard };
        var service = new ProductCatalogService(source);
        var session = service.BeginSave("a");
        service.Refresh(session);

        source.Next = () => DiscoveryResult.Failure("member missing");
        Assert.Equal(RefreshOutcome.Failed, service.Refresh(session));

        Assert.Equal(CatalogStatus.Failed, service.State.Status);
        Assert.Null(service.State.Snapshot);
        Assert.Equal("member missing", service.State.Message);
    }

    [Fact]
    public void Throwing_source_is_contained_and_reported_as_failure()
    {
        var log = new RecordingLog();
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = () => throw new InvalidOperationException("boom") }, log);
        var session = service.BeginSave("a");

        Assert.Equal(RefreshOutcome.Failed, service.Refresh(session));
        Assert.Equal(CatalogStatus.Failed, service.State.Status);
        Assert.Single(log.Errors);
    }

    [Fact]
    public void Unsupported_failure_maps_to_unsupported_status()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = () => DiscoveryResult.Failure("type missing", unsupported: true) });
        var session = service.BeginSave("a");
        service.Refresh(session);

        Assert.Equal(CatalogStatus.Unsupported, service.State.Status);
    }

    [Fact]
    public void Refresh_without_active_save_does_nothing()
    {
        var source = new FakeDiscoverySource();
        var service = new ProductCatalogService(source);

        Assert.Equal(RefreshOutcome.NoActiveSave, service.Refresh(default));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Ending_the_save_clears_the_catalog()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = Standard });
        service.Refresh(service.BeginSave("a"));
        service.EndSave();

        Assert.Equal(CatalogStatus.NoSave, service.State.Status);
        Assert.Null(service.State.Snapshot);
    }

    [Fact]
    public void State_changes_are_announced_and_faulty_handlers_are_contained()
    {
        var log = new RecordingLog();
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = Standard }, log);
        var seen = new List<CatalogStatus>();
        service.StateChanged += s => seen.Add(s.Status);
        service.StateChanged += _ => throw new InvalidOperationException("bad subscriber");

        service.Refresh(service.BeginSave("a"));

        Assert.Equal(new[] { CatalogStatus.Loading, CatalogStatus.Ready }, seen);
        Assert.Equal(2, log.Errors.Count);
    }

    [Fact]
    public void Conflicting_duplicate_ids_are_excluded_and_reported()
    {
        var snapshot = Snapshot(Product(OgKush, Weed), Product(OgKush, Meth), Product(SourDiesel, Weed));

        Assert.False(snapshot.TryGetProduct(OgKush, out _));
        Assert.True(snapshot.TryGetProduct(SourDiesel, out _));
        Assert.Contains(snapshot.Issues, i => i.Contains("ogkush", StringComparison.Ordinal));
    }

    [Fact]
    public void Identical_duplicates_are_merged_and_unlocked_wins_over_locked()
    {
        var snapshot = Snapshot(Product(OgKush, Weed, UnlockState.Locked), Product(OgKush, Weed, UnlockState.Unlocked));

        Assert.Equal(1, snapshot.ProductCount);
        Assert.True(snapshot.TryGetProduct(OgKush, out var p));
        Assert.True(p.IsUnlocked);
    }

    [Fact]
    public void Unknown_never_upgrades_a_duplicate()
    {
        var snapshot = Snapshot(Product(OgKush, Weed, UnlockState.Unknown), Product(OgKush, Weed, UnlockState.Unknown));

        Assert.True(snapshot.TryGetProduct(OgKush, out var p));
        Assert.Equal(UnlockState.Unknown, p.UnlockState);
    }

    [Fact]
    public void Categories_come_from_the_source_and_from_products()
    {
        var shrooms = CategoryKey.Create("shrooms");
        var snapshot = Snapshot(DiscoveryResult.Success(
            new[] { new CategoryDescriptor(Weed) },
            new[] { Product(ProductKey.Create("shroom-1"), shrooms) }));

        Assert.True(snapshot.HasCategory(Weed));
        Assert.True(snapshot.HasCategory(shrooms));
        Assert.Equal(new[] { Weed, shrooms }.OrderBy(c => c), snapshot.Categories.Select(c => c.Key));
    }

    [Fact]
    public void Empty_successful_discovery_is_ready_with_zero_products()
    {
        var snapshot = Snapshot(Result());

        Assert.Equal(0, snapshot.ProductCount);
        Assert.Equal(EligibilityReason.NoEligibleProducts,
            SaleEligibilityPolicy.EvaluateCategory(PolicyConfiguration.Default, snapshot, Weed).Reason);
    }
}
