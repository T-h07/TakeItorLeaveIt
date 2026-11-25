using System;
using System.Collections.Generic;

namespace TakeItOrLeaveIt.Core.Dealers;

/// <summary>Result of one retry pass for an owner.</summary>
public readonly struct PendingReturnPass
{
    public PendingReturnPass(int returned, int stillPending, int abandoned, long abandonedUnits)
    {
        Returned = returned;
        StillPending = stillPending;
        Abandoned = abandoned;
        AbandonedUnits = abandonedUnits;
    }

    /// <summary>Items given back during this pass.</summary>
    public int Returned { get; }

    /// <summary>Items still waiting for the next pass.</summary>
    public int StillPending { get; }

    /// <summary>Items dropped this pass after <see cref="PendingReturnQueue{T}.MaxAttempts"/> failed attempts.</summary>
    public int Abandoned { get; }

    public long AbandonedUnits { get; }
}

/// <summary>
/// Phase 08 hardening (rollback-failure handling): items a dealer handover removed but could not give back at once.
/// The game adapter holds each item here and retries on the dealer's later ticks instead of dropping it after one
/// failed <c>AddItemToInventory</c>. An item is never handed to the customer from here; it can only go back to the
/// dealer it came from. Bounded per owner and in attempts, so a permanently failing return cannot grow without limit.
/// Main thread only.
/// </summary>
public sealed class PendingReturnQueue<T>
    where T : class
{
    public const int MaxAttempts = 20;

    public const int MaxItemsPerOwner = 64;

    private readonly Dictionary<string, List<Entry>> _byOwner = new(StringComparer.Ordinal);

    /// <summary>Items waiting across all owners.</summary>
    public int Count { get; private set; }

    /// <summary>Units waiting across all owners.</summary>
    public long Units { get; private set; }

    /// <summary>Items given up on since the queue was created or cleared (each one logged by the caller).</summary>
    public int AbandonedTotal { get; private set; }

    public bool HasPending(string? owner) => owner is not null && _byOwner.ContainsKey(owner);

    /// <summary>Queues an item that could not be returned. False when the owner's queue is full (the caller reports the loss).</summary>
    public bool Add(string owner, T item, long units)
    {
        if (owner is null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (!_byOwner.TryGetValue(owner, out var list))
        {
            list = new List<Entry>();
            _byOwner[owner] = list;
        }

        if (list.Count >= MaxItemsPerOwner)
        {
            return false;
        }

        list.Add(new Entry(item, Math.Max(0, units)));
        Count++;
        Units += Math.Max(0, units);
        return true;
    }

    /// <summary>
    /// One retry pass for <paramref name="owner"/>: <paramref name="giveBack"/> returns true when the item is back in the
    /// owner's inventory, false or an exception when it is not (the attempt counts either way).
    /// </summary>
    public PendingReturnPass Retry(string? owner, Func<T, bool> giveBack)
    {
        if (giveBack is null)
        {
            throw new ArgumentNullException(nameof(giveBack));
        }

        if (owner is null || !_byOwner.TryGetValue(owner, out var list))
        {
            return default;
        }

        int returned = 0, abandoned = 0;
        long abandonedUnits = 0;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var entry = list[i];
            bool ok;
            try
            {
                ok = giveBack(entry.Item);
            }
            catch (Exception)
            {
                ok = false;
            }

            if (ok)
            {
                returned++;
            }
            else if (++entry.Attempts >= MaxAttempts)
            {
                abandoned++;
                abandonedUnits += entry.Units;
            }
            else
            {
                continue;
            }

            list.RemoveAt(i);
            Count--;
            Units -= entry.Units;
        }

        AbandonedTotal += abandoned;
        if (list.Count == 0)
        {
            _byOwner.Remove(owner);
        }

        return new PendingReturnPass(returned, list.Count, abandoned, abandonedUnits);
    }

    /// <summary>Drops everything (save unloaded: the items belong to a game state that no longer exists).</summary>
    public int Clear()
    {
        var dropped = Count;
        _byOwner.Clear();
        Count = 0;
        Units = 0;
        return dropped;
    }

    private sealed class Entry
    {
        public Entry(T item, long units)
        {
            Item = item;
            Units = units;
        }

        public T Item { get; }

        public long Units { get; }

        public int Attempts { get; set; }
    }
}
