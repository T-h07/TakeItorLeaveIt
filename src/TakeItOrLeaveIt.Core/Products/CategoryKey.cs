using System;

namespace TakeItOrLeaveIt.Core.Products;

/// <summary>
/// Identity of a drug category (for Schedule I: the name of the game's drug-type enum value,
/// e.g. "marijuana"). Comparison is case-insensitive; the value is stored lower-case so it can be
/// persisted and compared reliably. <c>default</c> is an invalid key.
/// </summary>
public readonly struct CategoryKey : IEquatable<CategoryKey>, IComparable<CategoryKey>
{
    public const int MaxLength = 64;

    private readonly string? _value;

    private CategoryKey(string value)
    {
        _value = value;
    }

    /// <summary>Normalised (lower-case) key text, or empty for an invalid key.</summary>
    public string Value => _value ?? string.Empty;

    public bool IsValid => _value is not null;

    public static bool TryCreate(string? raw, out CategoryKey key)
    {
        var normalized = KeyText.Normalize(raw, MaxLength);
        key = normalized is null ? default : new CategoryKey(normalized.ToLowerInvariant());
        return normalized is not null;
    }

    public static CategoryKey Create(string? raw)
        => TryCreate(raw, out var key) ? key : throw KeyText.Invalid(raw, "category key");

    public bool Equals(CategoryKey other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CategoryKey other && Equals(other);

    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    public int CompareTo(CategoryKey other) => string.CompareOrdinal(_value, other._value);

    public override string ToString() => _value ?? "<invalid category>";

    public static bool operator ==(CategoryKey a, CategoryKey b) => a.Equals(b);

    public static bool operator !=(CategoryKey a, CategoryKey b) => !a.Equals(b);
}
