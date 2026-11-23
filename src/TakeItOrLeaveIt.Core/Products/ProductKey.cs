using System;

namespace TakeItOrLeaveIt.Core.Products;

/// <summary>
/// Stable identity of one exact product (for Schedule I: the product definition's item ID; every
/// mixed product has its own ID). Comparison is ordinal and case-sensitive because it is the game's
/// own identifier. Display names are never used as identity. <c>default</c> is an invalid key.
/// </summary>
public readonly struct ProductKey : IEquatable<ProductKey>, IComparable<ProductKey>
{
    public const int MaxLength = 256;

    private readonly string? _value;

    private ProductKey(string value)
    {
        _value = value;
    }

    public string Value => _value ?? string.Empty;

    public bool IsValid => _value is not null;

    public static bool TryCreate(string? raw, out ProductKey key)
    {
        var normalized = KeyText.Normalize(raw, MaxLength);
        key = normalized is null ? default : new ProductKey(normalized);
        return normalized is not null;
    }

    public static ProductKey Create(string? raw)
        => TryCreate(raw, out var key) ? key : throw KeyText.Invalid(raw, "product key");

    public bool Equals(ProductKey other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ProductKey other && Equals(other);

    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    public int CompareTo(ProductKey other) => string.CompareOrdinal(_value, other._value);

    public override string ToString() => _value ?? "<invalid product>";

    public static bool operator ==(ProductKey a, ProductKey b) => a.Equals(b);

    public static bool operator !=(ProductKey a, ProductKey b) => !a.Equals(b);
}
