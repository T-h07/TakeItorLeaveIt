using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Catalog;
using static TakeItOrLeaveIt.Core.Tests.Sales;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Suite C: transaction integrity, against a simulated authoritative settlement (see <see cref="SettlementHarness"/>).
/// These prove the model; the in-game tests confirm the real economy.
/// </summary>
public class SaleTransactionTests
{
    private static SettlementHarness StockedHarness()
        => new SettlementHarness().Stock("gdp", Gdp, 10).Stock("og", OgKush, 10).Stock("coke", Coke1, 10);

    [Fact]
    public void C01_one_sale_settles_exactly_once()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.True(h.Submit(ContractA, ("gdp", 3), ("og", 2)));

        Assert.Equal((1, 1, 1, 1, 100f), (h.Completions, h.PaymentCount, h.XpAwards, h.RelationshipChanges, h.Cash));
        Assert.Equal(7, h.UnitsOf("gdp"));
        Assert.Equal(8, h.UnitsOf("og"));
        Assert.Equal(0, h.Coordinator.Book.Count);
    }

    [Fact]
    public void C02_rejected_handover_moves_nothing()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));
        var before = h.TotalUnits;

        Assert.False(h.Submit(ContractA, ("gdp", 3), ("coke", 2)));

        Assert.Equal((0, 0, 0, 0f), (h.Completions, h.PaymentCount, h.XpAwards, h.Cash));
        Assert.Equal(before, h.TotalUnits);
        Assert.Equal(SaleState.Rejected, h.Coordinator.Book.Orders[0].State);
        Assert.Equal("Active", h.StateOf(ContractA));
    }

    [Fact]
    public void C03_repeated_submit_does_not_double_consume_or_pay()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.True(h.Submit(ContractA, ("og", 5)));
        Assert.False(h.Submit(ContractA, ("og", 5)));

        Assert.Equal((1, 1, 1), (h.Completions, h.PaymentCount, h.XpAwards));
        Assert.Equal(5, h.UnitsOf("og"));
    }

    [Fact]
    public void C04_repeated_commit_callbacks_are_ignored()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.Equal(CommitObservation.First, h.Coordinator.OnHandoverProcessed(ContractA));
        Assert.Equal(CommitObservation.Duplicate, h.Coordinator.OnHandoverProcessed(ContractA));
        Assert.Equal(2, h.Coordinator.Book.Orders[0].CommitCount);
        Assert.Single(h.Log.Warnings);
    }

    [Fact]
    public void C05_canceled_order_cannot_complete_afterwards()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Cancel(ContractA);

        Assert.False(h.Submit(ContractA, ("og", 5)));
        Assert.Equal(0, h.PaymentCount);
        Assert.Equal(CommitObservation.NotManaged, h.Coordinator.OnHandoverProcessed(ContractA));
    }

    [Fact]
    public void C06_expired_order_cannot_complete()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));
        h.Expire(ContractA);

        Assert.False(h.Submit(ContractA, ("og", 5)));
        Assert.Equal((0, 0), (h.Completions, h.PaymentCount));
    }

    [Fact]
    public void C07_failed_order_is_not_marked_completed()
    {
        var book = new ActiveOrderBook();
        book.TryAdd(Order(5));

        var ended = book.End(ContractA, SaleEnd.Failed);

        Assert.Equal(SaleState.Failed, ended!.State);
        Assert.Null(book.End(ContractA, SaleEnd.Completed));
    }

    [Fact]
    public void C08_stale_callback_for_another_contract_changes_nothing()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 5));

        Assert.Equal(CommitObservation.NotManaged, h.Coordinator.OnHandoverProcessed(ContractB));
        Assert.Null(h.Coordinator.OnContractEnded(ContractB, SaleEnd.Completed));
        Assert.Equal(SaleState.Pending, h.Coordinator.Book.Orders[0].State);
    }

    [Fact]
    public void C09_second_sale_cannot_reuse_consumed_items()
    {
        var h = StockedHarness();
        h.Accept(ContractA, 100f, (OgKush, 8));
        h.Accept(ContractB, 50f, (OgKush, 8));

        Assert.True(h.Submit(ContractA, ("og", 8)));
        Assert.False(h.Submit(ContractB, ("og", 8)));

        Assert.Equal(1, h.PaymentCount);
        Assert.Equal(2, h.UnitsOf("og"));
    }

    [Fact]
    public void C10_completion_notifications_are_idempotent()
    {
        var book = new ActiveOrderBook();
        book.TryAdd(Order(5));
        var version = book.Version;

        Assert.NotNull(book.End(ContractA, SaleEnd.Completed));
        Assert.Null(book.End(ContractA, SaleEnd.Completed));
        Assert.Equal(version + 1, book.Version);
    }

    [Fact]
    public void Commit_after_end_is_refused()
    {
        var book = new ActiveOrderBook();
        book.TryAdd(Order(5));
        book.TryGet(ContractA, out var transaction);
        book.End(ContractA, SaleEnd.Expired);

        Assert.Equal(CommitObservation.NotManaged, book.ObserveCommit(ContractA));
        Assert.Equal(SaleState.Expired, transaction.State);
    }

    [Theory]
    [InlineData(SaleState.Pending, SaleState.Validated, true)]
    [InlineData(SaleState.Validated, SaleState.Rejected, true)]
    [InlineData(SaleState.Rejected, SaleState.CommitObserved, true)]
    [InlineData(SaleState.CommitObserved, SaleState.Validated, false)]
    [InlineData(SaleState.CommitObserved, SaleState.Completed, true)]
    [InlineData(SaleState.Completed, SaleState.Completed, false)]
    [InlineData(SaleState.Expired, SaleState.Completed, false)]
    [InlineData(SaleState.Canceled, SaleState.CommitObserved, false)]
    [InlineData(SaleState.Validated, SaleState.Pending, false)]
    public void State_machine_allows_only_forward_transitions(SaleState from, SaleState to, bool allowed)
        => Assert.Equal(allowed, SaleTransaction.CanTransition(from, to));

    [Fact]
    public void Validation_never_changes_a_committed_order()
    {
        var book = new ActiveOrderBook();
        book.TryAdd(Order(5));
        book.ObserveCommit(ContractA);

        var state = book.RecordValidation(DeliveryValidator.Validate(Order(5), new[] { Item("0", Coke1, 5) }, AllWeedUnlocked()));

        Assert.Equal(SaleState.CommitObserved, state);
    }

    [Fact]
    public void Validated_delivery_match_ignores_recipe_and_keeps_quality()
    {
        var valid = DeliveryValidator.Validate(Order(5), new[] { Item("0", Gdp, 3), Item("1", OgKush, 2) }, AllWeedUnlocked());

        var perfect = DeliveryMatch.Compute(valid, new[] { new RatedUnits(3, 1f), new RatedUnits(2, 1f) });
        var lowerQuality = DeliveryMatch.Compute(valid, new[] { new RatedUnits(3, 1f), new RatedUnits(2, 0.5f) });

        Assert.Equal((1f, 5), (perfect.Match, perfect.MatchedUnits));
        Assert.Equal(0.8f, lowerQuality.Match, 3);
    }

    [Fact]
    public void Invalid_delivery_matches_nothing()
    {
        var invalid = DeliveryValidator.Validate(Order(5), new[] { Item("0", Coke1, 5) }, AllWeedUnlocked());

        var result = DeliveryMatch.Compute(invalid, new[] { new RatedUnits(5, 1f) });

        Assert.Equal((0f, 0), (result.Match, result.MatchedUnits));
    }

    [Fact]
    public void Match_is_clamped_against_bad_ratings()
    {
        var valid = DeliveryValidator.Validate(Order(5), new[] { Item("0", Gdp, 5) }, AllWeedUnlocked());

        Assert.Equal(1f, DeliveryMatch.Compute(valid, new[] { new RatedUnits(5, 7f) }).Match);
        Assert.Equal(0f, DeliveryMatch.Compute(valid, new[] { new RatedUnits(5, float.NaN) }).Match);
        Assert.Equal(0f, DeliveryMatch.Compute(valid, new[] { new RatedUnits(5, -1f) }).Match);
    }
}
