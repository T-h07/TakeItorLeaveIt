using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using Xunit.Abstractions;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Suite E: persistence, reload reconciliation, save isolation, diagnostics and simulated stress.</summary>
public class DealerRecoveryTests
{
    private readonly ITestOutputHelper _output;

    public DealerRecoveryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void P04_T41_appointments_round_trip_without_reservations()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Accept(ContractB, 100f, (OgKush, 50));
        var fs = new MemoryFileSystem();
        var store = new DealerOrderStore("root", fs);

        store.Save("save-a", h.Coordinator.Records());
        var json = fs.Files[store.PathFor("save-a")];
        var loaded = store.Load("save-a");

        Assert.Equal(OrdersLoadStatus.Loaded, loaded.Status);
        Assert.Equal(new[] { (ContractA, Benji, false), (ContractB, Benji, false) }, loaded.Records.Select(r => (r.Order.ContractId, r.DealerId, r.Committed)));
        Assert.Equal(h.Coordinator.Find(ContractA)!.Order.AllowedProducts, loaded.Records[0].Order.AllowedProducts);
        Assert.DoesNotContain("reserv", json, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".dealer-orders.json", store.PathFor("save-a"));
    }

    [Fact]
    public void P04_T42_reload_rebuilds_reservations_from_live_stock_only()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Accept(ContractB, 100f, (OgKush, 5));
        var records = h.Coordinator.Records();

        // The game was saved after the dealer lost 7 units: only one appointment can be served after the reload.
        h.Take(Benji, OgKush, 7);
        h.Coordinator.BindSave("save-test", records);

        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
        Assert.Equal(2, h.Coordinator.WaitingAppointments);
        var report = h.Tick(Benji);
        Assert.Empty(report.Reserved); // 3 units cannot cover 5
        h.Stock(Benji, SourDiesel, 2);
        Assert.Equal(new[] { ContractA }, h.Tick(Benji).Reserved);
        Assert.Empty(h.Coordinator.CheckInvariants());
        Assert.True(h.Coordinator.Ledger.HeldUnitsTotal <= h.TotalUnits(Benji));
    }

    [Fact]
    public void P04_T43_damaged_files_never_create_appointments()
    {
        var fs = new MemoryFileSystem();
        var store = new DealerOrderStore("root", fs);
        var path = store.PathFor("save-a");

        fs.Files[path] = "{ not json";
        Assert.Equal(OrdersLoadStatus.Unreadable, store.Load("save-a").Status);

        fs.Files[path] = "{\"schemaVersion\":99,\"appointments\":[{\"contract\":\"x\"}]}";
        var newer = store.Load("save-a");
        Assert.Empty(newer.Records);
        Assert.Contains(newer.Issues, i => i.Contains("newer"));

        var good = DealerOrderStore.ToJson(new[] { new DealerOrderRecord(Order(ContractA, 5), Benji, 1, false) });
        fs.Files[path] = good.Replace("\"" + Benji + "\"", "\"bad dealer/id\"");
        var badDealer = store.Load("save-a");
        Assert.Empty(badDealer.Records);
        Assert.Contains(badDealer.Issues, i => i.Contains("no valid dealer"));

        Assert.Equal(OrdersLoadStatus.NotFound, store.Load("save-b").Status);
    }

    [Fact]
    public void P04_T44_save_switching_isolates_appointments()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        var saveA = h.Coordinator.Records();

        h.Coordinator.BindSave("save-b", null);
        Assert.Equal(0, h.Coordinator.ActiveAppointments);
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
        Assert.Null(h.Coordinator.Find(ContractA));

        h.Coordinator.BindSave("save-a", saveA);
        Assert.NotNull(h.Coordinator.Find(ContractA));
    }

    [Fact]
    public void P04_T45_prune_drops_vanished_contracts_and_their_holds()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Accept(ContractB, 100f, (OgKush, 5));

        var removed = h.Coordinator.Prune(new[] { ContractB, null });

        Assert.Equal(1, removed);
        Assert.Null(h.Coordinator.Find(ContractA));
        Assert.Equal(5, h.Coordinator.Ledger.HeldUnitsTotal);
        Assert.Empty(h.Coordinator.CheckInvariants());
    }

    [Fact]
    public void P04_T46_status_reports_only_what_is_really_active()
    {
        var status = new DealerSalesStatus();
        Assert.Equal("Unavailable", status.Headline);

        status.AppointmentHook = status.FulfilmentHook = status.ReconcileHook = ComponentStatus.Active;
        status.CatalogReady = status.PolicyLoaded = status.Authoritative = true;
        Assert.Equal("Disabled", status.Headline);

        status.ModEnabledForSave = true;
        status.ActiveAppointments = 3;
        status.WaitingAppointments = 1;
        status.Reservations = 2;
        status.ReservedUnits = 9;
        status.LastError = "boom";
        var lines = status.Lines();

        Assert.Equal("Dealer sales: Active", lines[0]);
        Assert.Contains("Managed dealer appointments: 3", lines);
        Assert.Contains("Waiting for eligible stock: 1", lines);
        Assert.Contains("Stock reservations: 2 (9 unit(s))", lines);
        Assert.Contains("Last dealer error: boom", lines);

        status.Authoritative = false;
        Assert.Equal("Inactive (unsupported session)", status.Headline);
    }

    /// <summary>Simulated: thousands of random interleavings across three dealers; every invariant holds after every step.</summary>
    [Fact]
    public void P04_T47_random_interleavings_keep_every_invariant()
    {
        var random = new Random(4242);
        var dealers = new[] { Benji, Molly, "uncle_nelson" };
        var products = new[] { OgKush, SourDiesel, Gdp, Meth1 };
        var h = new DealerHarness();
        var active = new List<string>();
        var next = 1;
        long settledUnits = 0;

        for (var step = 0; step < 3000; step++)
        {
            var dealer = dealers[random.Next(dealers.Length)];
            switch (random.Next(7))
            {
                case 0:
                    h.Stock(dealer, products[random.Next(products.Length)], random.Next(1, 8));
                    break;
                case 1:
                    h.Take(dealer, products[random.Next(products.Length)], random.Next(1, 8));
                    break;
                case 2:
                    var id = Contract(next++);
                    if (h.Accept(id, dealer, 50f, (OgKush, random.Next(1, 10))).IsManaged)
                    {
                        active.Add(id);
                    }

                    break;
                case 3:
                    h.Tick(dealer);
                    break;
                case 4 when active.Count > 0:
                    var contract = active[random.Next(active.Count)];
                    var result = h.Handover(contract, customerAccepts: random.Next(5) > 0);
                    if (result.Settled)
                    {
                        settledUnits += result.Items.Sum(i => i.Units);
                        active.Remove(contract);
                    }

                    break;
                case 5 when active.Count > 0:
                    var ended = active[random.Next(active.Count)];
                    h.End(ended, (SaleEnd)random.Next(4));
                    active.Remove(ended);
                    break;
                case 6:
                    h.Policies.Update(p => p.WithMixedDeliveries(random.Next(2) == 0));
                    break;
            }

            // Holds are checked against the stock the ledger last saw (stock may leave between ticks) ...
            var problems = h.Coordinator.CheckInvariants();
            Assert.True(problems.Count == 0, $"step {step}: {string.Join("; ", problems)}");
            if (step % 50 == 0)
            {
                // ... and after every dealer reconciled, against the live inventory: never more promised than held.
                foreach (var d in dealers)
                {
                    h.Tick(d);
                    foreach (var p in products)
                    {
                        Assert.True(h.Coordinator.Ledger.HeldUnits(d, p) <= h.UnitsOf(d, p), $"step {step}: {d}/{p} over-reserved");
                    }
                }
            }
        }

        Assert.Equal(h.Completions, h.Payments);
        Assert.Equal(h.Completions, h.XpAwards);
        Assert.Equal(h.Payments * 50f * DealerHarness.CutRate, h.DealerCash, 1);
        _output.WriteLine($"Simulated: {next - 1} contracts, {h.Completions} settled, {settledUnits} units delivered, {h.Coordinator.ActiveAppointments} still open.");
    }

    /// <summary>Simulated benchmark: 50 dealers with 20 waiting appointments each.</summary>
    [Fact]
    public void P04_T48_reconcile_stays_cheap_at_scale()
    {
        var h = new DealerHarness();
        var dealers = Enumerable.Range(0, 50).Select(i => $"dealer_{i:D2}").ToArray();
        var n = 0;
        foreach (var dealer in dealers)
        {
            for (var i = 0; i < 20; i++)
            {
                h.Accept(Contract(++n), dealer, 10f, (OgKush, 3));
            }
        }

        var watch = Stopwatch.StartNew();
        foreach (var dealer in dealers)
        {
            h.Stock(dealer, OgKush, 200);
        }

        var rounds = 0;
        for (; rounds < 5; rounds++)
        {
            foreach (var dealer in dealers)
            {
                h.Tick(dealer);
            }
        }

        watch.Stop();
        _output.WriteLine($"Simulated: {rounds} reconcile rounds over {dealers.Length} dealers / {n} appointments in {watch.ElapsedMilliseconds} ms.");
        Assert.True(watch.ElapsedMilliseconds < 5_000);
        Assert.Empty(h.Coordinator.CheckInvariants());
    }
}
