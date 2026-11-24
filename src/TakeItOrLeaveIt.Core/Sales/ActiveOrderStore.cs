using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Sales;

public enum OrdersLoadStatus
{
    NotFound,
    Loaded,

    /// <summary>Damaged, or written for another save. The bytes are kept (corrupt copy) before the file is next replaced.</summary>
    Unreadable,

    /// <summary>Phase 06: written by a newer build; read as empty and never overwritten in this session.</summary>
    NewerSchema,
}

public sealed class OrdersLoadResult
{
    public OrdersLoadResult(OrdersLoadStatus status, IReadOnlyList<(CategoryOrder Order, SaleState State)> orders, IReadOnlyList<string> issues)
    {
        Status = status;
        Orders = orders;
        Issues = issues;
    }

    public OrdersLoadStatus Status { get; }

    public IReadOnlyList<(CategoryOrder Order, SaleState State)> Orders { get; }

    public IReadOnlyList<string> Issues { get; }
}

/// <summary>
/// Persists the active order snapshots of one save to <c>&lt;root&gt;/saves/&lt;save identity&gt;.orders.json</c>,
/// next to the policy file. Writes go through <see cref="SafeDocumentFile"/> (temporary file, read-back check, one
/// rename, known-good <c>.bak</c>, corrupt copies kept, newer schemas never replaced). Documents carry the save
/// identity (Phase 06); a document written for another save is not loaded. Invalid entries are dropped with an issue,
/// never guessed: a dropped order simply falls back to vanilla rules.
/// </summary>
public sealed class ActiveOrderStore
{
    public const int SchemaVersion = 1;

    private readonly string _directory;
    private readonly string _suffix;
    private readonly int _schema;
    private readonly ISettingsFileSystem _fs;
    private readonly Func<DateTime> _utcNow;

    public ActiveOrderStore(string rootDirectory, ISettingsFileSystem? fileSystem = null, Func<DateTime>? utcNow = null)
        : this(rootDirectory, fileSystem, ".orders.json", SchemaVersion, utcNow)
    {
    }

    /// <summary>Same storage rules under another file name (the dealer appointment and alert files use it).</summary>
    internal ActiveOrderStore(string rootDirectory, ISettingsFileSystem? fileSystem, string fileSuffix, int supportedSchema, Func<DateTime>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Settings directory is required.", nameof(rootDirectory));
        }

        _directory = Path.Combine(rootDirectory, "saves");
        _suffix = fileSuffix;
        _schema = supportedSchema;
        _fs = fileSystem ?? PhysicalSettingsFileSystem.Instance;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string PathFor(string saveIdentity) => Path.Combine(_directory, SaveFileNames.Check(saveIdentity) + _suffix);

    /// <summary>Classifies the file on disk (I/O errors propagate).</summary>
    internal DocumentCheck Inspect(string saveIdentity)
        => SafeDocumentFile.Inspect(_fs, PathFor(saveIdentity), text => Classify(text, _schema, saveIdentity));

    /// <summary>The known-good backup's text when it classifies as valid for this save, else null.</summary>
    internal string? ReadVerifiedBackup(string saveIdentity)
    {
        var backup = PathFor(saveIdentity) + SafeDocumentFile.BackupSuffix;
        if (!_fs.FileExists(backup))
        {
            return null;
        }

        var text = _fs.ReadAllText(backup);
        return Classify(text, _schema, saveIdentity).State == DocumentState.Valid && DeclaredSave(text) == saveIdentity ? text : null;
    }

    /// <summary>
    /// Classifies a versioned runtime document: not an object / bad schema number → unreadable; newer schema →
    /// <see cref="DocumentState.NewerSchema"/>; a <c>save</c> field naming another save → unreadable.
    /// </summary>
    internal static DocumentCheck Classify(string json, int supportedSchema, string? saveIdentity)
    {
        Dictionary<string, object?>? root;
        try
        {
            root = MiniJson.Parse(json) as Dictionary<string, object?>;
        }
        catch (FormatException ex)
        {
            return DocumentCheck.Unreadable(ex.Message);
        }

        if (root is null)
        {
            return DocumentCheck.Unreadable("the root is not a JSON object");
        }

        if (!root.TryGetValue("schemaVersion", out var v) || v is not double version || version != Math.Floor(version) || version < 1)
        {
            return DocumentCheck.Unreadable("schemaVersion must be a positive integer");
        }

        if (version > supportedSchema)
        {
            return DocumentCheck.Newer($"schema {version}, supported {supportedSchema}");
        }

        if (root.TryGetValue("save", out var save) && save is not null &&
            (save is not string text || (saveIdentity is not null && !string.Equals(text, saveIdentity, StringComparison.Ordinal))))
        {
            return DocumentCheck.Unreadable("the file was written for a different save");
        }

        return DocumentCheck.Valid;
    }

    private static string? DeclaredSave(string json)
        => MiniJson.Parse(json) is Dictionary<string, object?> root && root.TryGetValue("save", out var save) ? save as string : null;

    /// <summary>The raw document, or null when there is none. Throws on I/O errors.</summary>
    internal string? ReadText(string saveIdentity)
    {
        var path = PathFor(saveIdentity);
        return _fs.FileExists(path) ? _fs.ReadAllText(path) : null;
    }

    public OrdersLoadResult Load(string saveIdentity)
    {
        var path = PathFor(saveIdentity);
        var none = Array.Empty<(CategoryOrder, SaleState)>();
        try
        {
            if (!_fs.FileExists(path))
            {
                return new OrdersLoadResult(OrdersLoadStatus.NotFound, none, Array.Empty<string>());
            }

            var text = _fs.ReadAllText(path);
            var check = Classify(text, SchemaVersion, saveIdentity);
            if (check.State != DocumentState.Valid)
            {
                return new OrdersLoadResult(check.State == DocumentState.NewerSchema ? OrdersLoadStatus.NewerSchema : OrdersLoadStatus.Unreadable, none,
                    new[] { check.State == DocumentState.NewerSchema
                        ? $"Order file was written by a newer version ({check.Detail}); it is kept unchanged and active orders use vanilla rules."
                        : $"Order file not used ({check.Detail}); the file is kept and active orders use vanilla rules." });
            }

            var issues = new List<string>();
            var orders = FromJson(text, issues);
            return new OrdersLoadResult(OrdersLoadStatus.Loaded, orders, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            return new OrdersLoadResult(OrdersLoadStatus.Unreadable, none, new[] { $"Order file could not be read ({ex.GetType().Name}: {ex.Message})." });
        }
    }

    public void Save(string saveIdentity, IEnumerable<SaleTransaction> transactions)
        => SaveText(saveIdentity, ToJson(transactions ?? Array.Empty<SaleTransaction>(), saveIdentity));

    /// <summary>
    /// Writes an already serialised document (lets the caller serialise on the main thread and write elsewhere) through
    /// <see cref="SafeDocumentFile.Write"/>. Throws on failure, and <see cref="NewerSchemaException"/> instead of replacing
    /// a newer file.
    /// </summary>
    public DocumentWriteOutcome SaveText(string saveIdentity, string json)
    {
        var path = PathFor(saveIdentity);
        _fs.CreateDirectory(_directory);
        return SafeDocumentFile.Write(_fs, path, json ?? throw new ArgumentNullException(nameof(json)),
            text => Classify(text, _schema, saveIdentity), _utcNow);
    }

    public static string ToJson(IEnumerable<SaleTransaction> transactions, string? saveIdentity = null)
    {
        var orders = transactions
            .Where(t => !t.IsTerminal)
            .OrderBy(t => t.Order.ContractId, StringComparer.Ordinal)
            .Select(t =>
            {
                var body = WriteOrder(t.Order);
                // Validated/Rejected describe transient handover-screen contents; only a commit survives a reload.
                body["committed"] = t.State == SaleState.CommitObserved;
                return (object?)body;
            })
            .ToList();

        return MiniJson.Write(Document(SchemaVersion, saveIdentity, "orders", orders));
    }

    /// <summary>The root object shared by the runtime files: schema, optional save identity, entry list.</summary>
    internal static Dictionary<string, object?> Document(int schema, string? saveIdentity, string listName, object? entries)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = schema,
            [listName] = entries,
        };
        if (saveIdentity is not null)
        {
            root["save"] = SaveFileNames.Check(saveIdentity);
        }

        return root;
    }

    /// <summary>Throws <see cref="FormatException"/> / <see cref="InvalidDataException"/> when the text is not an order document.</summary>
    public static IReadOnlyList<(CategoryOrder Order, SaleState State)> FromJson(string json, ICollection<string> issues)
    {
        var result = new List<(CategoryOrder, SaleState)>();
        foreach (var body in Entries(json, "orders", SchemaVersion, issues))
        {
            var order = ReadOrder(body, issues);
            if (order is not null)
            {
                var committed = body.TryGetValue("committed", out var k) && k is true;
                result.Add((order, committed ? SaleState.CommitObserved : SaleState.Pending));
            }
        }

        return result;
    }

    /// <summary>The snapshot fields shared by the player and dealer files.</summary>
    internal static Dictionary<string, object?> WriteOrder(CategoryOrder order)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contract"] = order.ContractId,
            ["category"] = order.Category.Value,
            ["requiredUnits"] = order.RequiredUnits,
            ["allowedProducts"] = order.AllowedProducts.Select(p => (object?)p.Value).ToList(),
            ["requestedProducts"] = order.RequestedProducts.Select(p => (object?)p.Value).ToList(),
            ["mixedDeliveries"] = order.MixedDeliveries,
            ["policyRevision"] = order.PolicyRevision,
            ["mixing"] = PolicyConfiguration.FormatMixing(order.Mixing),
        };

        // Phase 05 (optional fields; older readers ignore them).
        if (order.MinimumQuality is { } minimum)
        {
            body["minQuality"] = QualityTiers.Id(minimum);
        }

        if (order.BaseStrainGroups.Count > 0)
        {
            body["groups"] = order.BaseStrainGroups.ToDictionary(p => p.Key.Value, p => (object?)p.Value.Cast<object?>().ToList(), StringComparer.Ordinal);
        }

        // Added with the fair-price rule (still schema 1: older readers ignore unknown fields, and entries without
        // "pricing" load through the legacy compatibility path).
        if (order.PricingBasis != PricingBasis.Legacy)
        {
            body["pricing"] = order.PricingBasis == PricingBasis.Priced ? "priced" : "requestedOnly";
            if (order.QuotedValue is decimal quoted)
            {
                body["quotedValue"] = (double)quoted;
            }

            body["unitPrices"] = order.UnitPrices.ToDictionary(p => p.Key.Value, p => (object?)(double)p.Value, StringComparer.Ordinal);
        }

        return body;
    }

    /// <summary>
    /// The entries of a versioned document that have a unique, valid contract id. Throws when the document itself
    /// is not readable; a newer schema yields no entries (with an issue).
    /// </summary>
    internal static IReadOnlyList<Dictionary<string, object?>> Entries(string json, string listName, int supportedSchema, ICollection<string> issues)
    {
        if (MiniJson.Parse(json) is not Dictionary<string, object?> root)
        {
            throw new InvalidDataException("Order file root must be a JSON object.");
        }

        if (!root.TryGetValue("schemaVersion", out var v) || v is not double version || version != Math.Floor(version) || version < 1)
        {
            throw new InvalidDataException("schemaVersion must be a positive integer.");
        }

        var result = new List<Dictionary<string, object?>>();
        if (version > supportedSchema)
        {
            issues.Add($"Order file schema {version} is newer than supported ({supportedSchema}); active orders use vanilla rules.");
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetValue(listName, out var raw) || raw is not List<object?> entries)
        {
            return result;
        }

        foreach (var entry in entries)
        {
            if (entry is not Dictionary<string, object?> body)
            {
                issues.Add("Order entry is not an object; ignored.");
                continue;
            }

            var id = body.TryGetValue("contract", out var c) ? c as string : null;
            if (!CategoryOrder.IsValidContractId(id) || !seen.Add(id!))
            {
                issues.Add($"Order entry '{id}' has a missing, invalid or duplicate contract id; ignored.");
                continue;
            }

            result.Add(body);
        }

        return result;
    }

    /// <summary>Reads the snapshot fields of an entry from <see cref="Entries"/>; null (with an issue) when any is invalid.</summary>
    internal static CategoryOrder? ReadOrder(Dictionary<string, object?> body, ICollection<string> issues)
    {
        var id = (string)body["contract"]!;
        if (!CategoryKey.TryCreate(body.TryGetValue("category", out var cat) ? cat as string : null, out var category) ||
            !body.TryGetValue("requiredUnits", out var u) || u is not double units || units < 1 || units > int.MaxValue || units != Math.Floor(units))
        {
            issues.Add($"Order {id} has an invalid category or quantity; ignored.");
            return null;
        }

        var allowed = Keys(body, "allowedProducts");
        if (allowed.Count == 0)
        {
            issues.Add($"Order {id} has no valid allowed products; ignored.");
            return null;
        }

        var mixed = !body.TryGetValue("mixedDeliveries", out var m) || m is not bool mixedValue || mixedValue;
        var revision = body.TryGetValue("policyRevision", out var r) && r is double rev && rev >= 0 && rev <= long.MaxValue ? (long)rev : 0;
        var requested = Keys(body, "requestedProducts");

        // Phase 05 fields. An unreadable value is made stricter, never looser.
        MixingMode? mixing = null;
        if (body.TryGetValue("mixing", out var mx) && mx is not null)
        {
            if (mx is string mixText && PolicyConfiguration.TryParseMixing(mixText, out var parsedMixing))
            {
                mixing = parsedMixing;
            }
            else
            {
                mixing = MixingMode.SingleProduct;
                issues.Add($"Order {id} has an unknown mixing mode; single product used.");
            }
        }

        QualityTier? minimum = null;
        if (body.TryGetValue("minQuality", out var mq) && mq is not null)
        {
            if (mq is string qualityText && QualityTiers.TryParse(qualityText, out var parsedQuality))
            {
                minimum = parsedQuality;
            }
            else
            {
                issues.Add($"Order {id} has an unknown minimum quality; ignored.");
                return null;
            }
        }

        var groups = new List<KeyValuePair<ProductKey, IReadOnlyList<string>>>();
        if (body.TryGetValue("groups", out var gr) && gr is Dictionary<string, object?> groupTable)
        {
            foreach (var entry in groupTable)
            {
                if (ProductKey.TryCreate(entry.Key, out var key) && entry.Value is List<object?> list)
                {
                    groups.Add(new KeyValuePair<ProductKey, IReadOnlyList<string>>(key, list.OfType<string>().ToArray()));
                }
            }
        }

        CategoryOrder Make(IEnumerable<ProductKey> products, PricingBasis basisValue, decimal? quote, IEnumerable<KeyValuePair<ProductKey, decimal>>? unitPrices)
            => new(id, category, (int)units, products, mixed, revision, requested, basisValue, quote, unitPrices, mixing, groups, minimum);

        var pricing = body.TryGetValue("pricing", out var pr) ? pr as string : null;
        if (pricing is null)
        {
            var legacy = Make(allowed, PricingBasis.Legacy, null, null).WithLegacyPricing();
            issues.Add(legacy.PricingBasis == PricingBasis.Legacy
                ? $"Order {id} predates the fair-price rule and allows none of its requested products; kept unchanged."
                : $"Order {id} predates the fair-price rule; limited to its requested product(s).");
            return legacy;
        }

        PricingBasis? basis = pricing switch
        {
            "priced" => PricingBasis.Priced,
            "requestedOnly" => PricingBasis.RequestedOnly,
            _ => null,
        };
        if (basis is null)
        {
            issues.Add($"Order {id} has an unknown pricing basis; ignored.");
            return null;
        }

        decimal? quoted = body.TryGetValue("quotedValue", out var q) && q is double qv && qv >= 0 && qv < 1e12 ? (decimal)qv : null;
        var prices = new List<KeyValuePair<ProductKey, decimal>>();
        if (body.TryGetValue("unitPrices", out var up) && up is Dictionary<string, object?> table)
        {
            foreach (var entry in table)
            {
                if (ProductKey.TryCreate(entry.Key, out var key) && entry.Value is double price && price >= 0 && price < 1e12)
                {
                    prices.Add(new KeyValuePair<ProductKey, decimal>(key, (decimal)price));
                }
            }
        }

        var order = Make(allowed, basis.Value, quoted, prices);
        var substitutesOutsideRequest = order.AllowedProducts.Any(p => !requested.Contains(p));
        if (order.PricingBasis == PricingBasis.RequestedOnly && substitutesOutsideRequest)
        {
            // Damaged price data (or a requested-only entry edited by hand): never substitute on a quote that
            // cannot be checked.
            var safe = allowed.Where(requested.Contains).ToList();
            if (safe.Count == 0)
            {
                issues.Add($"Order {id} has unusable price data and allows none of its requested products; ignored.");
                return null;
            }

            issues.Add($"Order {id} has unusable price data; limited to its requested product(s).");
            return Make(safe, PricingBasis.RequestedOnly, null, prices);
        }

        return order;
    }

    private static List<ProductKey> Keys(Dictionary<string, object?> body, string name)
    {
        var keys = new List<ProductKey>();
        if (body.TryGetValue(name, out var raw) && raw is List<object?> items)
        {
            foreach (var item in items)
            {
                if (ProductKey.TryCreate(item as string, out var key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }
}
