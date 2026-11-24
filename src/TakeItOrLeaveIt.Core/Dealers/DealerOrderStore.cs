using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

public sealed class DealerOrdersLoadResult
{
    public DealerOrdersLoadResult(OrdersLoadStatus status, IReadOnlyList<DealerOrderRecord> records, IReadOnlyList<string> issues)
    {
        Status = status;
        Records = records;
        Issues = issues;
    }

    public OrdersLoadStatus Status { get; }

    public IReadOnlyList<DealerOrderRecord> Records { get; }

    public IReadOnlyList<string> Issues { get; }
}

/// <summary>
/// Persists the managed dealer appointments of one save to <c>&lt;root&gt;/saves/&lt;save identity&gt;.dealer-orders.json</c>
/// (same folder and write rules as the player order file). Only the snapshot, the dealer, the acceptance order
/// and whether the game already processed the handover are stored. <b>Reservations are never stored</b>: after
/// loading they are rebuilt from the dealers' live inventory, so a reload cannot create phantom stock. Invalid
/// entries are dropped with an issue; a dropped appointment simply follows vanilla rules.
/// </summary>
public sealed class DealerOrderStore
{
    public const int SchemaVersion = 1;

    private readonly ActiveOrderStore _files;

    public DealerOrderStore(string rootDirectory, ISettingsFileSystem? fileSystem = null, Func<DateTime>? utcNow = null)
    {
        _files = new ActiveOrderStore(rootDirectory, fileSystem, ".dealer-orders.json", SchemaVersion, utcNow);
    }

    public string PathFor(string saveIdentity) => _files.PathFor(saveIdentity);

    public DealerOrdersLoadResult Load(string saveIdentity)
    {
        var none = Array.Empty<DealerOrderRecord>();
        try
        {
            var text = _files.ReadText(saveIdentity);
            if (text is null)
            {
                return new DealerOrdersLoadResult(OrdersLoadStatus.NotFound, none, Array.Empty<string>());
            }

            var check = ActiveOrderStore.Classify(text, SchemaVersion, saveIdentity);
            if (check.State != DocumentState.Valid)
            {
                return new DealerOrdersLoadResult(check.State == DocumentState.NewerSchema ? OrdersLoadStatus.NewerSchema : OrdersLoadStatus.Unreadable, none,
                    new[] { check.State == DocumentState.NewerSchema
                        ? $"Dealer order file was written by a newer version ({check.Detail}); it is kept unchanged and appointments use vanilla rules."
                        : $"Dealer order file not used ({check.Detail}); the file is kept and appointments use vanilla rules." });
            }

            var issues = new List<string>();
            var records = FromJson(text, issues);
            return new DealerOrdersLoadResult(OrdersLoadStatus.Loaded, records, issues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            return new DealerOrdersLoadResult(OrdersLoadStatus.Unreadable, none, new[] { $"Dealer order file could not be read ({ex.GetType().Name}: {ex.Message})." });
        }
    }

    public void Save(string saveIdentity, IEnumerable<DealerOrderRecord> records)
        => SaveText(saveIdentity, ToJson(records ?? Array.Empty<DealerOrderRecord>(), saveIdentity));

    public DocumentWriteOutcome SaveText(string saveIdentity, string json) => _files.SaveText(saveIdentity, json);

    public static string ToJson(IEnumerable<DealerOrderRecord> records, string? saveIdentity = null)
    {
        var appointments = records
            .OrderBy(r => r.Sequence)
            .ThenBy(r => r.Order.ContractId, StringComparer.Ordinal)
            .Select(r =>
            {
                var body = ActiveOrderStore.WriteOrder(r.Order);
                body["dealer"] = r.DealerId;
                body["sequence"] = r.Sequence;
                body["committed"] = r.Committed;
                return (object?)body;
            })
            .ToList();

        return MiniJson.Write(ActiveOrderStore.Document(SchemaVersion, saveIdentity, "appointments", appointments));
    }

    /// <summary>Throws <see cref="FormatException"/> / <see cref="InvalidDataException"/> when the text is not a dealer order document.</summary>
    public static IReadOnlyList<DealerOrderRecord> FromJson(string json, ICollection<string> issues)
    {
        var result = new List<DealerOrderRecord>();
        foreach (var body in ActiveOrderStore.Entries(json, "appointments", SchemaVersion, issues))
        {
            var dealer = body.TryGetValue("dealer", out var d) ? d as string : null;
            if (!DealerStock.IsValidDealerId(dealer))
            {
                issues.Add($"Dealer order {body["contract"]} has no valid dealer; ignored.");
                continue;
            }

            var order = ActiveOrderStore.ReadOrder(body, issues);
            if (order is null)
            {
                continue;
            }

            var sequence = body.TryGetValue("sequence", out var s) && s is double seq && seq >= 0 && seq <= long.MaxValue ? (long)seq : 0;
            var committed = body.TryGetValue("committed", out var k) && k is true;
            result.Add(new DealerOrderRecord(order, dealer!, sequence, committed));
        }

        return result;
    }
}
