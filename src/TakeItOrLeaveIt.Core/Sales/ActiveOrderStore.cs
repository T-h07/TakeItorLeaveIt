using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Persistence;
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
    private readonly ISettingsFileSystem _fs;

    public ActiveOrderStore(string rootDirectory, ISettingsFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Settings directory is required.", nameof(rootDirectory));
        }

        _directory = Path.Combine(rootDirectory, "saves");
        _fs = fileSystem ?? PhysicalSettingsFileSystem.Instance;
    }

    public string PathFor(string saveIdentity)
    {
        if (string.IsNullOrWhiteSpace(saveIdentity) || saveIdentity.Length > 64 ||
            saveIdentity.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
        {
            throw new ArgumentException("Save identity must be a short file-safe token.", nameof(saveIdentity));
        }

        return Path.Combine(_directory, saveIdentity + ".orders.json");
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
            .Select(t => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contract"] = t.Order.ContractId,
                ["category"] = t.Order.Category.Value,
                ["requiredUnits"] = t.Order.RequiredUnits,
                ["allowedProducts"] = t.Order.AllowedProducts.Select(p => (object?)p.Value).ToList(),
                ["requestedProducts"] = t.Order.RequestedProducts.Select(p => (object?)p.Value).ToList(),
                ["mixedDeliveries"] = t.Order.MixedDeliveries,
                ["policyRevision"] = t.Order.PolicyRevision,
                // Validated/Rejected describe transient handover-screen contents; only a commit survives a reload.
                ["committed"] = t.State == SaleState.CommitObserved,
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
        if (MiniJson.Parse(json) is not Dictionary<string, object?> root)
        {
            throw new InvalidDataException("Order file root must be a JSON object.");
        }

        if (!root.TryGetValue("schemaVersion", out var v) || v is not double version || version != Math.Floor(version) || version < 1)
        {
            throw new InvalidDataException("schemaVersion must be a positive integer.");
        }

        if (version > SchemaVersion)
        {
            issues.Add($"Order file schema {version} is newer than supported ({SchemaVersion}); active orders use vanilla rules.");
            return Array.Empty<(CategoryOrder, SaleState)>();
        }

        var result = new List<(CategoryOrder, SaleState)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetValue("orders", out var raw) || raw is not List<object?> entries)
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

            if (!CategoryKey.TryCreate(body.TryGetValue("category", out var cat) ? cat as string : null, out var category) ||
                !body.TryGetValue("requiredUnits", out var u) || u is not double units || units < 1 || units > int.MaxValue || units != Math.Floor(units))
            {
                issues.Add($"Order {id} has an invalid category or quantity; ignored.");
                continue;
            }

            var allowed = Keys(body, "allowedProducts");
            if (allowed.Count == 0)
            {
                issues.Add($"Order {id} has no valid allowed products; ignored.");
                continue;
            }

            var mixed = !body.TryGetValue("mixedDeliveries", out var m) || m is not bool mixedValue || mixedValue;
            var revision = body.TryGetValue("policyRevision", out var r) && r is double rev && rev >= 0 && rev <= long.MaxValue ? (long)rev : 0;
            var committed = body.TryGetValue("committed", out var k) && k is true;
            result.Add((new CategoryOrder(id!, category, (int)units, allowed, mixed, revision, Keys(body, "requestedProducts")),
                committed ? SaleState.CommitObserved : SaleState.Pending));
        }

        return result;
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
