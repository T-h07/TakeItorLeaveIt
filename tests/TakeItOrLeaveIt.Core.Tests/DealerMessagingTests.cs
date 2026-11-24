using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// The restock-message decisions with a controllable clock. Sending is simulated: <see cref="Deliver"/> reports
/// success or failure the way the game adapter does after <c>MSGConversation.SendMessage</c>.
/// </summary>
internal sealed class MessagingRig
{
    public MessagingRig(PolicyConfiguration? policy = null, ProductCatalogSnapshot? catalog = null)
    {
        Policies = new InMemoryPolicyProvider(policy ?? PolicyConfiguration.Default);
        Catalog = catalog ?? AllWeedUnlocked();
        Dealers = new DealerSaleCoordinator(Policies, () => Catalog, Log) { Prices = Flat };
        Dealers.SetAuthority(true, 1);
        Dealers.BindSave("save-a", null);
        Notifier = new DealerRestockNotificationCoordinator(Policies, () => Catalog, Dealers, Log) { Clock = () => Now };
        Notifier.BindSave("save-a", null);
    }

    public InMemoryPolicyProvider Policies { get; }

    public ProductCatalogSnapshot? Catalog { get; set; }

    public RecordingLog Log { get; } = new();

    public DealerSaleCoordinator Dealers { get; }

    public DealerRestockNotificationCoordinator Notifier { get; }

    public double Now { get; set; }

    public List<DealerStockAlert> Sent { get; } = new();

    /// <summary>Advances past the settle period and the per-dealer throttle.</summary>
    public MessagingRig Settle()
    {
        Now += DealerRestockNotificationCoordinator.SettleSeconds + 1;
        return this;
    }

    public IReadOnlyList<DealerStockAlert> Check(DealerStock stock, bool deliver = true, bool succeed = true)
    {
        Now += DealerRestockNotificationCoordinator.EvaluateIntervalSeconds;
        var alerts = Notifier.Evaluate(stock);
        if (deliver)
        {
            foreach (var alert in alerts)
            {
                Notifier.Report(alert, succeed, succeed ? null : "simulated failure");
                if (succeed)
                {
                    Sent.Add(alert);
                }
            }
        }

        return alerts;
    }

    public DealerOrderCreation Accept(string contract, string dealer, DealerStock stock, params (ProductKey Product, int Quantity)[] lines)
        => Dealers.OnContractAccepted(DealerRequest(contract, lines), dealer, stock);
}

/// <summary>Phase 04 enhancement suite M: dealer restock messages (decisions, dedup, persistence).</summary>
public class DealerMessagingTests
{
    private const string OutOfWeed = "Yo boss, I'm out of Weed I can sell. Need you to restock me.";

    private static DealerStock Empty(string dealer = Benji) => StockOf(dealer);

    [Fact]
    public void M01_no_message_for_a_category_the_dealer_never_stocked()
    {
        var rig = new MessagingRig().Settle();
        Assert.Empty(rig.Check(Empty()));
        Assert.Empty(rig.Check(StockOf(Benji, (Meth1, 3))));
    }

    [Fact]
    public void M02_no_message_while_the_save_is_settling()
    {
        var rig = new MessagingRig();
        rig.Check(StockOf(Benji, (OgKush, 3)));
        Assert.Empty(rig.Check(Empty())); // still inside the settle period after loading
    }

    [Fact]
    public void M03_out_of_stock_message_after_the_dealer_sold_out()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 3)));

        var alert = Assert.Single(rig.Check(Empty()));

        Assert.Equal(DealerAlertType.OutOfStock, alert.Type);
        Assert.Equal(OutOfWeed, alert.Text);
        Assert.Equal(Benji, alert.DealerId);
        Assert.Equal("save-a|benji_coleman|marijuana|OutOfStock", alert.Key);
    }

    [Fact]
    public void M04_out_of_stock_is_sent_once()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 3)));
        rig.Check(Empty());

        Assert.Empty(rig.Check(Empty()));
        Assert.Empty(rig.Check(Empty()));
        Assert.Single(rig.Sent);
    }

    [Fact]
    public void M05_a_real_restock_rearms_the_message()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 3)));
        rig.Check(Empty());
        rig.Check(StockOf(Benji, (SourDiesel, 1)));

        Assert.Single(rig.Check(Empty()));
        Assert.Equal(2, rig.Sent.Count);
    }

    [Fact]
    public void M06_failed_sends_are_retried_with_backoff_and_never_marked_delivered()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 3)));

        Assert.Single(rig.Check(Empty(), succeed: false));
        Assert.Empty(rig.Check(Empty(), succeed: false)); // backing off
        Assert.False(rig.Notifier.State.Category(Benji, Weed).OutOfStockSent);
        rig.Now += DealerRestockNotificationCoordinator.RetrySeconds;
        Assert.Single(rig.Check(Empty(), succeed: true));
        Assert.True(rig.Notifier.State.Category(Benji, Weed).OutOfStockSent);
        Assert.Equal(1, rig.Notifier.Failed);
        Assert.Equal(1, rig.Notifier.Delivered);
    }

    [Fact]
    public void M07_gives_up_after_the_attempt_limit_until_the_stock_changes()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 3)));
        for (var i = 0; i < DealerRestockNotificationCoordinator.MaxSendAttempts; i++)
        {
            rig.Now += DealerRestockNotificationCoordinator.RetrySeconds * (i + 1);
            rig.Check(Empty(), succeed: false);
        }

        rig.Now += 10_000;
        Assert.Empty(rig.Check(Empty(), succeed: false));
        rig.Check(StockOf(Benji, (OgKush, 1))); // restock clears the give-up
        Assert.Single(rig.Check(Empty()));
    }

    [Fact]
    public void M08_only_permitted_units_count()
    {
        var selected = PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, new[] { OgKush }));
        var rig = new MessagingRig(selected).Settle();
        rig.Check(StockOf(Benji, (OgKush, 2), (SourDiesel, 10)));

        var alert = Assert.Single(rig.Check(StockOf(Benji, (SourDiesel, 10)))); // only an unticked strain left
        Assert.Equal(OutOfWeed, alert.Text);
    }

    [Fact]
    public void M09_a_disabled_category_is_never_reported()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 2)));
        rig.Policies.Update(p => p.WithCategory(Weed, new CategoryPolicy(PolicyMode.Disabled, null)));

        Assert.Empty(rig.Check(Empty()));
    }

    [Fact]
    public void M10_other_categories_do_not_hide_an_empty_one()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 2), (Meth1, 9)));

        var alert = Assert.Single(rig.Check(StockOf(Benji, (Meth1, 9))));
        Assert.Equal(Weed, alert.Category);
    }

    [Fact]
    public void M11_shortage_message_states_required_available_and_missing_units()
    {
        var rig = new MessagingRig().Settle();
        var stock = StockOf(Benji, (OgKush, 2));
        rig.Accept(ContractA, Benji, stock, (OgKush, 5));

        var alert = Assert.Single(rig.Check(stock));

        Assert.Equal(DealerAlertType.AppointmentShortage, alert.Type);
        Assert.Equal("Boss, I've got a customer waiting for 5 Weed, but only 2 permitted units are available. Need 3 more.", alert.Text);
        Assert.Equal((5L, 2L, 3L), (alert.Required, alert.Available, alert.Shortfall));
        Assert.EndsWith("|" + ContractA, alert.Key);
    }

    [Fact]
    public void M12_units_reserved_for_other_appointments_are_not_available()
    {
        var rig = new MessagingRig().Settle();
        var stock = StockOf(Benji, (OgKush, 7));
        rig.Accept(ContractA, Benji, stock, (OgKush, 5)); // reserves 5 of 7
        rig.Accept(ContractB, Benji, stock, (OgKush, 5)); // waits: 2 left

        var alert = Assert.Single(rig.Check(stock));
        Assert.Equal(ContractB, alert.ContractId);
        Assert.Equal(2, alert.Available);
    }

    [Fact]
    public void M13_mixed_off_counts_the_best_single_product()
    {
        var rig = new MessagingRig(PolicyConfiguration.Default.WithMixedDeliveries(false)).Settle();
        var stock = StockOf(Benji, (OgKush, 3), (SourDiesel, 2));
        rig.Accept(ContractA, Benji, stock, (OgKush, 5));

        var alert = Assert.Single(rig.Check(stock));
        Assert.Equal(3, alert.Available); // not 5: an order is never split when mixing is off
        Assert.Equal(2, alert.Shortfall);
    }

    [Fact]
    public void M14_shortage_is_resent_only_when_it_grows()
    {
        var rig = new MessagingRig().Settle();
        rig.Accept(ContractA, Benji, StockOf(Benji, (OgKush, 3)), (OgKush, 5));
        rig.Check(StockOf(Benji, (OgKush, 3))); // short by 2: sent

        Assert.Empty(rig.Check(StockOf(Benji, (OgKush, 3))));
        Assert.Empty(rig.Check(StockOf(Benji, (OgKush, 4)))); // smaller shortfall: no new text
        var grown = Assert.Single(rig.Check(StockOf(Benji, (OgKush, 1))));
        Assert.Equal(4, grown.Shortfall);
    }

    [Fact]
    public void M15_a_reservation_ends_the_episode_and_a_later_shortage_is_new()
    {
        var rig = new MessagingRig().Settle();
        rig.Accept(ContractA, Benji, StockOf(Benji, (OgKush, 3)), (OgKush, 5));
        rig.Check(StockOf(Benji, (OgKush, 3)));
        rig.Dealers.Reconcile(StockOf(Benji, (OgKush, 5)));
        rig.Check(StockOf(Benji, (OgKush, 5))); // reserved: history cleared
        rig.Dealers.Reconcile(StockOf(Benji, (OgKush, 1))); // stock lost: waiting again

        Assert.Single(rig.Check(StockOf(Benji, (OgKush, 1))));
        Assert.Equal(2, rig.Sent.Count(a => a.Type == DealerAlertType.AppointmentShortage));
    }

    [Fact]
    public void M16_a_waiting_appointment_replaces_the_out_of_stock_text()
    {
        var rig = new MessagingRig().Settle();
        rig.Accept(ContractA, Benji, Empty(), (OgKush, 5));

        var alert = Assert.Single(rig.Check(Empty()));
        Assert.Equal(DealerAlertType.AppointmentShortage, alert.Type);
        Assert.Equal(0, alert.Available);
    }

    [Fact]
    public void M17_each_dealer_has_their_own_history()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 1)));
        rig.Check(StockOf(Molly, (OgKush, 1)));
        rig.Check(Empty(Benji));

        var molly = Assert.Single(rig.Check(Empty(Molly)));
        Assert.Equal(Molly, molly.DealerId);
    }

    [Fact]
    public void M18_history_survives_a_reload_and_never_spams()
    {
        var fs = new MemoryFileSystem();
        var store = new DealerAlertStore("root", fs);
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 1)));
        rig.Check(Empty());
        store.SaveText("save-a", rig.Notifier.State.ToJson());

        var issues = new List<string>();
        rig.Notifier.BindSave("save-a", store.Load("save-a", issues));
        rig.Settle();

        Assert.Empty(issues);
        Assert.Empty(rig.Check(Empty())); // delivered before the reload: not repeated
        Assert.True(rig.Notifier.State.Category(Benji, Weed).Relevant);
    }

    [Fact]
    public void M19_save_switch_isolates_history_and_drops_stale_reports()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 1)));
        var alert = rig.Check(Empty(), deliver: false).Single();

        rig.Dealers.BindSave("save-b", null);
        rig.Notifier.BindSave("save-b", null);
        rig.Notifier.Report(alert, true);

        Assert.Empty(rig.Notifier.State.Categories);
        Assert.Equal(0, rig.Notifier.Delivered);
    }

    [Fact]
    public void M20_nothing_while_the_mod_is_off_or_not_the_host()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 1)));
        rig.Policies.Update(p => p.WithEnabled(false));
        Assert.Empty(rig.Check(Empty()));

        rig.Policies.Update(p => p.WithEnabled(true));
        rig.Dealers.SetAuthority(isServer: false, playerCount: 1);
        Assert.Empty(rig.Check(Empty()));
    }

    [Fact]
    public void M21_checks_are_throttled_per_dealer()
    {
        var rig = new MessagingRig().Settle();
        rig.Check(StockOf(Benji, (OgKush, 1)));
        rig.Now += DealerRestockNotificationCoordinator.EvaluateIntervalSeconds;
        Assert.Single(rig.Notifier.Evaluate(Empty()), a => a.Type == DealerAlertType.OutOfStock);
        Assert.Empty(rig.Notifier.Evaluate(Empty())); // same instant: throttled
    }

    [Fact]
    public void M22_damaged_history_starts_fresh_with_an_issue()
    {
        var fs = new MemoryFileSystem();
        var store = new DealerAlertStore("root", fs);
        fs.Files[store.PathFor("save-a")] = "{ not json";
        var issues = new List<string>();

        var state = store.Load("save-a", issues);

        Assert.Empty(state.Categories);
        Assert.Single(issues);
        fs.Files[store.PathFor("save-a")] = "{\"schemaVersion\":1,\"categories\":[{\"dealer\":\"bad id!\",\"category\":\"marijuana\"}],\"shortages\":[42]}";
        issues.Clear();
        Assert.Empty(store.Load("save-a", issues).Categories);
        Assert.Equal(2, issues.Count);
    }
}
