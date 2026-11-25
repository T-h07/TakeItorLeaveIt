using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using Xunit.Abstractions;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 08 hardening, suite V: persistence of damaged or hostile files.</summary>
public sealed class PersistenceHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tioli-p08-" + Guid.NewGuid().ToString("N"));

    public PersistenceHardeningTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string OrderJson(Action<Dictionary<string, object?>> change)
    {
        var book = new ActiveOrderBook();
        book.TryAdd(new CategoryOrder(ContractA, Weed, 5, new[] { OgKush, Gdp }, true, 3));
        var root = (Dictionary<string, object?>)MiniJson.Parse(ActiveOrderStore.ToJson(book.Orders, "save-a"))!;
        var entry = root.Values.OfType<List<object?>>().Single().OfType<Dictionary<string, object?>>().Single();
        change(entry);
        return MiniJson.Write(root);
    }

    [Fact]
    public void P08_V01_a_damaged_mixed_delivery_flag_is_made_stricter_with_an_issue()
    {
        var json = OrderJson(e =>
        {
            e.Remove("mixing");
            e["mixedDeliveries"] = "yes";
        });
        var issues = new List<string>();

        var order = Assert.Single(ActiveOrderStore.FromJson(json, issues)).Order;

        Assert.False(order.MixedDeliveries);
        Assert.Contains(issues, i => i.Contains("mixed-delivery flag", StringComparison.Ordinal));
    }

    [Fact]
    public void P08_V02_an_absent_or_valid_mixed_delivery_flag_keeps_its_meaning()
    {
        var absent = OrderJson(e =>
        {
            e.Remove("mixing");
            e.Remove("mixedDeliveries");
        });
        var off = OrderJson(e =>
        {
            e.Remove("mixing");
            e["mixedDeliveries"] = false;
        });
        var issues = new List<string>();

        Assert.True(Assert.Single(ActiveOrderStore.FromJson(absent, issues)).Order.MixedDeliveries);
        Assert.False(Assert.Single(ActiveOrderStore.FromJson(off, issues)).Order.MixedDeliveries);
        Assert.DoesNotContain(issues, i => i.Contains("mixed-delivery flag", StringComparison.Ordinal));
    }

    [Fact]
    public void P08_V03_an_oversized_document_is_not_read_and_is_left_on_disk()
    {
        var fs = new PhysicalSettingsFileSystem(maxDocumentBytes: 1_024);
        var path = Path.Combine(_dir, "big.json");
        File.WriteAllText(path, "{\"x\":\"" + new string('a', 2_000) + "\"}");
        var before = File.ReadAllBytes(path);

        var ex = Assert.Throws<DocumentTooLargeException>(() => fs.ReadAllText(path));

        Assert.IsAssignableFrom<IOException>(ex);
        Assert.Equal(1_024, ex.Limit);
        Assert.DoesNotContain(_dir, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void P08_V04_a_document_at_the_limit_is_read_normally_with_or_without_a_bom()
    {
        var fs = new PhysicalSettingsFileSystem(maxDocumentBytes: 64);
        var path = Path.Combine(_dir, "ok.json");
        File.WriteAllText(path, "{\"a\":\"é\"}", new System.Text.UTF8Encoding(true));
        Assert.Equal("{\"a\":\"é\"}", fs.ReadAllText(path));

        File.WriteAllText(path, new string(' ', 62) + "{}", new System.Text.UTF8Encoding(false));
        Assert.Equal(64, new FileInfo(path).Length);
        Assert.EndsWith("{}", fs.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(PhysicalSettingsFileSystem.DefaultMaxDocumentBytes, PhysicalSettingsFileSystem.Instance.MaxDocumentBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicalSettingsFileSystem(0));
    }

    [Fact]
    public void P08_V05_an_oversized_settings_file_loads_as_unreadable_without_a_path_and_is_preserved()
    {
        var fs = new PhysicalSettingsFileSystem(maxDocumentBytes: 512);
        var store = new JsonPolicyStore(_dir, fs);
        var path = store.PathFor("save-a");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string(' ', 4_096));

        var result = store.Load("save-a");

        Assert.Equal(PolicyLoadStatus.Unreadable, result.Status);
        Assert.DoesNotContain(_dir, result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4_096, new FileInfo(path).Length);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(path)!), f => f != path);
    }
}
