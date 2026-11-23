using System;
using System.Collections.Generic;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>One validated stack and how well it meets the order's quality (0..1, recipe ignored).</summary>
public readonly struct RatedUnits
{
    public RatedUnits(long units, float quality)
    {
        Units = units;
        Quality = quality;
    }

    public long Units { get; }

    /// <summary>The game's similarity of the item to its own product at the requested quality.</summary>
    public float Quality { get; }
}

/// <summary>The value returned to the game in place of the exact-recipe match.</summary>
public readonly struct MatchResult
{
    public MatchResult(float match, int matchedUnits)
    {
        Match = match;
        MatchedUnits = matchedUnits;
    }

    /// <summary>0..1, like the vanilla match.</summary>
    public float Match { get; }

    public int MatchedUnits { get; }
}

/// <summary>
/// Computes the match for a delivery that passed <see cref="DeliveryValidator"/>: the unit-weighted average of
/// each stack's quality rating, so the recipe-mismatch penalty disappears while quality still counts.
/// Invalid deliveries always score 0 with no matched units.
/// </summary>
public static class DeliveryMatch
{
    public static MatchResult Compute(DeliveryValidation validation, IEnumerable<RatedUnits> ratings)
    {
        if (validation is null)
        {
            throw new ArgumentNullException(nameof(validation));
        }

        if (!validation.IsValid)
        {
            return new MatchResult(0f, 0);
        }

        var required = validation.Order.RequiredUnits;
        double weighted = 0;
        foreach (var rating in ratings ?? Array.Empty<RatedUnits>())
        {
            if (rating.Units <= 0)
            {
                continue;
            }

            var quality = float.IsNaN(rating.Quality) ? 0f : Math.Max(0f, Math.Min(1f, rating.Quality));
            weighted += rating.Units * (double)quality;
        }

        var match = (float)Math.Max(0d, Math.Min(1d, weighted / required));
        return new MatchResult(match, required);
    }
}
