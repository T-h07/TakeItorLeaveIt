using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>Per dealer and category: has the dealer ever stocked it, and is an out-of-stock message already delivered?</summary>
public sealed class CategoryAlertEntry
{
    public CategoryAlertEntry(string dealerId, CategoryKey category, bool relevant, bool outOfStockSent)
    {
        DealerId = dealerId;
        Category = category;
        Relevant = relevant;
        OutOfStockSent = outOfStockSent;
    }

    public string DealerId { get; }

    public CategoryKey Category { get; }

    /// <summary>The dealer held permitted units of this category at some point in this save.</summary>
    public bool Relevant { get; internal set; }

    /// <summary>The out-of-stock message was delivered; it re-arms only after a real restock.</summary>
    public bool OutOfStockSent { get; internal set; }
}

/// <summary>Per waiting appointment: the shortfall last delivered (a new message only when it grows).</summary>
public sealed class ShortageAlertEntry
{
    public ShortageAlertEntry(string contractId, string dealerId, CategoryKey category, long shortfall)
    {
        ContractId = contractId;
        DealerId = dealerId;
        Category = category;
        Shortfall = shortfall;
    }

    public string ContractId { get; }

    public string DealerId { get; }

    public CategoryKey Category { get; }

    public long Shortfall { get; internal set; }
}

/// <summary>
/// The minimal deduplication state of restock messages for one save. Persisted next to the mod's other per-save
/// files (never inside the game's save). Holds no message text and no inventory.
/// </summary>
public sealed class DealerAlertState
{
    public const int MaxEntries = 4096;

    private readonly Dictionary<(string, CategoryKey), CategoryAlertEntry> _categories = new();
    private readonly Dictionary<string, ShortageAlertEntry> _shortages = new(StringComparer.Ordinal);

    public IReadOnlyList<CategoryAlertEntry> Categories => _categories.Values
        .OrderBy(e => e.DealerId, StringComparer.Ordinal).ThenBy(e => e.Category).ToList();

    public IReadOnlyList<ShortageAlertEntry> Shortages => _shortages.Values
        .OrderBy(e => e.ContractId, StringComparer.Ordinal).ToList();

    /// <summary>Bumped on every change that must be persisted.</summary>
    public long Version { get; private set; }

    public CategoryAlertEntry Category(string dealerId, CategoryKey category)
    {
        if (!_categories.TryGetValue((dealerId, category), out var entry))
        {
            entry = new CategoryAlertEntry(dealerId, category, false, false);
            if (_categories.Count < MaxEntries)
            {
                _categories.Add((dealerId, category), entry);
            }
        }

        return entry;
    }

    public bool TryGetCategory(string dealerId, CategoryKey category, out CategoryAlertEntry entry)
        => _categories.TryGetValue((dealerId, category), out entry!);

    public bool TryGetShortage(string contractId, out ShortageAlertEntry entry) => _shortages.TryGetValue(contractId, out entry!);

    internal void SetRelevant(CategoryAlertEntry entry)
    {
        if (!entry.Relevant)
        {
            entry.Relevant = true;
            Version++;
        }
    }

    internal void SetOutOfStockSent(CategoryAlertEntry entry, bool sent)
    {
        if (entry.OutOfStockSent != sent)
        {
            entry.OutOfStockSent = sent;
            Version++;
        }
    }

    internal void SetShortage(string contractId, string dealerId, CategoryKey category, long shortfall)
    {
        if (_shortages.TryGetValue(contractId, out var entry))
        {
            if (entry.Shortfall != shortfall || entry.DealerId != dealerId)
            {
                _shortages[contractId] = new ShortageAlertEntry(contractId, dealerId, category, shortfall);
                Version++;
            }
        }
        else if (_shortages.Count < MaxEntries)
        {
            _shortages.Add(contractId, new ShortageAlertEntry(contractId, dealerId, category, shortfall));
            Version++;
        }
    }

    internal bool ClearShortage(string contractId)
    {
        if (_shortages.Remove(contractId))
        {
            Version++;
            return true;
        }

        return false;
    }

    /// <summary>Drops shortage entries whose appointment no longer exists. Returns how many.</summary>
    internal int PruneShortages(Func<string, bool> keep)
    {
        var stale = _shortages.Keys.Where(id => !keep(id)).ToList();
        foreach (var id in stale)
        {
            _shortages.Remove(id);
        }

        if (stale.Count > 0)
        {
            Version++;
        }

        return stale.Count;
    }

    public string ToJson()
        => MiniJson.Write(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = DealerAlertStore.SchemaVersion,
            ["categories"] = Categories.Select(e => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dealer"] = e.DealerId,
                ["category"] = e.Category.Value,
                ["relevant"] = e.Relevant,
                ["outOfStockSent"] = e.OutOfStockSent,
            }).ToList(),
            ["shortages"] = Shortages.Select(e => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contract"] = e.ContractId,
                ["dealer"] = e.DealerId,
                ["category"] = e.Category.Value,
                ["shortfall"] = e.Shortfall,
            }).ToList(),
        });

    /// <summary>Reads a document; invalid entries are dropped with an issue. Throws when the text is not an alert document.</summary>
    public static DealerAlertState FromJson(string json, ICollection<string> issues)
    {
        if (MiniJson.Parse(json) is not Dictionary<string, object?> root)
        {
            throw new InvalidDataException("Alert file root must be a JSON object.");
        }

        if (!root.TryGetValue("schemaVersion", out var v) || v is not double version || version != Math.Floor(version) || version < 1)
        {
            throw new InvalidDataException("schemaVersion must be a positive integer.");
        }

        var state = new DealerAlertState();
        if (version > DealerAlertStore.SchemaVersion)
        {
            issues.Add($"Alert file schema {version} is newer than supported; starting with no alert history.");
            return state;
        }

        if (root.TryGetValue("categories", out var cats) && cats is List<object?> catList)
        {
            foreach (var item in catList)
            {
                if (item is Dictionary<string, object?> body &&
                    body.TryGetValue("dealer", out var d) && d is string dealer && DealerStock.IsValidDealerId(dealer) &&
                    CategoryKey.TryCreate(body.TryGetValue("category", out var c) ? c as string : null, out var category) &&
                    !state._categories.ContainsKey((dealer, category)) && state._categories.Count < MaxEntries)
                {
                    var relevant = body.TryGetValue("relevant", out var r) && r is true;
                    var sent = body.TryGetValue("outOfStockSent", out var s) && s is true;
                    state._categories.Add((dealer, category), new CategoryAlertEntry(dealer, category, relevant, sent));
                }
                else
                {
                    issues.Add("Alert category entry is invalid or duplicated; ignored.");
                }
            }
        }

        if (root.TryGetValue("shortages", out var shorts) && shorts is List<object?> shortList)
        {
            foreach (var item in shortList)
            {
                if (item is Dictionary<string, object?> body &&
                    body.TryGetValue("contract", out var k) && k is string contract && CategoryOrder.IsValidContractId(contract) &&
                    body.TryGetValue("dealer", out var d) && d is string dealer && DealerStock.IsValidDealerId(dealer) &&
                    CategoryKey.TryCreate(body.TryGetValue("category", out var c) ? c as string : null, out var category) &&
                    body.TryGetValue("shortfall", out var f) && f is double shortfall && shortfall >= 0 && shortfall <= int.MaxValue &&
                    !state._shortages.ContainsKey(contract) && state._shortages.Count < MaxEntries)
                {
                    state._shortages.Add(contract, new ShortageAlertEntry(contract, dealer, category, (long)shortfall));
                }
                else
                {
                    issues.Add("Alert shortage entry is invalid or duplicated; ignored.");
                }
            }
        }

        return state;
    }
}

/// <summary>
/// Stores <see cref="DealerAlertState"/> in <c>&lt;root&gt;/saves/&lt;save identity&gt;.dealer-alerts.json</c> with the same
/// atomic write rules as the order files. A missing or unreadable file only means "no alert history".
/// </summary>
public sealed class DealerAlertStore
{
    public const int SchemaVersion = 1;

    private readonly ActiveOrderStore _files;

    public DealerAlertStore(string rootDirectory, ISettingsFileSystem? fileSystem = null)
    {
        _files = new ActiveOrderStore(rootDirectory, fileSystem, ".dealer-alerts.json");
    }

    public string PathFor(string saveIdentity) => _files.PathFor(saveIdentity);

    /// <summary>The stored state, or a fresh one (with an issue when the file was unreadable).</summary>
    public DealerAlertState Load(string saveIdentity, ICollection<string> issues)
    {
        try
        {
            var text = _files.ReadText(saveIdentity);
            return text is null ? new DealerAlertState() : DealerAlertState.FromJson(text, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            issues.Add($"Dealer alert file could not be read ({ex.GetType().Name}); starting with no alert history.");
            return new DealerAlertState();
        }
    }

    public void SaveText(string saveIdentity, string json) => _files.SaveText(saveIdentity, json);
}
