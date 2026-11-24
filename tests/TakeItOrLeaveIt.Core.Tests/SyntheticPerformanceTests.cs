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

/// <summary>
/// Phase 08 hardening, suite W: deterministic SYNTHETIC workloads. They assert work counts and results, never wall-clock
/// thresholds; the elapsed times they print are for docs/PERFORMANCE.md only (this machine, Release, not the game).
/// </summary>
public sealed class SyntheticPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public SyntheticPerformanceTests(ITestOutputHelper output) => _output = output;

    private void Report(string name, int iterations, Stopwatch watch, long allocatedBytes)
        => _output.WriteLine(FormattableString.Invariant(
            $"SYNTHETIC {name}: {iterations} iterations, {watch.Elapsed.TotalMilliseconds / iterations * 1000:0.0} us/op, {allocatedBytes / iterations} B/op"));

    [Fact]
    public void P08_W01_handover_validation_of_a_full_screen()
    {
        var order = Order(40, true, OgKush, Gdp, SourDiesel);
        var catalog = AllWeedUnlocked();
        var items = Enumerable.Range(0, 8).Select(i => (DeliveredItem?)Item(i.ToString(System.Globalization.CultureInfo.InvariantCulture), i % 2 == 0 ? OgKush : Gdp, 5)).ToList();
        const int iterations = 20_000;
        DeliveryValidator.Validate(order, items, catalog); // warm-up

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        DeliveryValidation? last = null;
        for (var i = 0; i < iterations; i++)
        {
            last = DeliveryValidator.Validate(order, items, catalog);
        }

        watch.Stop();
        Report("validate-8-stacks", iterations, watch, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(last!.IsValid);
        Assert.Equal(40, last.DeliveredUnits);
    }

    [Fact]
    public void P08_W02_dealer_reconcile_with_nothing_changed_is_a_no_op()
    {
        var h = new DealerHarness().Stock(Dealers.Benji, OgKush, 500).Stock(Dealers.Benji, Gdp, 500);
        for (var n = 0; n < 20; n++)
        {
            h.Accept(Dealers.Contract(100 + n), Dealers.Benji, 10, (OgKush, 5));
        }

        var stock = h.StockOf(Dealers.Benji);
        h.Coordinator.Reconcile(stock);
        const int iterations = 5_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        ReconcileReport? report = null;
        for (var i = 0; i < iterations; i++)
        {
            report = h.Coordinator.Reconcile(stock);
        }

        watch.Stop();
        Report("reconcile-20-appointments-unchanged", iterations, watch, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(report!.Changed);
        Assert.Empty(h.Coordinator.CheckInvariants());
    }

    [Fact]
    public void P08_W03_log_redaction_cost_per_entry()
    {
        const string message = @"Could not write save-a (attempt 2; kept in memory and retried): IOException: The process cannot access the file 'C:\Users\someone\Games\Schedule I\UserData\TakeItOrLeaveIt\saves\save-a.orders.json' because it is being used by another process.";
        const int iterations = 20_000;
        LogText.Entry(message);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var last = string.Empty;
        for (var i = 0; i < iterations; i++)
        {
            last = LogText.Entry(message);
        }

        watch.Stop();
        Report("log-entry-redaction", iterations, watch, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.DoesNotContain("someone", last, StringComparison.Ordinal);
    }

    [Fact]
    public void P08_W05_log_entry_without_a_path()
    {
        const string message = "Dealer order c0ffee00-0000-4000-8000-00000000000c: removed 20 unit(s) (2 x ogkush) from dealer benji_coleman; payment, cut and XP stay vanilla.";
        const int iterations = 20_000;
        LogText.Entry(message);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var last = string.Empty;
        for (var i = 0; i < iterations; i++)
        {
            last = LogText.Entry(message);
        }

        watch.Stop();
        Report("log-entry-no-path", iterations, watch, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(message, last);
    }

    [Fact]
    public void P08_W04_pending_return_retry_touches_each_item_once_per_pass()
    {
        var queue = new PendingReturnQueue<object>();
        for (var i = 0; i < PendingReturnQueue<object>.MaxItemsPerOwner; i++)
        {
            queue.Add(Dealers.Benji, new object(), 1);
        }

        var calls = 0;
        queue.Retry(Dealers.Benji, _ => { calls++; return false; });
        Assert.Equal(PendingReturnQueue<object>.MaxItemsPerOwner, calls);

        calls = 0;
        queue.Retry(Dealers.Molly, _ => { calls++; return true; });
        Assert.Equal(0, calls);
    }
}
