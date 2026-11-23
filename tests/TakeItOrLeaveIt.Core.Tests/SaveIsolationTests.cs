using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Test group F: save transitions.</summary>
public class SaveIsolationTests
{
    private static DiscoveryResult SaveA() => Result(Product(OgKush, Weed), Product(SourDiesel, Weed));

    private static DiscoveryResult SaveB() => Result(Product(Gdp, Weed), Product(OgKush, Weed));

    [Fact]
    public void Loading_save_A_produces_save_A_catalog()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = SaveA });
        var a = service.BeginSave("A");
        service.Refresh(a);

        Assert.Equal("A", service.State.Snapshot!.Session.SaveIdentity);
        Assert.Equal(new[] { OgKush, SourDiesel }, service.State.Snapshot.GetUnlockedProducts(Weed).Keys());
    }

    [Fact]
    public void Loading_save_B_replaces_save_A_without_merging()
    {
        var source = new FakeDiscoverySource { Next = SaveA };
        var service = new ProductCatalogService(source);
        service.Refresh(service.BeginSave("A"));

        source.Next = SaveB;
        var b = service.BeginSave("B");
        Assert.Null(service.State.Snapshot); // nothing from A survives the switch
        service.Refresh(b);

        var unlocked = service.State.Snapshot!.GetUnlockedProducts(Weed).Keys().ToArray();
        Assert.Equal(new[] { Gdp, OgKush }, unlocked);
        Assert.DoesNotContain(SourDiesel, unlocked);
        Assert.Equal("B", service.State.Session.SaveIdentity);
    }

    [Fact]
    public void Stale_result_from_save_A_cannot_overwrite_save_B()
    {
        var source = new FakeDiscoverySource { Next = SaveB };
        var service = new ProductCatalogService(source);
        var a = service.BeginSave("A");
        var capturedForA = SaveA();          // discovery for A was captured...
        var b = service.BeginSave("B");      // ...but the player switched to B before it was applied
        service.Refresh(b);

        Assert.Equal(RefreshOutcome.StaleDiscarded, service.Publish(a, capturedForA));
        Assert.Equal(b, service.State.Session);
        Assert.DoesNotContain(SourDiesel, service.State.Snapshot!.GetUnlockedProducts(Weed).Keys());
    }

    [Fact]
    public void Stale_result_during_loading_does_not_make_the_catalog_ready()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = SaveA });
        var a = service.BeginSave("A");
        service.BeginSave("B");

        Assert.Equal(RefreshOutcome.StaleDiscarded, service.Publish(a, SaveA()));
        Assert.Equal(CatalogStatus.Loading, service.State.Status);
    }

    [Fact]
    public void Result_after_returning_to_menu_is_ignored()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = SaveA });
        var a = service.BeginSave("A");
        service.EndSave();

        Assert.Equal(RefreshOutcome.NoActiveSave, service.Publish(a, SaveA()));
        Assert.Equal(CatalogStatus.NoSave, service.State.Status);
    }

    [Fact]
    public void Reloading_the_same_save_gets_a_new_session()
    {
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = SaveA });
        var first = service.BeginSave("A");
        var second = service.BeginSave("A");

        Assert.NotEqual(first, second);
        Assert.Equal(RefreshOutcome.StaleDiscarded, service.Publish(first, SaveA()));
    }

    [Fact]
    public void Catalog_failure_does_not_authorize_products()
    {
        var source = new FakeDiscoverySource { Next = SaveA };
        var service = new ProductCatalogService(source);
        var a = service.BeginSave("A");
        service.Refresh(a);
        source.Next = () => DiscoveryResult.Failure("broken");
        service.Refresh(a);

        var decision = SaleEligibilityPolicy.Evaluate(PolicyConfiguration.Default, service.State.Snapshot, Weed, OgKush);
        Assert.Equal(EligibilityReason.CatalogNotReady, decision.Reason);
    }

    [Fact]
    public void Selections_from_another_save_cannot_bypass_the_active_save_unlocks()
    {
        // Player selected Sour Diesel while playing save A; save B has not unlocked it.
        var policy = PolicyConfiguration.Default.WithCategory(Weed, CategoryPolicy.SelectedOnly(SourDiesel, OgKush));
        var service = new ProductCatalogService(new FakeDiscoverySource { Next = SaveB });
        service.Refresh(service.BeginSave("B"));
        var snapshot = service.State.Snapshot;

        Assert.Equal(EligibilityReason.ProductUnknown, SaleEligibilityPolicy.Evaluate(policy, snapshot, Weed, SourDiesel).Reason);
        var summary = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, Weed);
        Assert.Equal(new[] { OgKush }, summary.EligibleProducts.Keys());
        Assert.Equal(new[] { SourDiesel }, summary.InactiveSelections);
    }
}
