using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Inventory;

/// <summary>
/// Plans which stacks could fulfil "N units of category C" under the active rules.
/// <para><b>Pure and non-mutating:</b> it reads the stack descriptors and returns a plan. It never
/// removes, reserves or modifies anything.</para>
/// <para><b>Deterministic strategy:</b>
/// (1) drop invalid stacks, and every occurrence of a duplicated stack id;
/// (2) keep stacks whose product passes <see cref="SaleEligibilityPolicy.Evaluate"/> and whose quality passes
/// <see cref="SaleEligibilityPolicy.CheckQuality"/> (Phase 05);
/// (3) sort the eligible stacks by stack id (ordinal), so input order never matters;
/// (4) take <c>min(remaining, stack quantity)</c> from each in that order until the request is met.
/// Mixed products are combined automatically when more than one is eligible.</para>
/// </summary>
public static class InventoryAllocationPlanner
{
    public static AllocationPlan Plan(
        CategoryKey category,
        int requestedQuantity,
        IEnumerable<InventoryStack?>? stacks,
        PolicyConfiguration policy,
        ProductCatalogSnapshot? snapshot)
    {
        policy ??= PolicyConfiguration.Default;
        var generation = snapshot?.Generation;
        var noAllocations = Array.Empty<StackAllocation>();

        if (requestedQuantity <= 0)
        {
            return new AllocationPlan(category, requestedQuantity, AllocationFailure.InvalidQuantity, EligibilityReason.InvalidQuantity,
                0, noAllocations, Array.Empty<StackRejection>(), generation, policy.Revision);
        }

        var categoryState = SaleEligibilityPolicy.EvaluateCategory(policy, snapshot, category);
        if (!categoryState.HasEligibleProducts)
        {
            return new AllocationPlan(category, requestedQuantity, AllocationFailure.CategoryNotEligible, categoryState.Reason,
                0, noAllocations, Array.Empty<StackRejection>(), generation, policy.Revision);
        }

        var rejections = new List<StackRejection>();
        var valid = new List<InventoryStack>();
        var input = (stacks ?? Array.Empty<InventoryStack?>()).ToList();

        // Stack ids that occur more than once are ambiguous: none of their occurrences may be counted.
        var duplicateIds = new HashSet<string>(
            input.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.StackId))
                 .GroupBy(s => s!.StackId, StringComparer.Ordinal)
                 .Where(g => g.Count() > 1)
                 .Select(g => g.Key),
            StringComparer.Ordinal);

        foreach (var stack in input)
        {
            if (stack is null || string.IsNullOrWhiteSpace(stack.StackId) || !stack.Product.IsValid)
            {
                rejections.Add(new StackRejection(stack?.StackId, StackRejectionReason.InvalidStack, null));
            }
            else if (duplicateIds.Contains(stack.StackId))
            {
                rejections.Add(new StackRejection(stack.StackId, StackRejectionReason.DuplicateStackId, null));
            }
            else if (stack.Quantity <= 0)
            {
                rejections.Add(new StackRejection(stack.StackId, StackRejectionReason.InvalidQuantity, null));
            }
            else
            {
                var decision = SaleEligibilityPolicy.Evaluate(policy, snapshot, category, stack.Product);
                var reason = decision.IsEligible ? SaleEligibilityPolicy.CheckQuality(policy, category, stack.Quality) : decision.Reason;
                if (reason == EligibilityReason.Allowed)
                {
                    valid.Add(stack);
                }
                else
                {
                    rejections.Add(new StackRejection(stack.StackId, StackRejectionReason.Ineligible, reason));
                }
            }
        }

        valid.Sort((a, b) => string.CompareOrdinal(a.StackId, b.StackId));

        long available = 0;
        foreach (var stack in valid)
        {
            available = checked(available + stack.Quantity);
        }

        var orderedRejections = rejections
            .OrderBy(r => r.StackId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.Reason)
            .ToArray();

        if (available < requestedQuantity)
        {
            var failure = available == 0 ? AllocationFailure.NoEligibleStock : AllocationFailure.InsufficientEligibleQuantity;
            return new AllocationPlan(category, requestedQuantity, failure, EligibilityReason.Allowed,
                available, noAllocations, orderedRejections, generation, policy.Revision);
        }

        var allocations = new List<StackAllocation>();
        var remaining = requestedQuantity;
        foreach (var stack in valid)
        {
            if (remaining == 0)
            {
                break;
            }

            var take = Math.Min(remaining, stack.Quantity);
            allocations.Add(new StackAllocation(stack.StackId, stack.Product, take, stack.Quality));
            remaining -= take;
        }

        return new AllocationPlan(category, requestedQuantity, AllocationFailure.None, EligibilityReason.Allowed,
            available, allocations, orderedRejections, generation, policy.Revision);
    }
}
