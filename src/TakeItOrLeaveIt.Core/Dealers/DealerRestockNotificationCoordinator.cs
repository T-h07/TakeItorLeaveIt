using System;
using System.Collections.Generic;
using System.Linq;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;
using TakeItOrLeaveIt.Core.Products;
using TakeItOrLeaveIt.Core.Sales;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>
/// Decides when a hired dealer texts the player about stock (Phase 04 enhancement). It never sends anything
/// itself: <see cref="Evaluate"/> returns the messages that are due, the game adapter sends them through the
/// dealer's own phone conversation, and <see cref="Report"/> records whether that worked. A message counts as
/// delivered only after a successful send; a failed send is retried with a back-off and never marked delivered.
/// <list type="bullet">
/// <item><b>Out of stock</b> (Type A): a category's permitted units drop to 0 while the dealer has stocked that
/// category before in this save, or has an appointment for it. Sent once; re-armed only after a real restock
/// (permitted units above 0 again).</item>
/// <item><b>Appointment shortage</b> (Type B): a waiting appointment's shortfall
/// (required − (eligible stock − other appointments' holds), best single product when mixing is off). Sent when it
/// first appears and again only if it grows.</item>
/// </list>
/// No messages during the first <see cref="SettleSeconds"/> after a save is bound, so loading never spams.
/// Main-thread only.
/// </summary>
public sealed class DealerRestockNotificationCoordinator
{
    public const double SettleSeconds = 20;
    public const double EvaluateIntervalSeconds = 5;
    public const double RetrySeconds = 30;
    public const int MaxSendAttempts = 3;

    private readonly IPolicyProvider _policies;
    private readonly Func<ProductCatalogSnapshot?> _catalog;
    private readonly DealerSaleCoordinator _dealers;
    private readonly IModLog? _log;
    private readonly Dictionary<string, double> _nextEvaluation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Attempts, double RetryAt)> _failures = new(StringComparer.Ordinal);
    private double _settledAt;
    private long _savedVersion;

    public DealerRestockNotificationCoordinator(IPolicyProvider policies, Func<ProductCatalogSnapshot?> catalog, DealerSaleCoordinator dealers, IModLog? log = null)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _dealers = dealers ?? throw new ArgumentNullException(nameof(dealers));
        _log = log;
    }

    public DealerAlertState State { get; private set; } = new();

    /// <summary>Seconds on a monotonic clock (settle period, throttling, retry back-off). Tests replace it.</summary>
    public Func<double> Clock { get; set; } = DefaultClock;

    public string? SaveIdentity { get; private set; }

    /// <summary>Whether the live settings are loaded and trustworthy. Null means always.</summary>
    public Func<bool>? PolicyReady { get; set; }

    public long Delivered { get; private set; }

    public long Failed { get; private set; }

    public string? LastFailure { get; private set; }

    public bool HasUnsavedChanges => SaveIdentity is not null && State.Version != _savedVersion;

    public void MarkPersisted() => _savedVersion = State.Version;

    /// <summary>Switches to another save's alert history (or none) and starts the settle period.</summary>
    public void BindSave(string? saveIdentity, DealerAlertState? state)
    {
        var now = Clock();
        SaveIdentity = saveIdentity;
        State = state ?? new DealerAlertState();
        _savedVersion = State.Version;
        _nextEvaluation.Clear();
        _failures.Clear();
        _settledAt = now + SettleSeconds;
        LastFailure = null;
    }

    /// <summary>
    /// Checks one hired dealer's stock and returns the messages due now (empty most of the time). Throttled per dealer
    /// to <see cref="EvaluateIntervalSeconds"/>; observations before the settle time only update the history.
    /// </summary>
    public IReadOnlyList<DealerStockAlert> Evaluate(DealerStock stock)
    {
        var now = Clock();
        var due = new List<DealerStockAlert>();
        var save = SaveIdentity;
        var policy = _policies.Current;
        var catalog = _catalog();
        if (stock is null || save is null || !_dealers.IsAuthoritative || !string.Equals(_dealers.SaveIdentity, save, StringComparison.Ordinal) ||
            (PolicyReady is not null && !PolicyReady()) || policy is null || !policy.Enabled || catalog is null)
        {
            return due;
        }

        if (_nextEvaluation.TryGetValue(stock.DealerId, out var next) && now < next)
        {
            return due;
        }

        _nextEvaluation[stock.DealerId] = now + EvaluateIntervalSeconds;
        var settled = now >= _settledAt;
        var appointments = _dealers.Appointments.Where(a => a.DealerId == stock.DealerId && !a.IsTerminal).ToList();

        // Type B first: a shortage message is more useful than "out of stock" for the same category.
        var shortageCategories = new HashSet<CategoryKey>();
        foreach (var appointment in appointments)
        {
            if (appointment.State != AppointmentState.WaitingForEligibleStock)
            {
                State.ClearShortage(appointment.ContractId); // reserved again: a later shortage is a new episode
                _failures.Remove(DealerStockAlert.MakeKey(save, stock.DealerId, appointment.Order.Category, DealerAlertType.AppointmentShortage, appointment.ContractId));
                continue;
            }

            var available = DealerStockMonitor.AvailableFor(appointment, stock, _dealers.Ledger);
            var shortfall = DealerStockMonitor.Shortfall(appointment.Order.RequiredUnits, available);
            if (shortfall <= 0)
            {
                continue; // the next reconcile reserves it
            }

            shortageCategories.Add(appointment.Order.Category);
            var sent = State.TryGetShortage(appointment.ContractId, out var entry) && entry.DealerId == stock.DealerId ? entry.Shortfall : 0;
            if (shortfall > sent && settled)
            {
                var name = DeliveryValidator.CategoryName(appointment.Order.Category, catalog);
                AddIfDue(due, new DealerStockAlert(save, stock.DealerId, appointment.Order.Category, DealerAlertType.AppointmentShortage,
                    appointment.ContractId, appointment.Order.RequiredUnits, available,
                    DealerStockAlert.ShortageText(name, appointment.Order.RequiredUnits, available)), now);
            }
        }

        // Type A.
        foreach (var pair in DealerStockMonitor.EligibleUnitsByCategory(stock, policy, catalog))
        {
            var category = pair.Key;
            if (pair.Value > 0)
            {
                var entry = State.Category(stock.DealerId, category);
                State.SetRelevant(entry);
                State.SetOutOfStockSent(entry, false); // re-armed by a real restock
                _failures.Remove(DealerStockAlert.MakeKey(save, stock.DealerId, category, DealerAlertType.OutOfStock, null));
                continue;
            }

            var hasDemand = appointments.Any(a => a.Order.Category == category);
            var known = State.TryGetCategory(stock.DealerId, category, out var existing);
            if (!(known && existing.Relevant) && !hasDemand)
            {
                continue; // never stocked and nobody waiting: not this dealer's category
            }

            var state = known ? existing : State.Category(stock.DealerId, category);
            if (state.OutOfStockSent || !settled || shortageCategories.Contains(category))
            {
                continue;
            }

            var name = DeliveryValidator.CategoryName(category, catalog);
            AddIfDue(due, new DealerStockAlert(save, stock.DealerId, category, DealerAlertType.OutOfStock, null, 0, 0,
                DealerStockAlert.OutOfStockText(name)), now);
        }

        return due;
    }

    /// <summary>The adapter tried to send <paramref name="alert"/>; only a successful send marks it delivered.</summary>
    public void Report(DealerStockAlert alert, bool sent, string? failure = null)
    {
        var now = Clock();
        if (alert is null || !string.Equals(alert.SaveId, SaveIdentity, StringComparison.Ordinal))
        {
            return; // the save changed in between: the new save's state must not record it
        }

        if (!sent)
        {
            var attempts = (_failures.TryGetValue(alert.Key, out var f) ? f.Attempts : 0) + 1;
            _failures[alert.Key] = (attempts, now + RetrySeconds * attempts);
            Failed++;
            LastFailure = $"{alert.Type} for dealer {alert.DealerId}: {failure ?? "send failed"} (attempt {attempts} of {MaxSendAttempts}).";
            _log?.Warning($"Dealer message not sent: {LastFailure}");
            return;
        }

        _failures.Remove(alert.Key);
        Delivered++;
        if (alert.Type == DealerAlertType.OutOfStock)
        {
            State.SetOutOfStockSent(State.Category(alert.DealerId, alert.Category), true);
        }
        else if (alert.ContractId is not null)
        {
            State.SetShortage(alert.ContractId, alert.DealerId, alert.Category, alert.Shortfall);
        }

        _log?.Info($"Dealer message sent: {alert}");
    }

    /// <summary>Drops shortage history of appointments that no longer exist (call after contracts end).</summary>
    public int Prune() => State.PruneShortages(id => _dealers.Find(id) is not null);

    public IReadOnlyList<string> StatusLines(bool messagingAvailable)
        => new[]
        {
            $"Dealer restock messages: {(messagingAvailable ? "Active" : "Unavailable")}",
            $"Dealer messages sent: {Delivered}, failed: {Failed}" + (LastFailure is null ? string.Empty : $" (last: {LastFailure})"),
        };

    private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();

    private static double DefaultClock() => Watch.Elapsed.TotalSeconds;

    private void AddIfDue(List<DealerStockAlert> due, DealerStockAlert alert, double now)
    {
        if (_failures.TryGetValue(alert.Key, out var failure) && (failure.Attempts >= MaxSendAttempts || now < failure.RetryAt))
        {
            return; // backing off, or given up until the situation changes (a restock clears the history)
        }

        due.Add(alert);
    }
}
