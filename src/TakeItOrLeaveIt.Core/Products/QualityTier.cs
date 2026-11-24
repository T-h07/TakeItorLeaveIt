using System;

namespace TakeItOrLeaveIt.Core.Products;

/// <summary>
/// The game's item quality tiers (<c>ScheduleOne.ItemFramework.EQuality</c>), in the game's order. Quality belongs to
/// a physical stack, never to a product definition.
/// </summary>
public enum QualityTier
{
    Trash = 0,
    Poor = 1,
    Standard = 2,
    Premium = 3,
    Heavenly = 4,
}

public static class QualityTiers
{
    public const QualityTier Lowest = QualityTier.Trash;

    public const QualityTier Highest = QualityTier.Heavenly;

    /// <summary>Maps the game's integer tier; anything outside Trash..Heavenly is unknown (null).</summary>
    public static QualityTier? FromGame(int value)
        => value >= (int)Lowest && value <= (int)Highest ? (QualityTier)value : null;

    /// <summary>"standard" style id used in settings files.</summary>
    public static string Id(QualityTier tier) => tier switch
    {
        QualityTier.Trash => "trash",
        QualityTier.Poor => "poor",
        QualityTier.Standard => "standard",
        QualityTier.Premium => "premium",
        _ => "heavenly",
    };

    public static bool TryParse(string? text, out QualityTier tier)
    {
        foreach (QualityTier candidate in Enum.GetValues(typeof(QualityTier)))
        {
            if (string.Equals(text?.Trim(), Id(candidate), StringComparison.OrdinalIgnoreCase))
            {
                tier = candidate;
                return true;
            }
        }

        tier = QualityTier.Trash;
        return false;
    }

    public static string Label(QualityTier tier) => tier.ToString();

    /// <summary>Whether a stack of <paramref name="quality"/> meets <paramref name="minimum"/>. No minimum accepts anything,
    /// a minimum with an unknown quality accepts nothing (fail closed).</summary>
    public static bool Meets(QualityTier? quality, QualityTier? minimum)
        => minimum is null || (quality is not null && quality.Value >= minimum.Value);

    public static QualityTier? Max(QualityTier? a, QualityTier? b)
        => a is null ? b : b is null ? a : (QualityTier)Math.Max((int)a.Value, (int)b.Value);
}
