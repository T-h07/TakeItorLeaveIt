using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;
using Xunit;
using static TakeItOrLeaveIt.Core.Tests.Advanced;
using static TakeItOrLeaveIt.Core.Tests.Catalog;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Phase 05 suite F: randomized but seeded (deterministic) runs over random rules, mixing modes, quality minimums,
/// dealer overrides and stock. Every reservation and every accepted delivery must respect the order's snapshot:
/// permitted products only, quality at or above the minimum, one combination group when mixing is limited, never
/// more units than the lot holds, and the same inputs always give the same result.
/// </summary>
public class AdvancedRuleStressTests
{
    private static readonly ProductKey[] WeedProducts = { OgKush, SourDiesel, Gdp, MixA, MixB, Hybrid, MixS, Orphan, Blank };
    private static readonly string[] AllEffects = { Calming, Energizing, Paranoia };

    private static T Pick<T>(Random r, IReadOnlyList<T> items) => items[r.Next(items.Count)];

    private static QualityTier? RandomTier(Random r) => r.Next(6) switch
    {
        0 => null,
        var n => (QualityTier)(n - 1),
    };

    private static CategoryPolicy RandomRule(Random r)
    {
        var mode = (PolicyMode)r.Next(4);
        var match = (SelectionMatch)r.Next(2);
        var selected = WeedProducts.Where(_ => r.Next(4) == 0).ToArray();
        var blocked = WeedProducts.Where(_ => r.Next(5) == 0).ToArray();
        var effects = r.Next(3) == 0
            ? new EffectRule((EffectMatch)r.Next(2), AllEffects.Where(_ => r.Next(3) == 0), AllEffects.Where(_ => r.Next(4) == 0))
            : EffectRule.None;
        return new CategoryPolicy(mode, selected, match, blocked, effects, r.Next(3) == 0 ? RandomTier(r) : null);
    }

    private static PolicyConfiguration RandomPolicy(Random r)
    {
        var policy = PolicyConfiguration.Default.WithCategory(Weed, RandomRule(r)).WithMixing((MixingMode)r.Next(4));
        if (r.Next(3) == 0)
        {
            policy = policy.WithDealerCategory(Dealers.Benji, Weed, RandomRule(r));
        }

        return policy;
    }

    private static DealerStock RandomStock(Random r)
        => new(Dealers.Benji, Enumerable.Range(0, r.Next(1, 9))
            .Select(i => new InventoryStack($"slot:{i}", Pick(r, WeedProducts), r.Next(1, 8), RandomTier(r))));

    [Fact]
    public void P05_F01_random_rules_never_reserve_or_accept_outside_the_snapshot()
    {
        var catalog = Snapshot();
        var random = new Random(20261124);
        var reservations = 0;
        for (var round = 0; round < 1500; round++)
        {
            var policy = RandomPolicy(random);
            var stock = RandomStock(random);
            var requested = Pick(random, WeedProducts);
            var request = new OrderRequest(Dealers.Contract(round + 1), false, new[] { new OrderLine(requested.Value, random.Next(1, 10)) }, RandomTier(random));
            var creation = CategoryOrderFactory.CreateForDealer(request, policy, catalog, Prices, Dealers.Benji);
            if (!creation.IsManaged)
            {
                continue;
            }

            var order = creation.Order!;
            var scoped = policy.ForScope(SalesScope.Dealer(Dealers.Benji));

            // Snapshot: only products the dealer's effective rules permit.
            foreach (var product in order.AllowedProducts)
            {
                Assert.True(SaleEligibilityPolicy.Evaluate(scoped, catalog, Weed, product).IsEligible, $"round {round}: {product} not permitted");
            }

            // Quality minimum is never below the customer's own standard.
            Assert.True(QualityTiers.Meets(order.MinimumQuality, request.RequiredQuality) || request.RequiredQuality is QualityTier.Trash,
                $"round {round}: minimum {order.MinimumQuality} below customer {request.RequiredQuality}");

            var ledger = new ReservationLedger();
            var result = ledger.TryReserve(order, stock, catalog);
            var again = new ReservationLedger().TryReserve(order, stock, catalog);
            Assert.Equal(result.Outcome, again.Outcome);
            Assert.Equal(result.Reservation?.Lines.Select(l => l.ToString()), again.Reservation?.Lines.Select(l => l.ToString()));
            if (result.Outcome != ReserveOutcome.Reserved)
            {
                Assert.Null(result.Reservation);
                Assert.Empty(ledger.CheckInvariants(_ => stock));
                continue;
            }

            reservations++;
            var lines = result.Reservation!.Lines;
            Assert.Equal(order.RequiredUnits, lines.Sum(l => l.Units));
            Assert.All(lines, l => Assert.True(order.IsAllowed(l.Product)));
            Assert.All(lines, l => Assert.True(QualityTiers.Meets(l.Quality, order.MinimumQuality)));
            Assert.All(lines, l => Assert.True(l.Units <= stock.UnitsOf(l.Lot)));
            Assert.True(order.CanCombine(lines.Select(l => l.Product)), $"round {round}: reservation crosses groups");
            Assert.Empty(ledger.CheckInvariants(_ => stock));

            // The handover of exactly the reserved lots is accepted by the snapshot.
            var delivered = lines.Select((l, i) => (DeliveredItem?)new DeliveredItem($"d{i}", l.Product.Value, l.Units, 1, true, l.Quality)).ToList();
            var validation = DeliveryValidator.Validate(order, delivered, catalog);
            Assert.True(validation.IsValid, $"round {round}: {validation.Rejection} {validation.Message}");
        }

        Assert.True(reservations > 100, $"only {reservations} reservations: the generator is too strict to be useful");
    }

    [Fact]
    public void P05_F02_random_deliveries_are_judged_only_by_the_snapshot()
    {
        var catalog = Snapshot();
        var random = new Random(5);
        for (var round = 0; round < 1500; round++)
        {
            var policy = RandomPolicy(random);
            var request = Sales.Request(Sales.ContractA, (Pick(random, WeedProducts), random.Next(1, 6)));
            var creation = CategoryOrderFactory.Create(request, policy, catalog, Prices);
            if (!creation.IsManaged)
            {
                continue;
            }

            var order = creation.Order!;
            var items = Enumerable.Range(0, random.Next(1, 4))
                .Select(i => (DeliveredItem?)new DeliveredItem($"s{i}", Pick(random, WeedProducts).Value, random.Next(1, 6), 1, true, RandomTier(random)))
                .ToList();
            var products = items.Select(i => ProductKey.Create(i!.ProductId!)).Distinct().ToList();
            var units = items.Sum(i => (long)i!.Units);

            var validation = DeliveryValidator.Validate(order, items, catalog);

            var expected = units == order.RequiredUnits
                           && products.All(order.IsAllowed)
                           && items.All(i => QualityTiers.Meets(i!.Quality, order.MinimumQuality))
                           && (products.Count == 1 || order.CanCombine(products))
                           && (order.PricingBasis != PricingBasis.Priced || order.QuotedValue is null ||
                               items.Sum(i => order.TryGetUnitPrice(ProductKey.Create(i!.ProductId!), out var p) ? p * i.Units : 0m) >= order.QuotedValue);
            Assert.True(expected == validation.IsValid, $"round {round}: expected {expected}, got {validation.Rejection} for {order}");

            // Changing the live rules afterwards never changes the decision.
            var later = DeliveryValidator.Validate(order, items, catalog, _ => "explanation only");
            Assert.Equal(validation.IsValid, later.IsValid);
        }
    }
}
