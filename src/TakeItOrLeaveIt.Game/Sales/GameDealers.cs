using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using TakeItOrLeaveIt.Core.Dealers;
using TakeItOrLeaveIt.Core.Inventory;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Game.Sales;

/// <summary>
/// Game-facing adapter for hired-dealer sales (IL2CPP, built only with local game references). Reads a dealer's
/// id and inventory and hands every decision to <see cref="DealerSaleCoordinator"/>. The only inventory method
/// it calls is the game's own removal, from the D1 hook, with the product and units the coordinator planned.
/// Main thread only.
/// </summary>
internal static class GameDealers
{
    private const long ReconcileIntervalMs = 3_000;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Dictionary<IntPtr, string> DealerIds = new();
    private static readonly Dictionary<string, DealerInfo> Seen = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> NextReconcile = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, double> NextNotify = new(StringComparer.Ordinal);

    public static DealerSaleCoordinator? Coordinator { get; set; }

    /// <summary>
    /// Phase 08: items a short or mismatched removal took from a dealer that could not be given back at once. Retried
    /// from the same dealer's tick; never handed to a customer.
    /// </summary>
    public static PendingReturnQueue<ItemInstance> PendingReturns { get; } = new();

    /// <summary>Restock messages (null when not wired). Checked from the dealer's tick, throttled in Core.</summary>
    public static DealerRestockNotificationCoordinator? Notifier { get; set; }

    /// <summary>The dealer's NPC id as a file-safe token (stable across sessions), or null.</summary>
    public static string? DealerId(Dealer? dealer)
    {
        if (dealer is null)
        {
            return null;
        }

        var pointer = dealer.Pointer;
        if (DealerIds.TryGetValue(pointer, out var cached))
        {
            return cached;
        }

        var id = Token(dealer.ID);
        if (id is not null && DealerIds.Count < 256)
        {
            DealerIds[pointer] = id;
        }

        return id;
    }

    /// <summary>
    /// The dealer's packaged product stock in units (stack size x package amount), one entry per slot with its quality
    /// tier (Phase 05). Unpackaged or unreadable items are left out, so the snapshot can only under-count what the game
    /// could remove.
    /// </summary>
    public static DealerStock? Stock(Dealer dealer)
    {
        var id = DealerId(dealer);
        if (id is null)
        {
            return null;
        }

        var stacks = new List<InventoryStack?>();
        var slots = dealer.GetAllSlots();
        if (slots is not null)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var product = slots[i]?.ItemInstance?.TryCast<ProductItemInstance>();
                if (product is null || product.AppliedPackaging == null || product.Quantity <= 0 || product.Amount <= 0 ||
                    !ProductKey.TryCreate(product.ID, out var key))
                {
                    continue;
                }

                var units = (long)product.Quantity * product.Amount;
                stacks.Add(new InventoryStack("slot:" + i.ToString(CultureInfo.InvariantCulture), key, (int)Math.Min(units, int.MaxValue), GameSales.QualityOf(product)));
            }
        }

        return new DealerStock(id, stacks);
    }

    /// <summary>Throttled reconcile for one dealer (from the dealer's tick). Cheap when it has no appointment.</summary>
    public static void MaybeReconcile(Dealer dealer)
    {
        var coordinator = Coordinator;
        if (coordinator is null || coordinator.ActiveAppointments == 0 || !coordinator.IsAuthoritative)
        {
            return;
        }

        var id = DealerId(dealer);
        if (id is null || !coordinator.HasAppointmentsFor(id))
        {
            return;
        }

        var now = Clock.ElapsedMilliseconds;
        if (NextReconcile.TryGetValue(id, out var next) && now < next)
        {
            return;
        }

        NextReconcile[id] = now + ReconcileIntervalMs;
        var stock = Stock(dealer);
        if (stock is null)
        {
            return;
        }

        var report = coordinator.Reconcile(stock);
        if (report.Changed)
        {
            GameSales.Log?.Info($"Dealer {id} stock reconciled: {report.Reserved.Count} reserved, {report.Invalidated.Count} invalidated, " +
                                $"{report.StillWaiting} waiting ({stock}).");
        }
    }

    /// <summary>
    /// Restock messages for a hired dealer (from the dealer's tick). Reads the stock at most once per
    /// <see cref="DealerRestockNotificationCoordinator.EvaluateIntervalSeconds"/> per dealer; sends due messages
    /// through the game's conversation and reports each result (a failed send is retried later, never counted).
    /// </summary>
    public static void MaybeNotify(Dealer dealer)
    {
        var notifier = Notifier;
        if (notifier is null || notifier.SaveIdentity is null || !GameDealerMessagingAdapter.Available || !dealer.IsRecruited)
        {
            return;
        }

        var id = DealerId(dealer);
        if (id is null)
        {
            return;
        }

        var now = Clock.Elapsed.TotalSeconds;
        if (NextNotify.TryGetValue(id, out var next) && now < next)
        {
            return;
        }

        NextNotify[id] = now + DealerRestockNotificationCoordinator.EvaluateIntervalSeconds;
        var stock = Stock(dealer);
        if (stock is null)
        {
            return;
        }

        foreach (var alert in notifier.Evaluate(stock))
        {
            var sent = GameDealerMessagingAdapter.TrySend(dealer, alert.Text, out var failure);
            notifier.Report(alert, sent, failure);
        }
    }

    /// <summary>
    /// Records a dealer seen on its tick for the menu's override list (Phase 05): id, name (<c>NPC.FullName</c>) and
    /// whether it is hired (<c>Dealer.IsRecruited</c>). Read-only; rebuilt per save.
    /// </summary>
    public static void Observe(Dealer dealer)
    {
        var id = DealerId(dealer);
        if (id is null || (Seen.Count >= 64 && !Seen.ContainsKey(id)))
        {
            return;
        }

        var hired = dealer.IsRecruited;
        if (Seen.TryGetValue(id, out var known) && known.Hired == hired)
        {
            return;
        }

        string? name = null;
        try
        {
            name = dealer.FullName;
        }
        catch (Exception)
        {
            // the id is shown instead
        }

        Seen[id] = new DealerInfo(id, name, hired);
    }

    /// <summary>Dealers seen in this save, for the menu.</summary>
    public static IReadOnlyList<DealerInfo> SeenDealers() => new List<DealerInfo>(Seen.Values);

    /// <summary>
    /// Forgets every per-session cache. Called on each save session change (Phase 06): the id cache is keyed by native
    /// object pointers, which may be reused by another dealer after a reload, and the seen list must not show the
    /// previous save's dealers.
    /// </summary>
    public static void Reset()
    {
        Seen.Clear();
        DealerIds.Clear();
        NextReconcile.Clear();
        NextNotify.Clear();
        var dropped = PendingReturns.Count;
        var units = PendingReturns.Units;
        if (PendingReturns.Clear() > 0)
        {
            GameSales.Log?.Error($"Save session changed with {dropped} removed item(s) ({units} unit(s)) still waiting to go back to their dealer; they belong to the previous game state and are dropped.");
        }
    }

    /// <summary>
    /// Phase 08: forgets the pointer-keyed dealer id cache. Called on every scene load: a native address is only an
    /// identity while the object is alive, and a scene change can free and reuse it.
    /// </summary>
    public static void ForgetPointers() => DealerIds.Clear();

    /// <summary>Phase 08: retries giving queued items back to this dealer (cheap when nothing is queued).</summary>
    public static void RetryReturns(Dealer dealer)
    {
        if (PendingReturns.Count == 0)
        {
            return;
        }

        var id = DealerId(dealer);
        if (!PendingReturns.HasPending(id))
        {
            return;
        }

        var pass = PendingReturns.Retry(id, item =>
        {
            dealer.AddItemToInventory(item);
            return true;
        });
        if (pass.Returned > 0)
        {
            GameSales.Log?.Info($"Dealer {id}: {pass.Returned} removed item stack(s) given back after an earlier failure; {pass.StillPending} still waiting.");
            GameSales.InvalidateCache();
        }

        if (pass.Abandoned > 0)
        {
            GameSales.Log?.Error($"Dealer {id}: {pass.Abandoned} removed item stack(s) ({pass.AbandonedUnits} unit(s)) could not be given back after {PendingReturnQueue<ItemInstance>.MaxAttempts} attempts and are lost. Please report this with the log.");
        }
    }

    private static string? Token(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(raw!.Length, 64));
        foreach (var c in raw.Trim())
        {
            if (builder.Length == 64)
            {
                break;
            }

            builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
        }

        var token = builder.ToString();
        return DealerStock.IsValidDealerId(token) ? token : null;
    }
}
