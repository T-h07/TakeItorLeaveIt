using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Core.Persistence;

public enum PolicyLoadStatus
{
    /// <summary>No settings saved for this save yet: defaults apply.</summary>
    NotFound,

    Loaded,

    /// <summary>
    /// The file exists but could not be read or parsed (or belongs to another save). Mod sales stay off and the file
    /// is preserved; <see cref="PolicyLoadResult.Recovery"/> may offer the last known-good backup.
    /// </summary>
    Unreadable,

    /// <summary>Phase 06: written by a newer build. Never rewritten or downgraded; mod sales stay off.</summary>
    NewerSchema,
}

public sealed class PolicyLoadResult
{
    public PolicyLoadResult(PolicyLoadStatus status, PolicySettings? settings, string? message, PolicySettings? recovery = null, string? recoveryNote = null)
    {
        Status = status;
        Settings = settings;
        Message = message;
        Recovery = recovery;
        RecoveryNote = recoveryNote;
    }

    public PolicyLoadStatus Status { get; }

    public PolicySettings? Settings { get; }

    public string? Message { get; }

    /// <summary>
    /// Only with <see cref="PolicyLoadStatus.Unreadable"/>: the verified known-good backup (<c>&lt;file&gt;.bak</c>, same save,
    /// supported schema). It is offered, never applied automatically.
    /// </summary>
    public PolicySettings? Recovery { get; }

    public string? RecoveryNote { get; }
}

/// <summary>Per-save settings storage. Implementations must not touch vanilla save files.</summary>
public interface IPolicyStore
{
    PolicyLoadResult Load(string saveIdentity);

    /// <summary>Writes atomically. Throws on failure; callers report success only if this returns.</summary>
    void Save(string saveIdentity, PolicySettings settings);
}

/// <summary>File operations used by <see cref="JsonPolicyStore"/> (replaceable in tests to simulate failures).</summary>
public interface ISettingsFileSystem
{
    bool FileExists(string path);

    string ReadAllText(string path);

    void WriteAllText(string path, string contents);

    /// <summary>Atomically moves <paramref name="source"/> over <paramref name="destination"/>.</summary>
    void ReplaceFile(string source, string destination);

    void CopyFile(string source, string destination);

    void DeleteFile(string path);

    void CreateDirectory(string path);

    /// <summary>Full paths of the files in <paramref name="directory"/> whose name starts with <paramref name="prefix"/> (none if the folder is missing).</summary>
    IEnumerable<string> ListFiles(string directory, string prefix);
}

public sealed class PhysicalSettingsFileSystem : ISettingsFileSystem
{
    public static readonly PhysicalSettingsFileSystem Instance = new();

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

    public void WriteAllText(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var bytes = new UTF8Encoding(false).GetBytes(contents);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    public void ReplaceFile(string source, string destination)
    {
        if (File.Exists(destination))
        {
            File.Replace(source, destination, null);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    public void CopyFile(string source, string destination) => File.Copy(source, destination, true);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<string> ListFiles(string directory, string prefix)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory).Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal)).ToArray()
            : Array.Empty<string>();
}

/// <summary>
/// Stores one JSON file per save under <c>&lt;root&gt;/saves/&lt;save identity&gt;.json</c> (the identity is an
/// anonymised hash, see <see cref="Discovery.SaveIdentity"/>). Every write goes through <see cref="SafeDocumentFile"/>:
/// temporary file, flush, read-back check, one rename; the previous valid file becomes <c>&lt;file&gt;.bak</c>, an
/// unreadable one is kept as <c>&lt;file&gt;.corrupt-&lt;time&gt;</c>, and a file of a newer schema is never replaced.
/// Before the first schema 2 write over an older file, a one-time <c>&lt;file&gt;.schema&lt;n&gt;.bak</c> is kept.
/// Nothing is ever written into the game's own save folders.
/// </summary>
public sealed class JsonPolicyStore : IPolicyStore
{
    private readonly string _directory;
    private readonly ISettingsFileSystem _fs;
    private readonly Func<DateTime> _utcNow;

    public JsonPolicyStore(string rootDirectory, ISettingsFileSystem? fileSystem = null, Func<DateTime>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Settings directory is required.", nameof(rootDirectory));
        }

        _directory = Path.Combine(rootDirectory, "saves");
        _fs = fileSystem ?? PhysicalSettingsFileSystem.Instance;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string PathFor(string saveIdentity) => Path.Combine(_directory, SaveFileNames.Check(saveIdentity) + ".json");

    public PolicyLoadResult Load(string saveIdentity)
    {
        var path = PathFor(saveIdentity);
        string text;
        try
        {
            if (!_fs.FileExists(path))
            {
                return new PolicyLoadResult(PolicyLoadStatus.NotFound, null, null);
            }

            text = _fs.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file may be fine (locked by another program): no recovery is offered, nothing is replaced.
            return new PolicyLoadResult(PolicyLoadStatus.Unreadable, null, $"Settings file could not be opened ({LogText.Describe(ex)}).");
        }

        var check = PolicySettingsJson.Classify(text, saveIdentity);
        switch (check.State)
        {
            case DocumentState.Valid:
                return new PolicyLoadResult(PolicyLoadStatus.Loaded, PolicySettingsJson.FromJson(text), null);
            case DocumentState.NewerSchema:
                return new PolicyLoadResult(PolicyLoadStatus.NewerSchema, null,
                    $"Settings were written by a newer version of the mod ({check.Detail}); they are kept unchanged and mod sales stay off.");
            default:
                var message = $"Settings file is damaged ({check.Detail}).";
                var backup = TryBackup(path, saveIdentity, out var note);
                return new PolicyLoadResult(PolicyLoadStatus.Unreadable, null, message, backup, note);
        }
    }

    public void Save(string saveIdentity, PolicySettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var path = PathFor(saveIdentity);
        var json = PolicySettingsJson.ToJson(settings, saveIdentity);
        _fs.CreateDirectory(_directory);
        PreserveOlderSchema(path, settings.SchemaVersion);
        SafeDocumentFile.Write(_fs, path, json, text => PolicySettingsJson.Classify(text, saveIdentity), _utcNow);
    }

    /// <summary>The known-good backup when it is readable, of a supported schema and written for this save.</summary>
    private PolicySettings? TryBackup(string path, string saveIdentity, out string? note)
    {
        note = null;
        var backup = path + SafeDocumentFile.BackupSuffix;
        try
        {
            if (!_fs.FileExists(backup))
            {
                return null;
            }

            var text = _fs.ReadAllText(backup);
            var check = PolicySettingsJson.Classify(text, saveIdentity);
            var settings = check.State == DocumentState.Valid ? PolicySettingsJson.FromJson(text) : null;
            if (settings is null || !string.Equals(settings.Save, saveIdentity, StringComparison.Ordinal))
            {
                note = "The backup could not be verified for this save; it was not used.";
                return null;
            }

            note = "The last settings saved before the damage were found in the backup.";
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            note = $"The backup could not be read ({ex.GetType().Name}).";
            return null;
        }
    }

    /// <summary>
    /// Before a newer schema replaces an older file for the first time, the older file is copied once to
    /// <c>&lt;file&gt;.schema&lt;n&gt;.bak</c> (never overwritten), so the Phase 01-04 settings stay recoverable.
    /// </summary>
    private void PreserveOlderSchema(string path, int newSchema)
    {
        if (!_fs.FileExists(path))
        {
            return;
        }

        try
        {
            var existing = PolicySettingsJson.FromJson(_fs.ReadAllText(path));
            var backup = $"{path}.schema{existing.SchemaVersion}.bak";
            if (existing.SchemaVersion < newSchema && !_fs.FileExists(backup))
            {
                _fs.CopyFile(path, backup);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            // unreadable files are preserved by SafeDocumentFile (corrupt copy)
        }
    }
}

/// <summary>Validation of save identities used in file names.</summary>
public static class SaveFileNames
{
    /// <summary>True for a short token of letters, digits, '-' and '_' (no separators, dots or drive letters).</summary>
    public static bool IsValid(string? saveIdentity)
        => !string.IsNullOrWhiteSpace(saveIdentity) && saveIdentity!.Length <= 64 &&
           saveIdentity.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

    public static string Check(string saveIdentity)
        => IsValid(saveIdentity) ? saveIdentity : throw new ArgumentException("Save identity must be a short file-safe token.", nameof(saveIdentity));
}

/// <summary>Maps <see cref="PolicySettings"/> to and from JSON (shape documented in MOD_MENU_SPEC.md).</summary>
public static class PolicySettingsJson
{
    /// <summary>
    /// Deterministic output: keys and lists are sorted (ordinal), so the same rules always give the same text.
    /// <paramref name="saveIdentity"/> (or <see cref="PolicySettings.Save"/>) is written as <c>save</c>.
    /// </summary>
    public static string ToJson(PolicySettings settings, string? saveIdentity = null)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = settings.SchemaVersion,
            ["enabled"] = settings.Enabled,
            ["defaultMode"] = settings.DefaultMode,
            ["mixedDeliveries"] = settings.MixedDeliveries,
            ["categories"] = WriteCategories(settings.Categories, settings.SchemaVersion >= 2),
        };

        if ((saveIdentity ?? settings.Save) is { } save)
        {
            root["save"] = save;
        }

        if (settings.SchemaVersion >= 2)
        {
            root["mixing"] = settings.Mixing;
            root["playerSales"] = settings.PlayerSales;
            root["dealerSales"] = settings.DealerSales;
            var dealers = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in (settings.Dealers ?? new Dictionary<string, DealerPolicySettings>()).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                dealers[pair.Key] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["categories"] = WriteCategories(pair.Value?.Categories, true),
                };
            }

            root["dealers"] = dealers;
        }

        return MiniJson.Write(root);
    }

    private static Dictionary<string, object?> WriteCategories(Dictionary<string, CategoryPolicySettings>? input, bool advanced)
    {
        var categories = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in (input ?? new Dictionary<string, CategoryPolicySettings>()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var body = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = pair.Value?.Mode,
                ["selectedProducts"] = Strings(pair.Value?.SelectedProducts),
            };
            if (advanced)
            {
                body["match"] = pair.Value?.Match ?? "exact";
                body["blockedProducts"] = Strings(pair.Value?.BlockedProducts);
                body["minQuality"] = pair.Value?.MinQuality;
                if (pair.Value?.Effects is { } effects)
                {
                    body["effects"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["match"] = effects.Match ?? "all",
                        ["required"] = Strings(effects.Required),
                        ["excluded"] = Strings(effects.Excluded),
                    };
                }
            }

            categories[pair.Key] = body;
        }

        return categories;
    }

    private static List<object?> Strings(List<string>? values)
        => (values ?? new List<string>()).OrderBy(v => v, StringComparer.Ordinal).Cast<object?>().ToList();

    /// <summary>A string field; any other non-null type becomes <see cref="InvalidText"/>, which every parser rejects.</summary>
    private static string? ReadText(Dictionary<string, object?> body, string name)
        => body.TryGetValue(name, out var raw) && raw is not null ? raw as string ?? InvalidText : null;

    /// <summary>Not a valid mode, match, mixing or quality id: makes the reader fail closed for a wrongly typed field.</summary>
    internal const string InvalidText = "<invalid type>";

    private static readonly string[] KnownRoot = { "schemaVersion", "save", "enabled", "defaultMode", "mixedDeliveries", "mixing", "playerSales", "dealerSales", "categories", "dealers" };
    private static readonly string[] KnownCategory = { "mode", "selectedProducts", "match", "blockedProducts", "minQuality", "effects" };
    private static readonly string[] KnownEffects = { "match", "required", "excluded" };
    private static readonly string[] KnownDealer = { "categories" };

    private static void Unknown(Dictionary<string, object?> body, string[] known, string path, List<string>? unknown)
    {
        if (unknown is null)
        {
            return;
        }

        foreach (var key in body.Keys.Where(k => !known.Contains(k, StringComparer.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
        {
            unknown.Add(path + key);
        }
    }

    /// <summary>A string list; a wrongly typed list or item marks the field damaged (the category is then Disabled).</summary>
    private static List<string> ReadStrings(Dictionary<string, object?> body, string name, CategoryPolicySettings entry, string field)
    {
        if (!body.TryGetValue(name, out var raw) || raw is null)
        {
            return new List<string>();
        }

        if (raw is not List<object?> items || items.Any(i => i is not string))
        {
            (entry.Damaged ??= new List<string>()).Add(field);
            return raw is List<object?> list ? list.OfType<string>().ToList() : new List<string>();
        }

        return items.Cast<string>().ToList();
    }

    private static bool ReadBool(Dictionary<string, object?> root, string name)
    {
        if (!root.TryGetValue(name, out var raw) || raw is null)
        {
            return true;
        }

        return raw is bool value ? value : throw new InvalidDataException(name + " must be true or false.");
    }

    private static Dictionary<string, CategoryPolicySettings> ReadCategories(object? cats, string path, List<string> unknown)
    {
        var result = new Dictionary<string, CategoryPolicySettings>(StringComparer.Ordinal);
        if (cats is null)
        {
            return result;
        }

        if (cats is not Dictionary<string, object?> map)
        {
            throw new InvalidDataException(path + "categories must be an object.");
        }

        foreach (var pair in map)
        {
            if (pair.Value is not Dictionary<string, object?> body)
            {
                // A category without its rule must not fall back to the (possibly permissive) default mode.
                throw new InvalidDataException($"{path}categories.{pair.Key} must be an object.");
            }

            var where = $"{path}categories.{pair.Key}.";
            var entry = new CategoryPolicySettings();
            entry.Mode = ReadText(body, "mode");
            entry.SelectedProducts = ReadStrings(body, "selectedProducts", entry, "selectedProducts");
            entry.BlockedProducts = ReadStrings(body, "blockedProducts", entry, "blockedProducts");
            entry.Match = ReadText(body, "match");
            entry.MinQuality = ReadText(body, "minQuality");
            if (body.TryGetValue("effects", out var e) && e is not null)
            {
                if (e is Dictionary<string, object?> effects)
                {
                    entry.Effects = new EffectRuleSettings
                    {
                        Match = ReadText(effects, "match"),
                        Required = ReadStrings(effects, "required", entry, "effects.required"),
                        Excluded = ReadStrings(effects, "excluded", entry, "effects.excluded"),
                    };
                    Unknown(effects, KnownEffects, where + "effects.", unknown);
                }
                else
                {
                    (entry.Damaged ??= new List<string>()).Add("effects");
                }
            }

            Unknown(body, KnownCategory, where, unknown);
            result[pair.Key] = entry;
        }

        return result;
    }

    /// <summary>
    /// Classifies settings text for <paramref name="saveIdentity"/>: unreadable (including a file written for another
    /// save), newer schema, or valid. Never throws for bad text.
    /// </summary>
    public static DocumentCheck Classify(string json, string? saveIdentity)
    {
        PolicySettings settings;
        try
        {
            settings = FromJson(json);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            return DocumentCheck.Unreadable(LogText.Clip(ex.Message, 200));
        }

        if (settings.SchemaVersion > PolicyConfiguration.CurrentSchemaVersion)
        {
            return DocumentCheck.Newer($"schema {settings.SchemaVersion}, supported {PolicyConfiguration.CurrentSchemaVersion}");
        }

        if (settings.SchemaVersion < 1)
        {
            return DocumentCheck.Unreadable("schemaVersion must be at least 1");
        }

        if (saveIdentity is not null && settings.Save is not null && !string.Equals(settings.Save, saveIdentity, StringComparison.Ordinal))
        {
            return DocumentCheck.Unreadable("the file was written for a different save");
        }

        return DocumentCheck.Valid;
    }

    /// <summary>Throws <see cref="FormatException"/> / <see cref="InvalidDataException"/> when the text is not a settings document.</summary>
    public static PolicySettings FromJson(string json)
    {
        if (MiniJson.Parse(json) is not Dictionary<string, object?> root)
        {
            throw new InvalidDataException("Settings root must be a JSON object.");
        }

        var settings = new PolicySettings
        {
            SchemaVersion = root.TryGetValue("schemaVersion", out var v) && v is double d && d >= 0 && d <= int.MaxValue && d == Math.Floor(d)
                ? (int)d
                : throw new InvalidDataException("schemaVersion must be a non-negative integer."),
            Enabled = !root.TryGetValue("enabled", out var e) || e is not bool enabled || enabled,
            DefaultMode = ReadText(root, "defaultMode"),
            MixedDeliveries = !root.TryGetValue("mixedDeliveries", out var md) || md is not bool mixed || mixed,
            Mixing = ReadText(root, "mixing"),
            PlayerSales = ReadBool(root, "playerSales"),
            DealerSales = ReadBool(root, "dealerSales"),
            Categories = new Dictionary<string, CategoryPolicySettings>(StringComparer.Ordinal),
        };

        if (root.TryGetValue("enabled", out var rawEnabled) && rawEnabled is not null && rawEnabled is not bool)
        {
            throw new InvalidDataException("enabled must be true or false.");
        }

        if (root.TryGetValue("mixedDeliveries", out var rawMixed) && rawMixed is not null && rawMixed is not bool)
        {
            throw new InvalidDataException("mixedDeliveries must be true or false.");
        }

        if (root.TryGetValue("save", out var rawSave) && rawSave is not null)
        {
            settings.Save = rawSave as string ?? throw new InvalidDataException("save must be a string.");
        }

        Unknown(root, KnownRoot, string.Empty, settings.UnknownFields);
        settings.Categories = ReadCategories(root.TryGetValue("categories", out var cats) ? cats : null, string.Empty, settings.UnknownFields);
        if (root.TryGetValue("dealers", out var rawDealers) && rawDealers is not null)
        {
            if (rawDealers is not Dictionary<string, object?> dealers)
            {
                throw new InvalidDataException("dealers must be an object.");
            }

            settings.Dealers = new Dictionary<string, DealerPolicySettings>(StringComparer.Ordinal);
            foreach (var pair in dealers)
            {
                // A dealer whose overrides cannot be read must not silently fall back to the global rules.
                if (pair.Value is not Dictionary<string, object?> body)
                {
                    throw new InvalidDataException($"dealers.{pair.Key} must be an object.");
                }

                Unknown(body, KnownDealer, $"dealers.{pair.Key}.", settings.UnknownFields);
                settings.Dealers[pair.Key] = new DealerPolicySettings
                {
                    Categories = ReadCategories(body.TryGetValue("categories", out var dc) ? dc : null, $"dealers.{pair.Key}.", settings.UnknownFields),
                };
            }
        }

        return settings;
    }
}
