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
    Unreadable,
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
/// next to the policy file. Atomic write (temp file, then replace). Invalid entries are dropped with an issue,
/// never guessed: a dropped order simply falls back to vanilla rules.
/// </summary>
public sealed class ActiveOrderStore
{
    public const int SchemaVersion = 1;

    private readonly string _directory;
    private readonly string _suffix;
    private readonly ISettingsFileSystem _fs;

    public ActiveOrderStore(string rootDirectory, ISettingsFileSystem? fileSystem = null)
        : this(rootDirectory, fileSystem, ".orders.json")
    {
    }

    /// <summary>Same storage rules under another file name (the dealer appointment file uses it).</summary>
    internal ActiveOrderStore(string rootDirectory, ISettingsFileSystem? fileSystem, string fileSuffix)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Settings directory is required.", nameof(rootDirectory));
        }

        _directory = Path.Combine(rootDirectory, "saves");
        _suffix = fileSuffix;
        _fs = fileSystem ?? PhysicalSettingsFileSystem.Instance;
    }

    public string PathFor(string saveIdentity)
    {
        if (string.IsNullOrWhiteSpace(saveIdentity) || saveIdentity.Length > 64 ||
            saveIdentity.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
        {
            throw new ArgumentException("Save identity must be a short file-safe token.", nameof(saveIdentity));
        }

        return Path.Combine(_directory, saveIdentity + _suffix);
    }

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

            var issues = new List<string>();
            var orders = FromJson(_fs.ReadAllText(path), issues);
            return new OrdersLoadResult(OrdersLoadStatus.Loaded, orders, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            return new OrdersLoadResult(OrdersLoadStatus.Unreadable, none, new[] { $"Order file could not be read ({ex.GetType().Name}: {ex.Message})." });
        }
    }

    public void Save(string saveIdentity, IEnumerable<SaleTransaction> transactions)
        => SaveText(saveIdentity, ToJson(transactions ?? Array.Empty<SaleTransaction>()));

    /// <summary>Writes an already serialised document (lets the caller serialise on the main thread and write elsewhere).</summary>
    public void SaveText(string saveIdentity, string json)
    {
        var path = PathFor(saveIdentity);
        var temp = path + ".tmp";
        _fs.CreateDirectory(_directory);
        try
        {
            _fs.WriteAllText(temp, json ?? throw new ArgumentNullException(nameof(json)));
            _fs.ReplaceFile(temp, path);
        }
        catch
        {
            try
            {
                _fs.DeleteFile(temp);
            }
            catch (Exception)
            {
                // best effort
            }

            throw;
        }
    }

    public static string ToJson(IEnumerable<SaleTransaction> transactions)
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

        return MiniJson.Write(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = SchemaVersion,
            ["orders"] = orders,
        });
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
