using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Diagnostics;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>The three runtime files kept per save.</summary>
public enum SalesFile
{
    Orders,
    Appointments,
    Alerts,
}

/// <summary>What was read from one save's runtime files when it was bound.</summary>
public sealed class SalesLoad
{
    internal SalesLoad(string identity)
    {
        Identity = identity;
    }

    public string Identity { get; }

    public IReadOnlyList<(CategoryOrder Order, SaleState State)> Orders { get; internal set; } = Array.Empty<(CategoryOrder, SaleState)>();

    public IReadOnlyList<DealerOrderRecord> Appointments { get; internal set; } = Array.Empty<DealerOrderRecord>();

    public DealerAlertState? Alerts { get; internal set; }

    public IReadOnlyDictionary<SalesFile, OrdersLoadStatus> Status { get; internal set; } = new Dictionary<SalesFile, OrdersLoadStatus>();

    public List<string> Issues { get; } = new();
}

/// <summary>
/// Phase 06: persistence of the per-save runtime files (player orders, dealer appointments, dealer alert history),
/// independent of the game so the save-switch and write-ordering rules are unit-tested.
/// <list type="bullet">
/// <item>Writes are only accepted for a valid save identity; there is no fallback profile.</item>
/// <item>Each submission carries a new generation; the <see cref="PersistenceQueue"/> keeps one pending document per
/// save and file, so an older snapshot never overwrites a newer one, and a pending write of the previous save is
/// still written to that save's own file after a switch.</item>
/// <item>A file that was written by a newer build is never written in this session (read-only).</item>
/// <item>An unreadable file is kept (corrupt copy) the first time it is replaced.</item>
/// </list>
/// </summary>
public sealed class SalesPersistence
{
    private readonly ActiveOrderStore _orders;
    private readonly DealerOrderStore _appointments;
    private readonly DealerAlertStore _alerts;
    private readonly HashSet<(string Identity, SalesFile File)> _readOnly = new();
    private readonly Dictionary<SalesFile, OrdersLoadStatus> _status = new();
    private long _generation;

    public SalesPersistence(string rootDirectory, ISettingsFileSystem? fileSystem = null, PersistenceQueue? queue = null, Func<DateTime>? utcNow = null)
    {
        _orders = new ActiveOrderStore(rootDirectory, fileSystem, utcNow);
        _appointments = new DealerOrderStore(rootDirectory, fileSystem, utcNow);
        _alerts = new DealerAlertStore(rootDirectory, fileSystem, utcNow);
        Queue = queue ?? new PersistenceQueue();
    }

    public PersistenceQueue Queue { get; }

    /// <summary>The save whose files are bound (null = none; nothing is written).</summary>
    public string? Identity { get; private set; }

    public ActiveOrderStore OrderStore => _orders;

    public DealerOrderStore AppointmentStore => _appointments;

    public DealerAlertStore AlertStore => _alerts;

    /// <summary>Reads <paramref name="identity"/>'s files and binds to it (null unbinds). Never throws.</summary>
    public SalesLoad? Bind(string? identity)
    {
        _status.Clear();
        if (identity is null || !SaveFileNames.IsValid(identity))
        {
            Identity = null;
            return null;
        }

        Identity = identity;
        var load = new SalesLoad(identity);
        var status = new Dictionary<SalesFile, OrdersLoadStatus>();

        try
        {
            var orders = _orders.Load(identity);
            load.Orders = orders.Orders;
            status[SalesFile.Orders] = orders.Status;
            load.Issues.AddRange(orders.Issues.Select(i => "Active orders: " + i));
        }
        catch (Exception ex)
        {
            status[SalesFile.Orders] = OrdersLoadStatus.Unreadable;
            load.Issues.Add($"Active orders could not be loaded ({LogText.Describe(ex)}); they use vanilla rules.");
        }

        try
        {
            var appointments = _appointments.Load(identity);
            load.Appointments = appointments.Records;
            status[SalesFile.Appointments] = appointments.Status;
            load.Issues.AddRange(appointments.Issues.Select(i => "Dealer appointments: " + i));
        }
        catch (Exception ex)
        {
            status[SalesFile.Appointments] = OrdersLoadStatus.Unreadable;
            load.Issues.Add($"Dealer appointments could not be loaded ({LogText.Describe(ex)}); they use vanilla rules.");
        }

        try
        {
            var issues = new List<string>();
            load.Alerts = _alerts.Load(identity, issues, out var alertStatus);
            status[SalesFile.Alerts] = alertStatus;
            load.Issues.AddRange(issues.Select(i => "Dealer messages: " + i));
        }
        catch (Exception ex)
        {
            status[SalesFile.Alerts] = OrdersLoadStatus.Unreadable;
            load.Issues.Add($"Dealer message history could not be loaded ({LogText.Describe(ex)}).");
        }

        foreach (var pair in status)
        {
            _status[pair.Key] = pair.Value;
            if (pair.Value == OrdersLoadStatus.NewerSchema)
            {
                _readOnly.Add((identity, pair.Key));
            }
        }

        load.Status = status;
        return load;
    }

    /// <summary>True when <paramref name="file"/> of the bound save must not be written in this session.</summary>
    public bool IsReadOnly(SalesFile file) => Identity is not null && _readOnly.Contains((Identity, file));

    /// <summary>
    /// Queues already serialised documents (null = unchanged) for <paramref name="identity"/>, which must be the bound
    /// save. Returns the number queued. Serialise on the main thread; the queue's writer never touches game objects.
    /// </summary>
    public int Submit(string identity, string? ordersJson, string? appointmentsJson, string? alertsJson)
    {
        if (identity is null || !string.Equals(identity, Identity, StringComparison.Ordinal) || !SaveFileNames.IsValid(identity))
        {
            return 0; // a stale caller (previous save) or no save: never redirected to another profile
        }

        var generation = ++_generation;
        var queued = 0;
        queued += Enqueue(identity, SalesFile.Orders, generation, ordersJson, json => _orders.SaveText(identity, json));
        queued += Enqueue(identity, SalesFile.Appointments, generation, appointmentsJson, json => _appointments.SaveText(identity, json));
        queued += Enqueue(identity, SalesFile.Alerts, generation, alertsJson, json => _alerts.SaveText(identity, json));
        return queued;
    }

    /// <summary>True while documents of <paramref name="identity"/> are still unwritten.</summary>
    public bool HasPending(string identity) => Queue.HasPending(identity + "|");

    public IReadOnlyList<string> StatusLines()
    {
        var lines = new List<string>();
        if (Identity is null)
        {
            lines.Add("Runtime files: no save bound (nothing is written)");
        }
        else
        {
            lines.Add("Runtime files: " + string.Join(", ", new[] { SalesFile.Orders, SalesFile.Appointments, SalesFile.Alerts }
                .Select(f => $"{Label(f)} {Describe(f)}")));
        }

        var pending = Queue.PendingCount;
        var failing = Queue.FailingCount;
        lines.Add(failing > 0
            ? $"Pending writes: {pending} ({failing} failing, retried automatically; last error: {Queue.LastError})"
            : $"Pending writes: {pending}; written {Queue.WrittenCount}, failed attempts {Queue.FailureCount}");
        return lines;
    }

    private string Describe(SalesFile file)
    {
        if (IsReadOnly(file))
        {
            return "newer version, kept read-only";
        }

        return _status.TryGetValue(file, out var status)
            ? status switch
            {
                OrdersLoadStatus.Loaded => "loaded",
                OrdersLoadStatus.NotFound => "new",
                OrdersLoadStatus.Unreadable => "damaged (kept; replaced on next write)",
                _ => "newer version",
            }
            : "not loaded";
    }

    private static string Label(SalesFile file) => file switch
    {
        SalesFile.Orders => "orders",
        SalesFile.Appointments => "appointments",
        _ => "alerts",
    };

    private int Enqueue(string identity, SalesFile file, long generation, string? json, Action<string> write)
    {
        if (json is null || _readOnly.Contains((identity, file)))
        {
            return 0;
        }

        return Queue.Submit(PersistenceQueue.KeyFor(identity, Label(file)), generation, () => write(json)) ? 1 : 0;
    }
}
