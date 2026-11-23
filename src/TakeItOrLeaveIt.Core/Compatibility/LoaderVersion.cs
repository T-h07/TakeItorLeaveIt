using System;
using System.Globalization;

namespace TakeItOrLeaveIt.Core.Compatibility;

/// <summary>
/// Tolerant <c>major.minor.patch</c> version parser for loader version strings such as
/// <c>0.7.3</c>, <c>v0.7.3</c>, <c>0.7.3-ci.1234</c> or <c>0.7.3 Open-Beta</c>.
/// Only the numeric core is compared; suffixes are preserved in <see cref="Original"/>.
/// </summary>
public readonly struct LoaderVersion : IComparable<LoaderVersion>, IEquatable<LoaderVersion>
{
    public LoaderVersion(int major, int minor, int patch, string? original = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Original = original ?? $"{major}.{minor}.{patch}";
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string Original { get; }

    public static bool TryParse(string? text, out LoaderVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text!.Trim();
        var core = trimmed.TrimStart('v', 'V');

        // Cut at the first character that is not a digit or '.' (handles "-ci.1", " Open-Beta", "+meta").
        var end = 0;
        while (end < core.Length && (char.IsDigit(core[end]) || core[end] == '.'))
        {
            end++;
        }

        var parts = core.Substring(0, end).Trim('.').Split('.');
        if (parts.Length < 2 || parts.Length > 4)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length)
            {
                numbers[i] = 0;
                continue;
            }

            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new LoaderVersion(numbers[0], numbers[1], numbers[2], trimmed);
        return true;
    }

    public int CompareTo(LoaderVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public bool Equals(LoaderVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is LoaderVersion other && Equals(other);

    public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator <(LoaderVersion a, LoaderVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(LoaderVersion a, LoaderVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(LoaderVersion a, LoaderVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(LoaderVersion a, LoaderVersion b) => a.CompareTo(b) >= 0;

    public static bool operator ==(LoaderVersion a, LoaderVersion b) => a.Equals(b);

    public static bool operator !=(LoaderVersion a, LoaderVersion b) => !a.Equals(b);
}
