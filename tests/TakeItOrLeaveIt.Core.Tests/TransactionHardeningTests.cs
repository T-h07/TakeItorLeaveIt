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

/// <summary>Phase 08 hardening, suite T: transaction integrity (fail closed, rollback failures).</summary>
public sealed class TransactionHardeningTests
{
    private static DeliveryValidation Valid() => DeliveryValidator.Validate(Order(5, true, OgKush), new[] { Item("0", OgKush, 5) }, AllWeedUnlocked());

    private static DeliveryValidation Invalid() => DeliveryValidator.Validate(Order(5, true, OgKush), new[] { Item("0", Gdp, 5) }, AllWeedUnlocked());

    [Fact]
    public void P08_T01_guard_decisions_cover_every_case()
    {
        Assert.Equal(HandoverHookAction.Vanilla, ManagedHandoverGuard.Decide(holdsSnapshot: false, validation: null));
        Assert.Equal(HandoverHookAction.Refuse, ManagedHandoverGuard.Decide(holdsSnapshot: true, validation: null));
        Assert.Equal(HandoverHookAction.UseValidation, ManagedHandoverGuard.Decide(holdsSnapshot: true, Valid()));
        Assert.Equal(HandoverHookAction.Refuse, ManagedHandoverGuard.Decide(holdsSnapshot: true, Invalid()));
        Assert.Equal(HandoverHookAction.Refuse, ManagedHandoverGuard.OnError(holdsSnapshot: true));
        Assert.Equal(HandoverHookAction.Vanilla, ManagedHandoverGuard.OnError(holdsSnapshot: false));
    }

    [Fact]
    public void P08_T02_a_player_order_is_still_held_when_authority_drops_so_the_delivery_is_refused_not_vanilla()
    {
        var h = new SettlementHarness();
        Assert.True(h.Accept(ContractA, 100, (OgKush, 5)).IsManaged);

        h.Coordinator.ApplySession(SessionDecision.Initial); // e.g. a failed session check (FailClosed)

        Assert.Null(h.Coordinator.Find(ContractA));
        Assert.Null(h.Coordinator.Validate(ContractA, new[] { Item("0", Gdp, 5) }));
        Assert.True(h.Coordinator.HoldsSnapshot(ContractA));
        Assert.Equal(HandoverHookAction.Refuse, ManagedHandoverGuard.Decide(h.Coordinator.HoldsSnapshot(ContractA), null));
    }

    [Fact]
    public void P08_T03_a_dealer_appointment_is_still_held_when_authority_drops_and_is_not_treated_as_unmanaged()
    {
        var h = new DealerHarness().Stock(Dealers.Benji, OgKush, 10);
        Assert.True(h.Accept(Dealers.ContractC, Dealers.Benji, 100, (OgKush, 5)).IsManaged);

        h.Coordinator.ApplySession(SessionDecision.Initial);

        Assert.Null(h.Coordinator.Find(Dealers.ContractC));
        Assert.False(h.Coordinator.BlocksUnmanaged(DealerRequest(Dealers.ContractC, (Gdp, 5)), Dealers.Benji, out _)); // the old path would run vanilla removal
        Assert.True(h.Coordinator.HoldsSnapshot(Dealers.ContractC)); // the new guard refuses instead
    }

    [Fact]
    public void P08_T04_ended_and_unknown_contracts_are_not_held()
    {
        var h = new SettlementHarness();
        h.Accept(ContractA, 100, (OgKush, 5));
        h.Expire(ContractA);

        Assert.False(h.Coordinator.HoldsSnapshot(ContractA));
        Assert.False(h.Coordinator.HoldsSnapshot(ContractB));
        Assert.False(h.Coordinator.HoldsSnapshot(null));

        var d = new DealerHarness().Stock(Dealers.Benji, OgKush, 10);
        d.Accept(Dealers.ContractC, Dealers.Benji, 100, (OgKush, 5));
        d.End(Dealers.ContractC, SaleEnd.Failed);
        Assert.False(d.Coordinator.HoldsSnapshot(Dealers.ContractC));
        Assert.False(d.Coordinator.HoldsSnapshot(null));
    }

    [Fact]
    public void P08_T05_a_held_order_returns_to_normal_validation_when_authority_comes_back()
    {
        var h = new SettlementHarness();
        h.Accept(ContractA, 100, (OgKush, 5));
        h.Coordinator.ApplySession(SessionDecision.Initial);
        h.Coordinator.SetAuthority(isServer: true, playerCount: 1);

        var validation = h.Coordinator.Validate(ContractA, new[] { Item("0", OgKush, 5) });
        Assert.NotNull(validation);
        Assert.Equal(HandoverHookAction.UseValidation, ManagedHandoverGuard.Decide(true, validation));
    }

    [Fact]
    public void P08_T06_a_failed_return_is_retried_until_it_succeeds()
    {
        var queue = new PendingReturnQueue<object>();
        var item = new object();
        Assert.True(queue.Add(Dealers.Benji, item, 20));
        Assert.Equal(1, queue.Count);
        Assert.Equal(20, queue.Units);

        var failed = queue.Retry(Dealers.Benji, _ => throw new InvalidOperationException("inventory busy"));
        Assert.Equal(0, failed.Returned);
        Assert.Equal(1, failed.StillPending);

        var returnedTo = new List<object>();
        var ok = queue.Retry(Dealers.Benji, i => { returnedTo.Add(i); return true; });
        Assert.Equal(1, ok.Returned);
        Assert.Same(item, Assert.Single(returnedTo));
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.Units);
        Assert.False(queue.HasPending(Dealers.Benji));
    }

    [Fact]
    public void P08_T07_a_return_that_keeps_failing_is_given_up_after_the_attempt_limit_and_counted()
    {
        var queue = new PendingReturnQueue<object>();
        queue.Add(Dealers.Benji, new object(), 7);
        PendingReturnPass last = default;
        var calls = 0;
        for (var i = 0; i < PendingReturnQueue<object>.MaxAttempts; i++)
        {
            last = queue.Retry(Dealers.Benji, _ => { calls++; return false; });
        }

        Assert.Equal(PendingReturnQueue<object>.MaxAttempts, calls);
        Assert.Equal(1, last.Abandoned);
        Assert.Equal(7, last.AbandonedUnits);
        Assert.Equal(1, queue.AbandonedTotal);
        Assert.Equal(0, queue.Count);
        Assert.Equal(default, queue.Retry(Dealers.Benji, _ => true));
    }

    [Fact]
    public void P08_T08_returns_never_go_to_another_dealer()
    {
        var queue = new PendingReturnQueue<object>();
        queue.Add(Dealers.Benji, new object(), 5);

        var other = queue.Retry(Dealers.Molly, _ => true);

        Assert.Equal(0, other.Returned);
        Assert.Equal(1, queue.Count);
        Assert.True(queue.HasPending(Dealers.Benji));
        Assert.False(queue.HasPending(null));
    }

    [Fact]
    public void P08_T09_the_queue_is_bounded_per_dealer_and_clear_drops_everything()
    {
        var queue = new PendingReturnQueue<object>();
        for (var i = 0; i < PendingReturnQueue<object>.MaxItemsPerOwner; i++)
        {
            Assert.True(queue.Add(Dealers.Benji, new object(), 1));
        }

        Assert.False(queue.Add(Dealers.Benji, new object(), 1));
        Assert.True(queue.Add(Dealers.Molly, new object(), 1));
        Assert.Equal(PendingReturnQueue<object>.MaxItemsPerOwner + 1, queue.Count);

        Assert.Equal(PendingReturnQueue<object>.MaxItemsPerOwner + 1, queue.Clear());
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.Units);
        Assert.Throws<ArgumentNullException>(() => queue.Add(null!, new object(), 1));
        Assert.Throws<ArgumentNullException>(() => queue.Add(Dealers.Benji, null!, 1));
    }

    [Fact]
    public void P08_T10_a_partial_retry_keeps_only_the_items_that_failed()
    {
        var queue = new PendingReturnQueue<string>();
        queue.Add(Dealers.Benji, "a", 1);
        queue.Add(Dealers.Benji, "b", 2);
        queue.Add(Dealers.Benji, "c", 4);

        var pass = queue.Retry(Dealers.Benji, item => item != "b");

        Assert.Equal(2, pass.Returned);
        Assert.Equal(1, pass.StillPending);
        Assert.Equal(2, queue.Units);
    }
}

/// <summary>Phase 08 hardening, suite U: runtime-safety reporting.</summary>
public sealed class RuntimeSafetyTests
{
    [Fact]
    public void P08_U01_pending_returns_appear_in_diagnostics_only_when_present()
    {
        var status = new DealerSalesStatus();
        Assert.DoesNotContain(status.Lines(), l => l.StartsWith("Items waiting to go back", StringComparison.Ordinal));

        status.PendingReturnItems = 2;
        status.PendingReturnUnits = 40;
        status.AbandonedReturns = 1;

        Assert.Contains("Items waiting to go back to dealers: 2 (40 unit(s)); given up: 1", status.Lines());
    }

    [Fact]
    public void P08_U02_the_cannot_check_message_contains_no_internal_detail()
    {
        Assert.DoesNotContain("authority", ManagedHandoverGuard.CannotCheckMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(ManagedHandoverGuard.CannotCheckMessage.Length < 100);
    }

    [Fact]
    public void P08_U03_the_held_check_needs_no_authority_and_does_not_change_it()
    {
        var h = new SettlementHarness();
        h.Accept(ContractA, 100, (OgKush, 5));
        h.Coordinator.ApplySession(SessionDecision.Initial);
        var before = h.Coordinator.AuthorityDetail;

        Assert.True(h.Coordinator.HoldsSnapshot(ContractA));
        Assert.False(h.Coordinator.IsAuthoritative);
        Assert.Equal(before, h.Coordinator.AuthorityDetail);
    }
}
