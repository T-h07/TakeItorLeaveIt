using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Core.Discovery;

/// <summary>Formats catalog state as log lines for diagnostics. Contains no raw paths.</summary>
public static class CatalogReport
{
    public static IReadOnlyList<string> Describe(CatalogState state, PolicyConfiguration policy, bool includeProducts)
    {
        var lines = new List<string>();
        if (state is null)
        {
            return lines;
        }

        lines.Add($"Product catalog: {state.Status}" + (state.Session.IsNone ? string.Empty : $" [{state.Session}]")
                  + (state.Message is null ? string.Empty : $" - {state.Message}"));

        var snapshot = state.Snapshot;
        if (snapshot is null)
        {
            if (state.Status != CatalogStatus.NoSave)
            {
                lines.Add("  No products are eligible until the catalog is Ready (fail-closed).");
            }

            return lines;
        }

        lines.Add($"  Generation {snapshot.Generation}: {snapshot.ProductCount} product(s) in {snapshot.Categories.Count} category(ies).");
        foreach (var summary in SaleEligibilityPolicy.EvaluateAll(policy, snapshot))
        {
            lines.Add($"  [{summary.Category}] {snapshot.GetProducts(summary.Category).Count} known, {summary.UnlockedCount} unlocked; " +
                      $"mode {PolicyConfiguration.FormatMode(summary.Mode)} -> {summary.EligibleProducts.Count} eligible ({summary.Reason})" +
                      (summary.InactiveSelections.Count > 0 ? $", {summary.InactiveSelections.Count} inactive selection(s)" : string.Empty));

            if (includeProducts)
            {
                foreach (var product in snapshot.GetProducts(summary.Category))
                {
                    var effects = product.EffectIds.Count == 0 ? string.Empty : $" effects: {string.Join(", ", product.EffectIds)}";
                    lines.Add($"    - {product.Key} \"{product.DisplayName}\" {product.UnlockState}{effects}");
                }
            }
        }

        foreach (var issue in snapshot.Issues.Take(20))
        {
            lines.Add($"  issue: {issue}");
        }

        if (snapshot.Issues.Count > 20)
        {
            lines.Add($"  ... {snapshot.Issues.Count - 20} more issue(s).");
        }

        return lines;
    }
}
