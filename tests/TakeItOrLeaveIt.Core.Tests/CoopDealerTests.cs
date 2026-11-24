using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Multiplayer;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Dealers;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>Phase 07 suite D: dealer sales under the session model and the quality-lot integrity check (simulation only).</summary>
public class CoopDealerTests
{
    private static DealerStock Stock(string dealer, params (ProductKey P, int U, QualityTier? Q)[] stacks)
        => new(dealer, stacks.Select((s, i) => new InventoryStack($"slot:{i}", s.P, s.U, s.Q)));

    private static (DealerSaleCoordinator Coordinator, RecordingLog Log) Host()
    {
        var log = new RecordingLog();
        var c = new DealerSaleCoordinator(new InMemoryPolicyProvider(PolicyConfiguration.Default), AllWeedUnlocked, log);
        c.ApplySession(Coop.MultiplayerHost());
        c.BindSave("save-a", null);
        return (c, log);
    }

    private static OrderRequest Request(string id, int units, QualityTier? minimum)
        => new(id, isPlayerContract: false, new[] { new OrderLine(OgKush.Value, units) }, minimum);

    /// <summary>Accepts and prepares a Standard-or-better appointment for 3 OG Kush reserved from the Premium lot.</summary>
    private static (DealerSaleCoordinator C, RecordingLog Log, HandoverPlan Plan) Prepared(DealerStock stock, int units = 3, QualityTier? minimum = QualityTier.Standard)
    {
        var (c, log) = Host();
        Assert.True(c.OnContractAccepted(Request(ContractC, units, minimum), Benji, stock).IsManaged);
        var plan = c.PrepareHandover(ContractC, stock);
        Assert.Equal(HandoverDecision.Fulfil, plan.Decision);
        return (c, log, plan);
    }

    [Fact]
    public void P07_D01_multiplayer_host_runs_dealer_appointments_end_to_end()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Coordinator.ApplySession(Coop.MultiplayerHost(4));

        Assert.True(h.Accept(ContractC, 100f, (OgKush, 3)).IsManaged);
        var result = h.Handover(ContractC);

        Assert.True(result.Settled);
        Assert.Equal((1, 1, 1), (h.Payments, h.XpAwards, h.Completions));
        Assert.Equal(7, h.UnitsOf(Benji, OgKush));
    }

    [Fact]
    public void P07_D02_a_client_never_creates_or_runs_dealer_appointments()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Coordinator.ApplySession(Coop.ClientReady());

        Assert.Equal(OrderOutcome.ModDisabled, h.Accept(ContractC, 100f, (OgKush, 3)).Outcome);
        Assert.Null(h.Coordinator.Find(ContractC));
        Assert.False(h.Coordinator.BlocksUnmanaged(Sales.DealerRequest(ContractC, (OgKush, 3)), out _)); // vanilla on a client
    }

    [Fact]
    public void P07_D03_exact_lot_removal_commits_without_mismatch()
    {
        var stock = Stock(Benji, (OgKush, 5, QualityTier.Poor), (OgKush, 4, QualityTier.Premium));
        var (c, _, plan) = Prepared(stock);
        var line = Assert.Single(plan.Lines);
        Assert.Equal(QualityTier.Premium, line.Quality);

        var completion = c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, QualityTier.Premium, 3) }, out var mismatches);

        Assert.Equal(HandoverCompletion.Committed, completion);
        Assert.Equal(0, mismatches);
        Assert.Equal(TransactionPhase.Committing, TransactionPhases.Of(c.Find(ContractC)!.State)); // committed once the game processes it
    }

    [Fact]
    public void P07_D04_a_higher_tier_than_reserved_is_accepted_and_reported()
    {
        var stock = Stock(Benji, (OgKush, 4, QualityTier.Premium), (OgKush, 4, QualityTier.Heavenly));
        var (c, log, _) = Prepared(stock);

        var completion = c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, QualityTier.Heavenly, 3) }, out var mismatches);

        Assert.Equal(HandoverCompletion.Committed, completion);
        Assert.Equal(3, mismatches);
        Assert.Contains(log.Infos, i => i.Contains("different quality tier than reserved"));
    }

    [Fact]
    public void P07_D05_units_below_the_minimum_roll_back()
    {
        var stock = Stock(Benji, (OgKush, 4, QualityTier.Premium));
        var (c, _, _) = Prepared(stock);

        var completion = c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, QualityTier.Poor, 3) }, out _);

        Assert.Equal(HandoverCompletion.RolledBack, completion);
        Assert.Equal(AppointmentState.WaitingForEligibleStock, c.Find(ContractC)!.State);
        Assert.Equal(0, c.Ledger.HeldUnits(Benji, OgKush));
        Assert.Contains("below the order's quality minimum", c.LastError);
    }

    [Fact]
    public void P07_D06_unknown_quality_with_a_minimum_rolls_back()
    {
        var stock = Stock(Benji, (OgKush, 4, QualityTier.Premium));
        var (c, _, _) = Prepared(stock);

        Assert.Equal(HandoverCompletion.RolledBack, c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, null, 3) }, out _));
    }

    [Fact]
    public void P07_D07_a_product_that_was_not_planned_rolls_back()
    {
        var stock = Stock(Benji, (OgKush, 4, QualityTier.Premium));
        var (c, _, _) = Prepared(stock);

        var completion = c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, QualityTier.Premium, 2), new RemovedLot(Gdp, QualityTier.Premium, 1) }, out _);

        Assert.Equal(HandoverCompletion.RolledBack, completion);
        Assert.Contains("other products than planned", c.LastError);
    }

    [Fact]
    public void P07_D08_a_short_removal_rolls_back()
    {
        var stock = Stock(Benji, (OgKush, 4, QualityTier.Premium));
        var (c, _, _) = Prepared(stock);

        Assert.Equal(HandoverCompletion.RolledBack, c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, QualityTier.Premium, 2) }, out _));
    }

    [Fact]
    public void P07_D09_after_a_tier_mismatch_other_holds_are_rechecked_against_real_stock()
    {
        // Two appointments: C holds 3 Standard, D holds 3 Heavenly. The game takes Heavenly units for C.
        var (c, _) = Host();
        var before = Stock(Benji, (OgKush, 3, QualityTier.Standard), (OgKush, 3, QualityTier.Heavenly));
        Assert.True(c.OnContractAccepted(Request(ContractC, 3, QualityTier.Standard), Benji, before).IsManaged);
        Assert.True(c.OnContractAccepted(Request(ContractD, 3, QualityTier.Heavenly), Benji, before).IsManaged);
        Assert.Equal(6, c.Ledger.HeldUnits(Benji, OgKush));
        c.PrepareHandover(ContractC, before);

        Assert.Equal(HandoverCompletion.Committed, c.CompleteHandover(ContractC, new[] { new RemovedLot(OgKush, QualityTier.Heavenly, 3) }, out var mismatches));
        Assert.Equal(3, mismatches);
        var after = Stock(Benji, (OgKush, 3, QualityTier.Standard)); // what is really left
        c.Reconcile(after, force: true);

        Assert.NotEqual(AppointmentState.Reserved, c.Find(ContractD)!.State); // D's Heavenly hold is gone, it waits
        Assert.Empty(c.CheckInvariants());
        Assert.True(c.Ledger.HeldUnits(Benji, new StockLot(OgKush, QualityTier.Heavenly)) == 0);
    }

    [Fact]
    public void P07_D10_completion_is_idempotent()
    {
        var stock = Stock(Benji, (OgKush, 4, QualityTier.Premium));
        var (c, _, _) = Prepared(stock);
        var lots = new[] { new RemovedLot(OgKush, QualityTier.Premium, 3) };

        Assert.Equal(HandoverCompletion.Committed, c.CompleteHandover(ContractC, lots, out _));
        Assert.Equal(HandoverCompletion.Ignored, c.CompleteHandover(ContractC, lots, out _));
    }

    [Fact]
    public void P07_D11_a_second_handover_request_removes_nothing()
    {
        var stock = Stock(Benji, (OgKush, 8, QualityTier.Premium));
        var (c, _, _) = Prepared(stock);

        var again = c.PrepareHandover(ContractC, stock);

        Assert.Equal(HandoverDecision.NothingToGive, again.Decision);
        Assert.Empty(again.Lines);
    }

    [Fact]
    public void P07_D12_dealer_cut_and_payment_happen_once_under_repeated_handover_routines()
    {
        var h = new DealerHarness().Stock(Benji, OgKush, 10);
        h.Coordinator.ApplySession(Coop.MultiplayerHost());
        h.Accept(ContractC, 100f, (OgKush, 3));

        h.Handover(ContractC);
        h.Handover(ContractC);
        h.Handover(ContractC);

        Assert.Equal((1, 1), (h.Payments, h.Completions));
        Assert.Equal(7, h.UnitsOf(Benji, OgKush));
        Assert.Equal(DealerHarness.CutRate * 100f, h.DealerCash, 3);
    }
}
