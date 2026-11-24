using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 06 suite A: the safe write path shared by every mod file (fault injection).</summary>
public class SafeDocumentFileTests
{
    private const string Path1 = "root/saves/save-a.orders.json";

    private static DocumentCheck Check(string text) => ActiveOrderStore.Classify(text, 1, "save-a");

    private static string Doc(int marker) => "{\"schemaVersion\":1,\"save\":\"save-a\",\"orders\":[],\"marker\":" + marker + "}\n";

    [Fact]
    public void P06_A01_write_replaces_and_keeps_the_previous_valid_file_as_bak()
    {
        var fs = new FaultyFileSystem();

        SafeDocumentFile.Write(fs, Path1, Doc(1), Check);
        var outcome = SafeDocumentFile.Write(fs, Path1, Doc(2), Check);

        Assert.Equal(Doc(2), fs[Path1]);
        Assert.Equal(Doc(1), fs[Path1 + ".bak"]);
        Assert.True(outcome.BackupUpdated);
        Assert.Null(outcome.PreservedCorruptCopy);
        Assert.DoesNotContain(fs.Paths, p => p.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void P06_A02_a_document_that_would_not_read_back_is_never_written()
    {
        var fs = new FaultyFileSystem();
        SafeDocumentFile.Write(fs, Path1, Doc(1), Check);

        Assert.Throws<InvalidDataException>(() => SafeDocumentFile.Write(fs, Path1, "{\"orders\":[]}", Check));
        Assert.Throws<InvalidDataException>(() => SafeDocumentFile.Write(fs, Path1, string.Empty, Check));

        Assert.Equal(Doc(1), fs[Path1]);
    }

    [Fact]
    public void P06_A03_a_newer_schema_file_is_never_replaced()
    {
        var fs = new FaultyFileSystem();
        const string future = "{\"schemaVersion\":7,\"orders\":[],\"futureField\":true}";
        fs[Path1] = future;

        Assert.Throws<NewerSchemaException>(() => SafeDocumentFile.Write(fs, Path1, Doc(1), Check));

        Assert.Equal(future, fs[Path1]);
        Assert.Equal(new[] { Path1 }, fs.Paths);
    }

    [Fact]
    public void P06_A04_unreadable_files_are_kept_with_a_bounded_number_of_copies()
    {
        var fs = new FaultyFileSystem();
        var clock = new TestClock();
        for (var i = 0; i < 5; i++)
        {
            fs[Path1] = "{ truncated " + i;
            clock.Advance(1);
            var outcome = SafeDocumentFile.Write(fs, Path1, Doc(i), Check, () => clock.Utc);
            Assert.NotNull(outcome.PreservedCorruptCopy);
            Assert.False(outcome.BackupUpdated); // a damaged file never becomes the known-good backup
        }

        var copies = SafeDocumentFile.CorruptCopies(fs, Path1);
        Assert.Equal(SafeDocumentFile.MaxCorruptCopies, copies.Length);
        Assert.Equal(new[] { "{ truncated 2", "{ truncated 3", "{ truncated 4" }, copies.Select(c => fs[c]));
        Assert.Equal(Doc(4), fs[Path1]);
        Assert.False(fs.FileExists(Path1 + ".bak"));
    }

    [Fact]
    public void P06_A05_corrupt_copies_with_the_same_time_stamp_do_not_overwrite_each_other()
    {
        var fs = new FaultyFileSystem();
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs[Path1] = "x1";
        var first = SafeDocumentFile.PreserveCorrupt(fs, Path1, () => at);
        fs[Path1] = "x2";
        var second = SafeDocumentFile.PreserveCorrupt(fs, Path1, () => at);

        Assert.NotEqual(first, second);
        Assert.Equal("x1", fs[first]);
        Assert.Equal("x2", fs[second]);
        Assert.EndsWith(".corrupt-20260101T000000000Z", first, StringComparison.Ordinal);
    }

    [Fact]
    public void P06_A06_a_write_that_does_not_read_back_keeps_the_previous_file()
    {
        var fs = new FaultyFileSystem();
        SafeDocumentFile.Write(fs, Path1, Doc(1), Check);

        fs.CorruptWrites = true;
        Assert.Throws<IOException>(() => SafeDocumentFile.Write(fs, Path1, Doc(2), Check));

        Assert.Equal(Doc(1), fs[Path1]);
        Assert.False(fs.FileExists(Path1 + ".tmp"));
    }

    [Fact]
    public void P06_A07_a_crash_before_the_rename_leaves_the_previous_file_and_the_next_write_recovers()
    {
        var fs = new FaultyFileSystem();
        SafeDocumentFile.Write(fs, Path1, Doc(1), Check);

        fs.FailReplace = true;
        Assert.Throws<IOException>(() => SafeDocumentFile.Write(fs, Path1, Doc(2), Check));
        Assert.Equal(Doc(1), fs[Path1]);

        // A leftover temporary file from a hard crash is simply overwritten.
        fs[Path1 + ".tmp"] = "{ half";
        fs.FailReplace = false;
        SafeDocumentFile.Write(fs, Path1, Doc(3), Check);
        Assert.Equal(Doc(3), fs[Path1]);
        Assert.False(fs.FileExists(Path1 + ".tmp"));
    }

    [Fact]
    public void P06_A08_a_file_that_cannot_be_read_is_never_assumed_damaged_or_replaced()
    {
        var fs = new FaultyFileSystem();
        SafeDocumentFile.Write(fs, Path1, Doc(1), Check);
        fs.FailReadsOf.Add(Path1);

        Assert.Throws<IOException>(() => SafeDocumentFile.Write(fs, Path1, Doc(2), Check));

        fs.FailReadsOf.Clear();
        Assert.Equal(Doc(1), fs[Path1]);
        Assert.Empty(SafeDocumentFile.CorruptCopies(fs, Path1));
    }
}

/// <summary>Phase 06 suite B: ordered, coalescing writes (no stale snapshot can overwrite a newer one).</summary>
public class PersistenceQueueTests
{
    [Fact]
    public void P06_B01_an_older_generation_is_refused_after_a_newer_one_was_written()
    {
        var queue = new PersistenceQueue(() => 0);
        var file = new List<long>();

        Assert.True(queue.Submit("save-a|orders", 11, () => file.Add(11)));
        queue.RunPending();
        Assert.False(queue.Submit("save-a|orders", 10, () => file.Add(10)));
        Assert.False(queue.Submit("save-a|orders", 11, () => file.Add(11)));
        queue.RunPending();

        Assert.Equal(new long[] { 11 }, file);
        Assert.Equal(11, queue.CommittedGeneration("save-a|orders"));
    }

    [Fact]
    public void P06_B02_pending_writes_coalesce_to_the_newest_snapshot()
    {
        var queue = new PersistenceQueue(() => 0);
        var file = new List<long>();
        for (var g = 1; g <= 5; g++)
        {
            var generation = g;
            queue.Submit("save-a|orders", generation, () => file.Add(generation));
        }

        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(1, queue.RunPending());
        Assert.Equal(new long[] { 5 }, file);
    }

    [Fact]
    public void P06_B03_version_10_finishing_after_version_11_was_queued_never_overwrites_it()
    {
        // The spec's race: 10 starts writing, 11 is queued meanwhile, 10 completes. 11 must be the final content.
        var queue = new PersistenceQueue(() => 0);
        var content = 0L;
        queue.Submit("save-a|orders", 10, () =>
        {
            queue.Submit("save-a|orders", 11, () => content = 11);
            content = 10;
        });

        queue.RunPending();
        Assert.Equal(10, content);
        Assert.Equal(1, queue.PendingCount);
        queue.RunPending();
        Assert.Equal(11, content);
        Assert.False(queue.Submit("save-a|orders", 10, () => content = 10));
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void P06_B04_a_failed_write_stays_pending_and_is_retried_with_back_off()
    {
        var clock = new TestClock();
        var queue = new PersistenceQueue(() => clock.Seconds);
        var fail = true;
        var written = 0;
        queue.Submit("save-a|orders", 1, () =>
        {
            if (fail)
            {
                throw new IOException("disk full (simulated)");
            }

            written++;
        });

        Assert.Equal(0, queue.RunPending());
        Assert.Equal(1, queue.FailingCount);
        Assert.Contains("disk full", queue.LastError);
        Assert.Contains(queue.DrainMessages(), m => m.Contains("kept in memory and retried"));
        Assert.False(queue.HasDue);          // back-off
        Assert.Equal(0, queue.RunPending());

        clock.Advance(1.5);
        fail = false;
        Assert.True(queue.HasDue);
        Assert.Equal(1, queue.RunPending());
        Assert.Equal(1, written);
        Assert.Equal(0, queue.PendingCount);
        Assert.Contains(queue.DrainMessages(), m => m.Contains("written after 1 failed attempt"));
    }

    [Fact]
    public void P06_B05_flush_ignores_back_off_and_reports_what_is_left()
    {
        var clock = new TestClock();
        var queue = new PersistenceQueue(() => clock.Seconds);
        queue.Submit("save-a|orders", 1, () => throw new IOException("locked"));
        queue.RunPending();

        Assert.False(queue.Flush(100));
        Assert.Equal(2, queue.FailureCount);
        Assert.True(queue.HasPending("save-a|"));
        Assert.False(queue.HasPending("save-b|"));
    }

    [Fact]
    public async Task P06_B06_flush_is_bounded_while_another_runner_is_writing()
    {
        var queue = new PersistenceQueue();
        using var release = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        queue.Submit("save-a|orders", 1, () =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });

        var runner = Task.Run(() => queue.RunPending());
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        queue.Submit("save-a|alerts", 2, () => { });

        Assert.False(queue.Flush(50));
        Assert.Contains(queue.DrainMessages(), m => m.Contains("timed out"));

        release.Set();
        await runner.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(queue.Flush(5_000));
    }

    [Fact]
    public void P06_B07_concurrent_submitters_and_runners_always_end_on_the_newest_snapshot()
    {
        for (var round = 0; round < 20; round++)
        {
            var queue = new PersistenceQueue();
            var gate = new object();
            var files = new Dictionary<string, long>(StringComparer.Ordinal);
            var maxSubmitted = new Dictionary<string, long>(StringComparer.Ordinal);
            var generation = 0L;
            var random = new Random(round);
            var keys = new[] { "save-a|orders", "save-a|alerts", "save-b|orders" };
            var plan = Enumerable.Range(0, 400).Select(_ => keys[random.Next(keys.Length)]).ToArray();

            Parallel.ForEach(plan, new ParallelOptions { MaxDegreeOfParallelism = 4 }, key =>
            {
                var g = Interlocked.Increment(ref generation);
                if (queue.Submit(key, g, () =>
                    {
                        lock (gate)
                        {
                            // A write must never go backwards for its file.
                            Assert.True(!files.TryGetValue(key, out var current) || g > current, $"{key}: {g} after {current}");
                            files[key] = g;
                        }
                    }))
                {
                    lock (gate)
                    {
                        maxSubmitted[key] = Math.Max(maxSubmitted.TryGetValue(key, out var m) ? m : 0, g);
                    }
                }

                if (g % 7 == 0)
                {
                    queue.RunPending(timeoutMs: 0);
                }
            });

            Assert.True(queue.Flush(5_000));
            foreach (var key in maxSubmitted.Keys)
            {
                Assert.Equal(maxSubmitted[key], files[key]);
            }
        }
    }
}

/// <summary>Phase 06 suite C: per-save runtime files (orders, appointments, alerts) through <see cref="SalesPersistence"/>.</summary>
public class SalesPersistenceTests
{
    private static PlayerSaleCoordinator Coordinator(PolicyConfiguration? policy = null)
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(policy ?? PolicyConfiguration.Default), () => AllWeedUnlocked()) { Prices = Flat };
        coordinator.SetAuthority(true, 1);
        return coordinator;
    }

    [Fact]
    public void P06_C01_orders_round_trip_with_their_save_identity()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var coordinator = Coordinator();
        Assert.NotNull(files.Bind("save-a"));
        coordinator.BindSave("save-a", null);
        coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5)));

        Assert.Equal(1, files.Submit("save-a", ActiveOrderStore.ToJson(coordinator.Book.Orders, "save-a"), null, null));
        Assert.True(files.Queue.Flush(1_000));

        Assert.Contains("\"save\":\"save-a\"", fs[files.OrderStore.PathFor("save-a")].Replace(" ", string.Empty));
        var reloaded = new SalesPersistence("root", fs).Bind("save-a")!;
        Assert.Equal(OrdersLoadStatus.Loaded, reloaded.Status[SalesFile.Orders]);
        Assert.Equal(ContractA, reloaded.Orders.Single().Order.ContractId);
        Assert.Equal(OrdersLoadStatus.NotFound, reloaded.Status[SalesFile.Appointments]);
    }

    [Fact]
    public void P06_C02_a_stale_caller_cannot_write_into_another_profile()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        files.Bind("save-b");

        Assert.Equal(0, files.Submit("save-a", ActiveOrderStore.ToJson(Array.Empty<SaleTransaction>(), "save-a"), null, null));
        Assert.Equal(0, files.Submit(null!, "{}", null, null));
        Assert.Null(files.Bind("../escape"));
        Assert.Null(files.Identity);
        Assert.Equal(0, files.Submit("../escape", "{}", null, null));
        Assert.Empty(fs.Paths);
    }

    [Fact]
    public void P06_C03_a_pending_write_of_save_a_still_lands_in_save_a_after_switching_to_save_b()
    {
        // Phase 04 bug fixed here: one shared sequence number made save B's write skip save A's last write.
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var coordinator = Coordinator();

        files.Bind("save-a");
        coordinator.BindSave("save-a", null);
        coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5)));
        files.Submit("save-a", ActiveOrderStore.ToJson(coordinator.Book.Orders, "save-a"), null, null);

        files.Bind("save-b");
        coordinator.BindSave("save-b", null);
        coordinator.OnContractAccepted(Request(ContractB, (Gdp, 3)));
        files.Submit("save-b", ActiveOrderStore.ToJson(coordinator.Book.Orders, "save-b"), null, null);
        Assert.True(files.Queue.Flush(1_000));

        var a = new SalesPersistence("root", fs).Bind("save-a")!;
        var b = new SalesPersistence("root", fs).Bind("save-b")!;
        Assert.Equal(new[] { ContractA }, a.Orders.Select(o => o.Order.ContractId));
        Assert.Equal(new[] { ContractB }, b.Orders.Select(o => o.Order.ContractId));
    }

    [Fact]
    public void P06_C04_newer_schema_files_are_read_only_for_the_session()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var path = files.AppointmentStore.PathFor("save-a");
        const string future = "{\"schemaVersion\":4,\"appointments\":[{\"contract\":\"x\",\"newField\":1}]}";
        fs[path] = future;

        var load = files.Bind("save-a")!;

        Assert.Equal(OrdersLoadStatus.NewerSchema, load.Status[SalesFile.Appointments]);
        Assert.Empty(load.Appointments);
        Assert.True(files.IsReadOnly(SalesFile.Appointments));
        Assert.Equal(1, files.Submit("save-a", "{\"schemaVersion\":1,\"orders\":[]}", DealerOrderStore.ToJson(Array.Empty<DealerOrderRecord>(), "save-a"), null));
        Assert.True(files.Queue.Flush(1_000));
        Assert.Equal(future, fs[path]);
        Assert.Contains(files.StatusLines(), l => l.Contains("appointments newer version, kept read-only"));
    }

    [Fact]
    public void P06_C05_a_damaged_order_file_is_kept_as_a_copy_when_it_is_replaced()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var path = files.OrderStore.PathFor("save-a");
        fs[path] = "{\"schemaVersion\":1,\"orders\":[{\"contract\":";

        var load = files.Bind("save-a")!;
        Assert.Equal(OrdersLoadStatus.Unreadable, load.Status[SalesFile.Orders]);
        Assert.Contains(load.Issues, i => i.Contains("vanilla rules"));

        files.Submit("save-a", ActiveOrderStore.ToJson(Array.Empty<SaleTransaction>(), "save-a"), null, null);
        Assert.True(files.Queue.Flush(1_000));

        var copy = Assert.Single(SafeDocumentFile.CorruptCopies(fs, path));
        Assert.Equal("{\"schemaVersion\":1,\"orders\":[{\"contract\":", fs[copy]);
    }

    [Fact]
    public void P06_C06_a_file_copied_from_another_save_is_not_loaded()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        var foreign = ActiveOrderStore.ToJson(new[] { Transaction(ContractA) }, "save-b");
        fs[files.OrderStore.PathFor("save-a")] = foreign;

        var load = files.Bind("save-a")!;

        Assert.Equal(OrdersLoadStatus.Unreadable, load.Status[SalesFile.Orders]);
        Assert.Empty(load.Orders);
        Assert.Contains(load.Issues, i => i.Contains("different save"));
    }

    [Fact]
    public void P06_C07_files_of_older_builds_without_a_save_field_still_load()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        fs[files.OrderStore.PathFor("save-a")] = ActiveOrderStore.ToJson(new[] { Transaction(ContractA) });

        var load = files.Bind("save-a")!;

        Assert.Equal(OrdersLoadStatus.Loaded, load.Status[SalesFile.Orders]);
        Assert.Single(load.Orders);
    }

    [Fact]
    public void P06_C08_damaged_alert_history_is_recovered_only_from_a_verified_backup()
    {
        var fs = new FaultyFileSystem();
        var store = new DealerAlertStore("root", fs);
        var path = store.PathFor("save-a");
        var state = new DealerAlertState();
        state.Category(Dealers.Benji, Weed).OutOfStockSent = true;
        fs[path + ".bak"] = state.ToJson("save-a");
        fs[path] = "{ damaged";

        var issues = new List<string>();
        var recovered = store.Load("save-a", issues, out var status);
        Assert.Equal(OrdersLoadStatus.Unreadable, status);
        Assert.True(recovered.Categories.Single().OutOfStockSent); // no duplicate out-of-stock text after the restart
        Assert.Contains(issues, i => i.Contains("recovered from its verified backup"));

        fs[path + ".bak"] = state.ToJson("save-b"); // a backup of another save is never used
        issues.Clear();
        Assert.Empty(store.Load("save-a", issues, out _).Categories);
        Assert.Contains(issues, i => i.Contains("no alert history"));
    }

    [Fact]
    public void P06_C09_write_failures_are_reported_in_the_status_lines_and_retried()
    {
        var clock = new TestClock();
        var fs = new FaultyFileSystem { FailWrites = true };
        var files = new SalesPersistence("root", fs, new PersistenceQueue(() => clock.Seconds));
        files.Bind("save-a");
        files.Submit("save-a", ActiveOrderStore.ToJson(Array.Empty<SaleTransaction>(), "save-a"), null, null);

        files.Queue.RunPending();
        Assert.Contains(files.StatusLines(), l => l.Contains("1 failing") && l.Contains("disk full"));

        fs.FailWrites = false;
        clock.Advance(5);
        files.Queue.RunPending();
        Assert.Equal(0, files.Queue.PendingCount);
        Assert.True(fs.FileExists(files.OrderStore.PathFor("save-a")));
    }

    private static SaleTransaction Transaction(string contract)
    {
        var book = new ActiveOrderBook();
        book.TryAdd(new CategoryOrder(contract, Weed, 5, new[] { OgKush }, true, 1));
        return book.Orders.Single();
    }
}
