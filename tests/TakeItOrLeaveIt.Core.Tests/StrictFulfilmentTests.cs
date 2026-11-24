using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 04 enhancement suite S: strict dealer fulfilment (no vanilla fallback for managed appointments). Runs on
/// <see cref="DealerHarness"/>, a <b>simulated</b> vanilla dealer; the in-game checks are in docs/PHASE_04_TESTING.md.
/// </summary>
public class StrictFulfilmentTests
{
    private static PolicyConfiguration OnlyOgKush()
        => PolicyConfiguration.Default.WithCategory(Weed, new CategoryPolicy(PolicyMode.SelectedOnly, new[] { OgKush }));

    [Fact]
    public void S01_short_permitted_stock_never_falls_back_to_the_excluded_requested_product()
    {
        var h = new DealerHarness(OnlyOgKush()).Stock(Benji, OgKush, 2).Stock(Benji, SourDiesel, 50);
        h.Accept(ContractA, 100f, (SourDiesel, 5)); // vanilla asked for an excluded strain

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Withheld, result.Decision);
        Assert.Equal(50, h.UnitsOf(Benji, SourDiesel));
        Assert.Equal(2, h.UnitsOf(Benji, OgKush));
        Assert.Equal(0, h.Payments);
        Assert.Equal(AppointmentState.WaitingForEligibleStock, h.Coordinator.Find(ContractA)!.State);
    }

    [Fact]
    public void S02_never_a_partial_delivery()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 3).Stock(Benji, Gdp, 1);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.Empty(result.Items);
        Assert.Equal(0, h.UnitsRemoved);
        Assert.Equal(4, h.TotalUnits(Benji));
    }

    [Fact]
    public void S03_a_short_removal_is_rolled_back_and_nothing_is_handed_over()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.RemovalShortfall = 1;

        var result = h.Handover(ContractA);

        Assert.False(result.Settled);
        Assert.Empty(result.Items);
        Assert.Equal(10, h.UnitsOf(Benji, OgKush)); // the 4 removed units went back to the dealer
        Assert.Equal(AppointmentState.WaitingForEligibleStock, h.Coordinator.Find(ContractA)!.State);
        Assert.Equal(0, h.Coordinator.Ledger.HeldUnitsTotal);
        Assert.Contains("returned 4 of 5", h.Coordinator.LastError);

        h.RemovalShortfall = 0;
        Assert.True(h.Handover(ContractA).Settled);
        Assert.Equal(1, h.Payments);
    }

    [Fact]
    public void S04_a_removal_that_could_not_start_waits_again()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Accept(ContractA, 100f, (OgKush, 5));
        Assert.Equal(HandoverDecision.Fulfil, h.Coordinator.PrepareHandover(ContractA, h.StockOf(Benji)).Decision);

        h.Coordinator.AbortHandover(ContractA, "simulated error");

        Assert.Equal(AppointmentState.WaitingForEligibleStock, h.Coordinator.Find(ContractA)!.State);
        Assert.Equal(0, h.Coordinator.Ledger.ActiveCount);
        Assert.Empty(h.Coordinator.CheckInvariants());
    }

    [Theory]
    [InlineData(SaleEnd.Expired)]
    [InlineData(SaleEnd.Failed)]
    public void S05_the_deadline_uses_the_games_own_failure_and_leaves_nothing_behind(SaleEnd end)
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 2);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Handover(ContractA); // withheld

        h.End(ContractA, end);

        Assert.Null(h.Coordinator.Find(ContractA));
        Assert.Equal(0, h.Coordinator.ActiveAppointments);
        Assert.Equal((0, 0, 0), (h.Payments, h.XpAwards, h.Completions));
        Assert.Equal(2, h.UnitsOf(Benji, OgKush));
    }

    [Fact]
    public void S06_unmanaged_contract_for_an_excluded_product_hands_over_nothing()
    {
        var h = new DealerHarness(OnlyOgKush()).Stock(Benji, SourDiesel, 10);
        var ready = false;
        h.Coordinator.PolicyReady = () => ready;
        Assert.Equal(OrderOutcome.PolicyNotLoaded, h.Accept(ContractA, 100f, (SourDiesel, 5)).Outcome);
        ready = true;

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Withheld, result.Decision);
        Assert.Equal(10, h.UnitsOf(Benji, SourDiesel));
        Assert.Equal(0, h.Payments);
    }

    [Fact]
    public void S07_unmanaged_contract_for_a_permitted_product_stays_vanilla()
    {
        var h = new DealerHarness(OnlyOgKush()).Stock(Benji, OgKush, 10);
        var ready = false;
        h.Coordinator.PolicyReady = () => ready;
        h.Accept(ContractA, 100f, (OgKush, 5));
        ready = true;

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Vanilla, result.Decision);
        Assert.True(result.Settled);
    }

    [Fact]
    public void S08_global_off_is_vanilla_even_for_excluded_products()
    {
        var h = new DealerHarness(OnlyOgKush().WithEnabled(false)).Stock(Benji, SourDiesel, 10);
        h.Accept(ContractA, 100f, (SourDiesel, 5));

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Vanilla, result.Decision);
        Assert.True(result.Settled);
        Assert.False(h.Coordinator.BlocksUnmanaged(DealerRequest(ContractA, (SourDiesel, 5)), out _));
    }

    [Fact]
    public void S09_clients_and_multiplayer_stay_vanilla()
    {
        var h = new DealerHarness(OnlyOgKush()).Stock(Benji, SourDiesel, 10);
        h.Coordinator.SetAuthority(isServer: true, playerCount: 2);
        h.Accept(ContractA, 100f, (SourDiesel, 5));

        Assert.Equal(HandoverDecision.Vanilla, h.Handover(ContractA).Decision);
    }

    [Fact]
    public void S10_repeated_withheld_handovers_never_pay_and_keep_one_appointment()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 1);
        h.Accept(ContractA, 100f, (OgKush, 5));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HandoverDecision.Withheld, h.Handover(ContractA).Decision);
        }

        Assert.Equal(4, h.Coordinator.Find(ContractA)!.WithheldHandovers);
        Assert.Equal(1, h.Coordinator.ActiveAppointments);
        Assert.Equal((0, 0), (h.Payments, h.XpAwards));
        Assert.Empty(h.Coordinator.CheckInvariants());
    }

    [Fact]
    public void S11_moving_to_a_dealer_without_stock_waits_and_frees_the_old_dealer()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 5);
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Accept(ContractB, 100f, (OgKush, 5)); // waits behind A
        h.Reassign(ContractA, Molly);

        Assert.Equal(HandoverDecision.Withheld, h.Handover(ContractA).Decision);
        Assert.Equal(Molly, h.Coordinator.Find(ContractA)!.DealerId);
        h.Tick(Benji);
        Assert.Equal(AppointmentState.Reserved, h.Coordinator.Find(ContractB)!.State); // Benji's units went to B
    }

    [Fact]
    public void S12_managed_handover_with_enough_stock_is_unchanged()
    {
        var h = new DealerHarness().Stock(Benji, SourDiesel, 5);
        h.Accept(ContractA, 100f, (OgKush, 5));

        var result = h.Handover(ContractA);

        Assert.Equal(HandoverDecision.Fulfil, result.Decision);
        Assert.True(result.Settled);
        Assert.Equal((1, 1, 1), (h.Payments, h.XpAwards, h.Completions));
        Assert.Equal(5, h.UnitsRemoved);
    }

    /// <summary>
    /// Randomised interleavings on the <b>simulated</b> dealer with tiered prices, policy changes and short removals.
    /// After every handover: a managed delivery is all-or-nothing, only snapshot products, never below the quote, and
    /// at most one payment per contract; the ledger invariants hold after every step.
    /// </summary>
    [Fact]
    public void S13_randomised_strict_and_fair_invariants_hold()
    {
        var random = new Random(9001);
        var dealers = new[] { Benji, Molly, "dealer_three" };
        var products = new[] { OgKush, SourDiesel, Gdp };
        var h = new DealerHarness();
        h.Coordinator.Prices = Tiered;
        var orders = new Dictionary<string, CategoryOrder>();
        var paid = new Dictionary<string, int>();
        var next = 1;
        var settled = 0;
        var withheld = 0;

        for (var step = 0; step < 3000; step++)
        {
            var dealer = dealers[random.Next(dealers.Length)];
            switch (random.Next(9))
            {
                case 0:
                case 1:
                    h.Stock(dealer, products[random.Next(products.Length)], random.Next(1, 8));
                    break;
                case 2:
                    h.Take(dealer, products[random.Next(products.Length)], random.Next(1, 6));
                    break;
                case 3:
                {
                    var id = Contract(next++);
                    var creation = h.Accept(id, dealer, 100f, (products[random.Next(products.Length)], random.Next(1, 7)));
                    if (creation.IsManaged)
                    {
                        orders[id] = creation.Appointment!.Order;
                    }

                    break;
                }

                case 4:
                    h.Tick(dealer);
                    break;
                case 5:
                case 6:
                {
                    var open = orders.Keys.Where(id => h.StateOf(id) == "Active").ToList();
                    if (open.Count == 0)
                    {
                        break;
                    }

                    var id = open[random.Next(open.Count)];
                    h.RemovalShortfall = random.Next(10) == 0 ? 1 : 0;
                    var before = h.Payments;
                    var result = h.Handover(id, customerAccepts: random.Next(8) != 0);
                    var order = orders[id];
                    var units = result.Items.Sum(i => i.Units);
                    Assert.True(units == 0 || units == order.RequiredUnits, $"partial delivery {units}/{order.RequiredUnits}");
                    Assert.All(result.Items, i => Assert.True(order.IsAllowed(i.Product), $"{i.Product} not in snapshot"));
                    if (units > 0 && order.PricingBasis == PricingBasis.Priced)
                    {
                        Assert.True(FairPriceRule.BasketValue(order, result.Items.Select(i => (i.Product, (long)i.Units))) >= order.QuotedValue);
                    }

                    if (h.Payments > before)
                    {
                        paid[id] = (paid.TryGetValue(id, out var n) ? n : 0) + 1;
                        Assert.Equal(1, paid[id]);
                        settled++;
                    }

                    if (result.Decision == HandoverDecision.Withheld)
                    {
                        withheld++;
                    }

                    break;
                }

                case 7:
                {
                    var open = orders.Keys.Where(id => h.StateOf(id) == "Active").ToList();
                    if (open.Count > 0)
                    {
                        h.End(open[random.Next(open.Count)], random.Next(2) == 0 ? SaleEnd.Expired : SaleEnd.Failed);
                    }

                    break;
                }

                default:
                    h.Policies.Update(p => p.WithMixedDeliveries(random.Next(2) == 0));
                    break;
            }

            Assert.Empty(h.Coordinator.Ledger.CheckInvariants());
        }

        Assert.True(settled > 20, $"only {settled} settled");
        Assert.True(withheld > 5, $"only {withheld} withheld");
    }
}
