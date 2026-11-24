using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TakeItOrLeaveIt.Core.Persistence;

/// <summary>What a mod file on disk contains, as far as a reader of this build can tell.</summary>
public enum DocumentState
{
    /// <summary>No file.</summary>
    Missing,

    /// <summary>A readable document of a supported schema that belongs to the expected save.</summary>
    Valid,

    /// <summary>A readable document written by a newer build. Never rewritten, downgraded or trimmed.</summary>
    NewerSchema,

    /// <summary>Not a document of this kind (truncated, damaged, edited by hand, or another save's file).</summary>
    Unreadable,
}

/// <summary>The result of classifying one document's text.</summary>
public readonly struct DocumentCheck
{
    public DocumentCheck(DocumentState state, string? detail = null)
    {
        State = state;
        Detail = detail;
    }

    public DocumentState State { get; }

    public string? Detail { get; }

    public static DocumentCheck Valid => new(DocumentState.Valid);

    public static DocumentCheck Newer(string detail) => new(DocumentState.NewerSchema, detail);

    public static DocumentCheck Unreadable(string detail) => new(DocumentState.Unreadable, detail);
}

/// <summary>Thrown when a write would replace a document written by a newer build of the mod.</summary>
public sealed class NewerSchemaException : IOException
{
    public NewerSchemaException(string message)
        : base(message)
    {
    }
}

/// <summary>What a successful <see cref="SafeDocumentFile.Write"/> did besides replacing the file.</summary>
public readonly struct DocumentWriteOutcome
{
    public DocumentWriteOutcome(string? preservedCorruptCopy, bool backupUpdated)
    {
        PreservedCorruptCopy = preservedCorruptCopy;
        BackupUpdated = backupUpdated;
    }

    /// <summary>Where the unreadable file that was replaced was copied to, or null.</summary>
    public string? PreservedCorruptCopy { get; }

    /// <summary>True when the previous valid document was copied to <c>&lt;file&gt;.bak</c>.</summary>
    public bool BackupUpdated { get; }
}

/// <summary>
/// The one write path for every mod file (settings, player orders, dealer appointments, dealer alerts):
/// <list type="number">
/// <item>the new document must classify as <see cref="DocumentState.Valid"/> (nothing unreadable is ever written);</item>
/// <item>the current file is inspected: a <see cref="DocumentState.NewerSchema"/> file is never replaced
/// (<see cref="NewerSchemaException"/>), an unreadable one is first copied to <c>&lt;file&gt;.corrupt-&lt;UTC time&gt;</c>
/// (at most <see cref="MaxCorruptCopies"/> are kept), and a valid one becomes the known-good <c>&lt;file&gt;.bak</c>;</item>
/// <item>the document goes to <c>&lt;file&gt;.tmp</c>, is flushed to disk, read back and compared;</item>
/// <item>the temporary file replaces the destination in one rename (<c>File.Replace</c> / <c>File.Move</c>).</item>
/// </list>
/// Any failure leaves the previous file untouched and throws; the temporary file is removed when possible.
/// The rename is atomic on the same NTFS volume; it is not a guarantee against power loss, because the
/// directory entry itself is not flushed (Windows offers no portable way to do that). See PHASE_06_ANALYSIS.md.
/// </summary>
public static class SafeDocumentFile
{
    public const string TempSuffix = ".tmp";
    public const string BackupSuffix = ".bak";
    public const string CorruptMarker = ".corrupt-";
    public const int MaxCorruptCopies = 3;

    /// <summary>Classifies the file at <paramref name="path"/>. I/O errors propagate (a file that cannot be read is never assumed bad).</summary>
    public static DocumentCheck Inspect(ISettingsFileSystem fs, string path, Func<string, DocumentCheck> classify)
    {
        if (!fs.FileExists(path))
        {
            return new DocumentCheck(DocumentState.Missing);
        }

        return classify(fs.ReadAllText(path));
    }

    public static DocumentWriteOutcome Write(ISettingsFileSystem fs, string path, string json, Func<string, DocumentCheck> classify, Func<DateTime>? utcNow = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        var own = classify(json);
        if (own.State != DocumentState.Valid)
        {
            throw new InvalidDataException("Refusing to write a document that would not read back: " + (own.Detail ?? own.State.ToString()));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            fs.CreateDirectory(directory!);
        }

        var existing = Inspect(fs, path, classify);
        string? preserved = null;
        switch (existing.State)
        {
            case DocumentState.NewerSchema:
                throw new NewerSchemaException($"{Path.GetFileName(path)} was written by a newer version of the mod ({existing.Detail}); it is not overwritten.");
            case DocumentState.Unreadable:
                preserved = PreserveCorrupt(fs, path, utcNow ?? (() => DateTime.UtcNow));
                break;
        }

        var temp = path + TempSuffix;
        try
        {
            fs.WriteAllText(temp, json);
            if (!string.Equals(fs.ReadAllText(temp), json, StringComparison.Ordinal))
            {
                throw new IOException("The temporary file did not read back identically.");
            }

            if (existing.State == DocumentState.Valid)
            {
                fs.CopyFile(path, path + BackupSuffix);
            }

            fs.ReplaceFile(temp, path);
        }
        catch
        {
            TryDelete(fs, temp);
            throw;
        }

        return new DocumentWriteOutcome(preserved, existing.State == DocumentState.Valid);
    }

    /// <summary>
    /// Copies an unreadable file to <c>&lt;file&gt;.corrupt-yyyyMMddTHHmmssfffZ</c> (a counter is added on a clash) and
    /// deletes the oldest copies beyond <see cref="MaxCorruptCopies"/>. Returns the copy's path.
    /// </summary>
    public static string PreserveCorrupt(ISettingsFileSystem fs, string path, Func<DateTime> utcNow)
    {
        var stamp = utcNow().ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var copy = path + CorruptMarker + stamp;
        for (var n = 2; fs.FileExists(copy); n++)
        {
            copy = path + CorruptMarker + stamp + "-" + n.ToString(CultureInfo.InvariantCulture);
        }

        fs.CopyFile(path, copy);
        Prune(fs, path);
        return copy;
    }

    /// <summary>The preserved copies of <paramref name="path"/>, oldest first.</summary>
    public static string[] CorruptCopies(ISettingsFileSystem fs, string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var prefix = Path.GetFileName(path) + CorruptMarker;
        return fs.ListFiles(directory, prefix)
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();
    }

    private static void Prune(ISettingsFileSystem fs, string path)
    {
        var copies = CorruptCopies(fs, path);
        for (var i = 0; i < copies.Length - MaxCorruptCopies; i++)
        {
            TryDelete(fs, copies[i]);
        }
    }

    private static void TryDelete(ISettingsFileSystem fs, string path)
    {
        try
        {
            fs.DeleteFile(path);
        }
        catch (Exception)
        {
            // best effort; the destination file is untouched either way
        }
    }
}
