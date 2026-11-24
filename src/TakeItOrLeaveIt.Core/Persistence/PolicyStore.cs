using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Core.Persistence;

public enum PolicyLoadStatus
{
    /// <summary>No settings saved for this save yet: defaults apply.</summary>
    NotFound,

    Loaded,

    /// <summary>The file exists but could not be read or parsed. Defaults apply and the file is preserved.</summary>
    Unreadable,
}

public sealed class PolicyLoadResult
{
    public PolicyLoadResult(PolicyLoadStatus status, PolicySettings? settings, string? message)
    {
        Status = status;
        Settings = settings;
        Message = message;
    }

    public PolicyLoadStatus Status { get; }

    public PolicySettings? Settings { get; }

    public string? Message { get; }
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
}

/// <summary>
/// Stores one JSON file per save under <c>&lt;root&gt;/saves/&lt;save identity&gt;.json</c> (the identity is an
/// anonymised hash, see <see cref="Discovery.SaveIdentity"/>). Writes go to a temporary file first and then
/// replace the target, so a crash never leaves a half-written file. An unreadable file is backed up before it is
/// replaced. Nothing is ever written into the game's own save folders.
/// </summary>
public sealed class JsonPolicyStore : IPolicyStore
{
    private readonly string _directory;
    private readonly ISettingsFileSystem _fs;

    public JsonPolicyStore(string rootDirectory, ISettingsFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Settings directory is required.", nameof(rootDirectory));
        }

        _directory = Path.Combine(rootDirectory, "saves");
        _fs = fileSystem ?? PhysicalSettingsFileSystem.Instance;
    }

    public string PathFor(string saveIdentity) => Path.Combine(_directory, CheckIdentity(saveIdentity) + ".json");

    public PolicyLoadResult Load(string saveIdentity)
    {
        var path = PathFor(saveIdentity);
        try
        {
            if (!_fs.FileExists(path))
            {
                return new PolicyLoadResult(PolicyLoadStatus.NotFound, null, null);
            }

            return new PolicyLoadResult(PolicyLoadStatus.Loaded, PolicySettingsJson.FromJson(_fs.ReadAllText(path)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            return new PolicyLoadResult(PolicyLoadStatus.Unreadable, null, $"Settings file could not be read ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    public void Save(string saveIdentity, PolicySettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var path = PathFor(saveIdentity);
        var temp = path + ".tmp";
        _fs.CreateDirectory(_directory);
        PreserveUnreadable(path);
        PreserveOlderSchema(path, settings.SchemaVersion);
        try
        {
            _fs.WriteAllText(temp, PolicySettingsJson.ToJson(settings));
            _fs.ReplaceFile(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private void PreserveUnreadable(string path)
    {
        if (!_fs.FileExists(path))
        {
            return;
        }

        try
        {
            PolicySettingsJson.FromJson(_fs.ReadAllText(path));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            _fs.CopyFile(path, path + ".unreadable.bak");
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
            // unreadable files are preserved by PreserveUnreadable
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            _fs.DeleteFile(path);
        }
        catch (Exception)
        {
            // best effort; the original file is untouched either way
        }
    }

    private static string CheckIdentity(string saveIdentity)
    {
        if (string.IsNullOrWhiteSpace(saveIdentity) || saveIdentity.Length > 64 ||
            saveIdentity.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
        {
            throw new ArgumentException("Save identity must be a short file-safe token.", nameof(saveIdentity));
        }

        return saveIdentity;
    }
}

/// <summary>Maps <see cref="PolicySettings"/> to and from JSON (shape documented in MOD_MENU_SPEC.md).</summary>
public static class PolicySettingsJson
{
    public static string ToJson(PolicySettings settings)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = settings.SchemaVersion,
            ["enabled"] = settings.Enabled,
            ["defaultMode"] = settings.DefaultMode,
            ["mixedDeliveries"] = settings.MixedDeliveries,
            ["categories"] = WriteCategories(settings.Categories, settings.SchemaVersion >= 2),
        };

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

    private static List<object?> Strings(List<string>? values) => (values ?? new List<string>()).Cast<object?>().ToList();

    private static List<string> ReadStrings(Dictionary<string, object?> body, string name)
        => body.TryGetValue(name, out var raw) && raw is List<object?> items ? items.OfType<string>().ToList() : new List<string>();

    private static bool ReadBool(Dictionary<string, object?> root, string name)
    {
        if (!root.TryGetValue(name, out var raw) || raw is null)
        {
            return true;
        }

        return raw is bool value ? value : throw new InvalidDataException(name + " must be true or false.");
    }

    private static Dictionary<string, CategoryPolicySettings> ReadCategories(object? cats)
    {
        var result = new Dictionary<string, CategoryPolicySettings>(StringComparer.Ordinal);
        if (cats is null)
        {
            return result;
        }

        if (cats is not Dictionary<string, object?> map)
        {
            throw new InvalidDataException("categories must be an object.");
        }

        foreach (var pair in map)
        {
            var entry = new CategoryPolicySettings { SelectedProducts = new List<string>() };
            if (pair.Value is Dictionary<string, object?> body)
            {
                entry.Mode = body.TryGetValue("mode", out var m) ? m as string : null;
                // Non-string entries are skipped here; PolicyConfiguration.FromSettings validates the rest.
                entry.SelectedProducts = ReadStrings(body, "selectedProducts");
                entry.BlockedProducts = ReadStrings(body, "blockedProducts");
                entry.Match = body.TryGetValue("match", out var match) ? match as string : null;
                entry.MinQuality = body.TryGetValue("minQuality", out var q) ? q as string : null;
                if (body.TryGetValue("effects", out var e) && e is Dictionary<string, object?> effects)
                {
                    entry.Effects = new EffectRuleSettings
                    {
                        Match = effects.TryGetValue("match", out var em) ? em as string : null,
                        Required = ReadStrings(effects, "required"),
                        Excluded = ReadStrings(effects, "excluded"),
                    };
                }
            }

            result[pair.Key] = entry;
        }

        return result;
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
            DefaultMode = root.TryGetValue("defaultMode", out var dm) ? dm as string : null,
            MixedDeliveries = !root.TryGetValue("mixedDeliveries", out var md) || md is not bool mixed || mixed,
            Mixing = root.TryGetValue("mixing", out var mx) ? mx as string : null,
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

        settings.Categories = ReadCategories(root.TryGetValue("categories", out var cats) ? cats : null);
        if (root.TryGetValue("dealers", out var rawDealers) && rawDealers is not null)
        {
            if (rawDealers is not Dictionary<string, object?> dealers)
            {
                throw new InvalidDataException("dealers must be an object.");
            }

            settings.Dealers = new Dictionary<string, DealerPolicySettings>(StringComparer.Ordinal);
            foreach (var pair in dealers)
            {
                settings.Dealers[pair.Key] = new DealerPolicySettings
                {
                    Categories = pair.Value is Dictionary<string, object?> body && body.TryGetValue("categories", out var dc) ? ReadCategories(dc) : new Dictionary<string, CategoryPolicySettings>(),
                };
            }
        }

        return settings;
    }
}
