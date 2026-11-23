using System;

namespace TakeItOrLeaveIt.Core.Products;

/// <summary>Shared validation for identifier text used by <see cref="CategoryKey"/> and <see cref="ProductKey"/>.</summary>
internal static class KeyText
{
    /// <summary>Trims <paramref name="raw"/> and checks length and characters; returns null when invalid.</summary>
    public static string? Normalize(string? raw, int maxLength)
    {
        if (raw is null)
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            return null;
        }

        foreach (var c in trimmed)
        {
            if (char.IsControl(c))
            {
                return null;
            }
        }

        return trimmed;
    }

    public static ArgumentException Invalid(string? raw, string what)
        => new($"'{raw ?? "<null>"}' is not a valid {what}.", nameof(raw));
}
