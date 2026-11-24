using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TakeItOrLeaveIt.Core.Diagnostics;

/// <summary>
/// Makes text safe to write to <c>Latest.log</c> (Phase 08 hardening): absolute file paths are reduced to their file
/// name, so a log a player shares does not reveal their Windows user name or folder layout; control characters from
/// untrusted input are replaced; long values are clipped so a damaged or hostile file cannot flood the log.
/// </summary>
public static class LogText
{
    /// <summary>Default length for one untrusted value inside a message.</summary>
    public const int MaxValueLength = 120;

    /// <summary>Upper bound for one whole log entry (stack traces included).</summary>
    public const int MaxEntryLength = 8_000;

    private const string Ellipsis = "…";

    // Drive paths (C:\… or C:/…), UNC paths (\\server\share\…) and POSIX home/user paths. A quoted path may contain
    // spaces ("C:\Users\Jane Doe\…"), so quoted paths are matched first, up to the closing quote; an unquoted one stops
    // at whitespace. Only the last segment (the file name) is kept.
    private const string Root =
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\[^\\/\s'""]+[\\/]|/(?:home|Users|root|mnt|media|tmp|var|private)/)";

    private static readonly Regex QuotedPath = new(
        @"(?<q>['""])(?<p>" + Root + @"[^'""\r\n]*)\k<q>", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UnquotedPath = new(
        Root + @"[^\s'""<>|]*", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Replaces every absolute path in <paramref name="text"/> with <c>&lt;path&gt;\file-name</c>.</summary>
    public static string RedactPaths(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Measured fast path (docs/PERFORMANCE.md): every path root above contains ":\", ":/", "\\" or a "/" followed
        // by a POSIX root name; without any of them the regexes cannot match.
        if (!MayContainPath(text!))
        {
            return text!;
        }

        var quoted = QuotedPath.Replace(text!, m => m.Groups["q"].Value + Shorten(m.Groups["p"].Value) + m.Groups["q"].Value);
        return UnquotedPath.Replace(quoted, m =>
        {
            if (m.Value.StartsWith("<path>", StringComparison.Ordinal))
            {
                return m.Value;
            }

            var value = m.Value.TrimEnd('.', ',', ';', ':', ')');
            return Shorten(value) + m.Value.Substring(value.Length);
        });
    }

    /// <summary>
    /// An untrusted value for a log line or a diagnostics text: control characters (line breaks included) become
    /// spaces and the result is clipped to <paramref name="maxLength"/> characters.
    /// </summary>
    public static string Clip(string? value, int maxLength = MaxValueLength)
    {
        if (maxLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        if (value is null)
        {
            return string.Empty;
        }

        var length = Math.Min(value.Length, maxLength);
        var builder = new StringBuilder(length + 1);
        for (var i = 0; i < length; i++)
        {
            var c = value[i];
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        if (value.Length > maxLength)
        {
            builder.Append(Ellipsis);
        }

        return builder.ToString();
    }

    /// <summary>"<c>ExceptionType: message</c>" with paths redacted and the message clipped. For one-line warnings.</summary>
    public static string Describe(Exception? exception)
        => exception is null ? "(no exception)" : exception.GetType().Name + ": " + Clip(RedactPaths(exception.Message), 300);

    /// <summary>A whole log entry (message plus optional exception with its stack trace), redacted and bounded.</summary>
    public static string Entry(string? message, Exception? exception = null)
    {
        var text = exception is null ? message ?? string.Empty : (message ?? string.Empty) + System.Environment.NewLine + exception;
        text = RedactPaths(text);
        return text.Length <= MaxEntryLength ? text : text.Substring(0, MaxEntryLength) + Ellipsis + " (truncated)";
    }

    private static bool MayContainPath(string text)
        => text.IndexOf(":\\", StringComparison.Ordinal) >= 0
           || text.IndexOf(":/", StringComparison.Ordinal) >= 0
           || text.IndexOf("\\\\", StringComparison.Ordinal) >= 0
           || text.IndexOf('/') >= 0;

    private static string Shorten(string path)
    {
        var value = path.TrimEnd('\\', '/');
        var cut = value.LastIndexOfAny(new[] { '\\', '/' });
        var name = cut >= 0 ? value.Substring(cut + 1) : string.Empty;
        return name.Length == 0 ? "<path>" : "<path>" + (value.IndexOf('\\') >= 0 ? "\\" : "/") + name;
    }
}
