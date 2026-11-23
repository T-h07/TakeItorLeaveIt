using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>In-memory store; can be told to fail writes.</summary>
internal sealed class MemoryPolicyStore : IPolicyStore
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public bool FailWrites { get; set; }

    public int Writes { get; private set; }

    public PolicyLoadResult Load(string saveIdentity)
    {
        if (!Files.TryGetValue(saveIdentity, out var json))
        {
            return new PolicyLoadResult(PolicyLoadStatus.NotFound, null, null);
        }

        try
        {
            return new PolicyLoadResult(PolicyLoadStatus.Loaded, PolicySettingsJson.FromJson(json), null);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            return new PolicyLoadResult(PolicyLoadStatus.Unreadable, null, ex.Message);
        }
    }

    public void Save(string saveIdentity, PolicySettings settings)
    {
        if (FailWrites)
        {
            throw new IOException("disk full (simulated)");
        }

        Writes++;
        Files[saveIdentity] = PolicySettingsJson.ToJson(settings);
    }
}

/// <summary>A catalog service + menu controller wired like the game does it.</summary>
internal sealed class MenuHarness
{
    public MenuHarness(Func<DiscoveryResult>? discovery = null, MemoryPolicyStore? store = null)
    {
        Source = new FakeDiscoverySource { Next = discovery ?? Standard };
        Catalog = new ProductCatalogService(Source);
        Store = store ?? new MemoryPolicyStore();
        Provider = new InMemoryPolicyProvider();
        Log = new RecordingLog();
        Menu = new ModMenuController(Catalog, Store, Provider, Log);
    }

    public FakeDiscoverySource Source { get; }

    public ProductCatalogService Catalog { get; }

    public MemoryPolicyStore Store { get; }

    public InMemoryPolicyProvider Provider { get; }

    public RecordingLog Log { get; }

    public ModMenuController Menu { get; }

    public MenuHarness Load(string save = "save-a")
    {
        Catalog.Refresh(Catalog.BeginSave(save));
        return this;
    }

    public IReadOnlyList<ProductRowView> Rows() => Menu.GetProductPage().Rows;

    public ProductRowView Row(ProductKey key) => Rows().Single(r => r.Key == key);

    public static DiscoveryResult Synthetic(int count)
        => Result(Enumerable.Range(0, count)
            .Select(i => Product(ProductKey.Create($"weed-{i:D4}"), Weed, i % 10 == 9 ? UnlockState.Locked : UnlockState.Unlocked,
                $"Strain {i:D4} {(i % 3 == 0 ? "Purple" : "Kush")}", $"effect{i % 7}"))
            .ToArray());
}
