using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 06 suite H: dealer appointment recovery, quality-lot reservations after reload, message dedup across restarts,
/// transaction idempotency, stale callbacks, reset, and a randomised fault-injection stress run. Simulation only:
/// these prove the Core rules, not the game (see PHASE_06_TESTING.md for the Phase 08 checks).
/// </summary>
public class PersistenceRecoveryTests
{
    private static DealerStock LotStock(string dealer, params (ProductKey P, int U, QualityTier? Q)[] stacks)
        => new(dealer, stacks.Select((s, i) => new InventoryStack($"slot:{i}", s.P, s.U, s.Q)));

    private static DealerSaleCoordinator DealerCoordinator()
    {
        var coordinator = new DealerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Flat };
        coordinator.SetAuthority(true, 1);
        return coordinator;
    }

    private static PlayerSaleCoordinator PlayerCoordinator()
    {
        var coordinator = new PlayerSaleCoordinator(new InMemoryPolicyProvider(), () => AllWeedUnlocked()) { Prices = Flat };
        coordinator.SetAuthority(true, 1);
        return coordinator;
    }

    [Fact]
    public void P06_H01_reloaded_appointments_reserve_only_lots_that_meet_their_saved_minimum()
    {
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        files.Bind("save-a");
        var order = new CategoryOrder(ContractC, Weed, 5, new[] { OgKush, SourDiesel }, true, 1, mixing: MixingMode.AnyPermitted, minimumQuality: QualityTier.Premium);
        files.Submit("save-a", null, DealerOrderStore.ToJson(new[] { new DealerOrderRecord(order, Benji, 1, false) }, "save-a"), null);
        Assert.True(files.Queue.Flush(1_000));

        var load = new SalesPersistence("root", fs).Bind("save-a")!;
        var coordinator = DealerCoordinator();
        coordinator.BindSave("save-a", load.Appointments);
        Assert.Equal(QualityTier.Premium, coordinator.Find(ContractC)!.Order.MinimumQuality);
        Assert.Equal(0, coordinator.Ledger.ActiveCount); // nothing held until live stock is seen

        // Only 4 premium units: not enough, and the poor lot is never used to make up the difference.
        var stock = LotStock(Benji, (OgKush, 10, QualityTier.Poor), (OgKush, 4, QualityTier.Premium), (SourDiesel, 3, null));
        Assert.Empty(coordinator.Reconcile(stock).Reserved);

        var restocked = LotStock(Benji, (OgKush, 10, QualityTier.Poor), (OgKush, 4, QualityTier.Premium), (SourDiesel, 2, QualityTier.Heavenly));
        Assert.Equal(new[] { ContractC }, coordinator.Reconcile(restocked).Reserved);
        Assert.Equal(0, coordinator.Ledger.HeldUnits(Benji, new StockLot(OgKush, QualityTier.Poor)));
        Assert.Equal(4, coordinator.Ledger.HeldUnits(Benji, new StockLot(OgKush, QualityTier.Premium)));
        Assert.Equal(1, coordinator.Ledger.HeldUnits(Benji, new StockLot(SourDiesel, QualityTier.Heavenly)));
        Assert.Empty(coordinator.CheckInvariants());
    }

    [Fact]
    public void P06_H02_a_handover_committed_before_the_reload_is_never_paid_or_removed_twice()
    {
        var coordinator = DealerCoordinator();
        var records = new[] { new DealerOrderRecord(Order(ContractC, 5), Benji, 1, committed: true) };
        var json = DealerOrderStore.ToJson(records, "save-a");
        var restored = DealerOrderStore.FromJson(json, new List<string>());
        coordinator.BindSave("save-a", restored);

        var stock = StockOf(Benji, (OgKush, 20));
        coordinator.Reconcile(stock);
        Assert.Equal(0, coordinator.Ledger.ActiveCount);                                  // no stock promised again
        Assert.Equal(HandoverDecision.NothingToGive, coordinator.PrepareHandover(ContractC, stock).Decision);
        Assert.Equal(CommitObservation.Duplicate, coordinator.OnHandoverProcessed(ContractC));
        Assert.Equal(HandoverCompletion.Ignored, coordinator.CompleteHandover(ContractC, 5));
    }

    [Fact]
    public void P06_H03_a_player_commit_reported_again_after_a_reload_is_a_duplicate()
    {
        var coordinator = PlayerCoordinator();
        coordinator.BindSave("save-a", null);
        coordinator.OnContractAccepted(Request(ContractA, (OgKush, 5)));
        Assert.Equal(CommitObservation.First, coordinator.OnHandoverProcessed(ContractA));
        var json = ActiveOrderStore.ToJson(coordinator.Book.Orders, "save-a");

        var reloaded = PlayerCoordinator();
        reloaded.BindSave("save-a", ActiveOrderStore.FromJson(json, new List<string>()));

        Assert.Equal(SaleState.CommitObserved, reloaded.Book.Orders.Single().State);
        Assert.Equal(CommitObservation.Duplicate, reloaded.OnHandoverProcessed(ContractA));
    }

    [Fact]
    public void P06_H04_callbacks_for_the_previous_save_do_nothing_in_the_next_one()
    {
        var dealers = DealerCoordinator();
        dealers.BindSave("save-a", new[] { new DealerOrderRecord(Order(ContractC, 5), Benji, 1, false) });
        var players = PlayerCoordinator();
        players.BindSave("save-a", null);
        players.OnContractAccepted(Request(ContractA, (OgKush, 5)));

        dealers.BindSave("save-b", null);
        players.BindSave("save-b", null);
        var stock = StockOf(Benji, (OgKush, 20));

        Assert.Equal(HandoverDecision.Vanilla, dealers.PrepareHandover(ContractC, stock).Decision);
        Assert.Equal(HandoverCompletion.Ignored, dealers.CompleteHandover(ContractC, 5));
        Assert.Equal(CommitObservation.NotManaged, dealers.OnHandoverProcessed(ContractC));
        Assert.Null(dealers.OnContractEnded(ContractC, SaleEnd.Completed));
        Assert.Null(players.Validate(ContractA, new[] { Item("0", OgKush, 5) }));
        Assert.Null(players.OnContractEnded(ContractA, SaleEnd.Completed));
        Assert.Equal(0, dealers.Ledger.ActiveCount);
        Assert.Empty(players.Book.Orders);
    }

    [Fact]
    public void P06_H05_an_out_of_stock_text_is_not_sent_again_after_a_restart()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 5)));
        var first = rig.Check(StockOf(Benji));
        Assert.Single(first, a => a.Type == DealerAlertType.OutOfStock);

        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs);
        files.Bind("save-a");
        files.Submit("save-a", null, null, rig.Notifier.State.ToJson("save-a"));
        Assert.True(files.Queue.Flush(1_000));

        // Restart: a new coordinator bound to the stored history; the dealer is still empty.
        var restarted = new MessagingRig();
        var load = new SalesPersistence("root", fs).Bind("save-a")!;
        restarted.Notifier.BindSave("save-a", load.Alerts);
        restarted.Settle();
        Assert.Empty(restarted.Check(StockOf(Benji)));

        // A real restock re-arms it, so a later empty stock is a new episode.
        restarted.Check(StockOf(Benji, (OgKush, 3)));
        Assert.Single(restarted.Check(StockOf(Benji)), a => a.Type == DealerAlertType.OutOfStock);
    }

    [Fact]
    public void P06_H06_reset_to_defaults_keeps_every_backup()
    {
        const string schema1 = "{\"schemaVersion\":1,\"enabled\":true,\"categories\":{\"marijuana\":{\"mode\":\"selectedOnly\",\"selectedProducts\":[\"ogkush\"]}}}";
        var h = new DiskMenuHarness();
        var path = h.PathOf("save-a");
        h.Fs[path] = schema1;
        h.Load("save-a");
        Assert.True(h.Menu.Save());
        var migrated = h.Fs[path];

        h.Menu.RequestReset();
        Assert.True(h.Menu.ConfirmReset());
        Assert.True(h.Menu.Save());

        Assert.Equal(schema1, h.Fs[path + ".schema1.bak"]);
        Assert.Equal(migrated, h.Fs[path + ".bak"]);                 // the pre-reset rules stay recoverable
        Assert.Equal(PolicyMode.AnyUnlocked, h.Provider.Current.GetEffectivePolicy(Weed).Mode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void P06_H07_randomised_fault_injection_never_tears_mixes_or_loses_the_last_write(int seed)
    {
        var random = new Random(seed);
        var clock = new TestClock();
        var fs = new FaultyFileSystem();
        var files = new SalesPersistence("root", fs, new PersistenceQueue(() => clock.Seconds), () => clock.Utc);
        var saves = new[] { "save-a", "save-b", "save-c" };
        var submitted = saves.ToDictionary(s => s, _ => new List<string>(), StringComparer.Ordinal);
        var last = new Dictionary<string, string>(StringComparer.Ordinal);
        var contract = 0;

        for (var step = 0; step < 400; step++)
        {
            switch (random.Next(10))
            {
                case 0:
                    files.Bind(saves[random.Next(saves.Length)]); // save switch (pending writes stay queued)
                    break;
                case 1:
                    fs.FailWrites = random.Next(3) == 0;
                    fs.FailReplace = random.Next(4) == 0;
                    fs.CorruptWrites = random.Next(5) == 0;
                    break;
                case 2:
                case 3:
                    files.Queue.RunPending(force: random.Next(2) == 0);
                    break;
                case 4:
                    clock.Advance(random.Next(1, 40));
                    break;
                case 5:
                    // Damage on disk (torn sector / user edit) while the game runs.
                    var victim = files.OrderStore.PathFor(saves[random.Next(saves.Length)]);
                    if (fs.FileExists(victim))
                    {
                        fs[victim] = "{\"schemaVersion\":1,\"orders\":[{\"con";
                        foreach (var key in submitted.Keys.Where(k => victim.Contains(k + ".")))
                        {
                            submitted[key].Add(fs[victim]);
                            if (!files.HasPending(key))
                            {
                                last.Remove(key); // nothing newer to write: the damaged file stays until the next change
                            }
                        }
                    }

                    break;
                default:
                    if (files.Identity is { } save)
                    {
                        var book = new ActiveOrderBook();
                        for (var i = random.Next(0, 4); i > 0; i--)
                        {
                            book.TryAdd(new CategoryOrder(Contract(++contract), Weed, 1 + random.Next(9), new[] { OgKush, Gdp }, true, step));
                        }

                        var json = ActiveOrderStore.ToJson(book.Orders, save);
                        if (files.Submit(save, json, null, null) > 0)
                        {
                            submitted[save].Add(json);
                            last[save] = json;
                        }
                    }

                    break;
            }

            // Invariant after every step: a file is absent, or exactly one complete document of its own save.
            foreach (var save in saves)
            {
                var path = files.OrderStore.PathFor(save);
                if (fs.FileExists(path))
                {
                    Assert.Contains(fs[path], submitted[save]);
                }

                Assert.True(SafeDocumentFile.CorruptCopies(fs, path).Length <= SafeDocumentFile.MaxCorruptCopies);
                if (fs.FileExists(path + ".bak"))
                {
                    Assert.Equal(DocumentState.Valid, ActiveOrderStore.Classify(fs[path + ".bak"], 1, save).State);
                }
            }
        }

        fs.FailWrites = fs.FailReplace = fs.CorruptWrites = false;
        Assert.True(files.Queue.Flush(1_000));
        foreach (var pair in last)
        {
            Assert.Equal(pair.Value, fs[files.OrderStore.PathFor(pair.Key)]);
        }

        Assert.DoesNotContain(fs.Paths, p => p.EndsWith(SafeDocumentFile.TempSuffix, StringComparison.Ordinal));
    }
}
