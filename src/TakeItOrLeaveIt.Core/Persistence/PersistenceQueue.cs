using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TakeItOrLeaveIt.Core.Persistence;

/// <summary>
/// Ordered, coalescing write queue for the per-save runtime files (player orders, dealer appointments, dealer alerts).
/// <list type="bullet">
/// <item>Documents are serialised by the caller on the main thread; the queue only holds the finished text and a
/// write action, so background work never touches game or Unity objects.</item>
/// <item>One pending document per destination key (<c>&lt;save identity&gt;|&lt;file kind&gt;</c>). A newer generation
/// replaces an older pending one; an older generation than the pending or the last committed one is refused, so a
/// stale snapshot can never overwrite a newer committed file.</item>
/// <item>Only one runner writes at a time (no overlapping writes to any destination). Keys never share state, so
/// Save A's pending write is still written to Save A's file after the game has switched to Save B.</item>
/// <item>A failed write stays pending and is retried with back-off; the error is reported, never swallowed.</item>
/// <item><see cref="Flush"/> writes everything still pending on the calling thread, bounded by a timeout.</item>
/// </list>
/// </summary>
public sealed class PersistenceQueue
{
    private static readonly double[] BackoffSeconds = { 1, 2, 5, 15, 30 };

    private readonly object _gate = new();
    private readonly object _io = new();
    private readonly Dictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _committed = new(StringComparer.Ordinal);
    private readonly Queue<string> _messages = new();
    private readonly Func<double> _clock;
    private long _written;
    private long _failures;
    private string? _lastError;

    /// <param name="clock">Monotonic seconds (retry back-off). Defaults to a stopwatch; tests pass their own.</param>
    public PersistenceQueue(Func<double>? clock = null)
    {
        if (clock is null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }

        _clock = clock;
    }

    /// <summary>Documents waiting to be written (including ones waiting for a retry).</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Pending documents whose last write attempt failed.</summary>
    public int FailingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Values.Count(e => e.Attempts > 0);
            }
        }
    }

    /// <summary>True when at least one pending document may be written now.</summary>
    public bool HasDue
    {
        get
        {
            var now = _clock();
            lock (_gate)
            {
                return _pending.Values.Any(e => e.DueAt <= now);
            }
        }
    }

    public long WrittenCount => Interlocked.Read(ref _written);

    public long FailureCount => Interlocked.Read(ref _failures);

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public static string KeyFor(string saveIdentity, string kind) => saveIdentity + "|" + kind;

    /// <summary>
    /// Queues <paramref name="write"/> as generation <paramref name="generation"/> of <paramref name="key"/>.
    /// Returns false (and queues nothing) when a newer or equal generation is already pending or committed.
    /// </summary>
    public bool Submit(string key, long generation, Action write)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A destination key is required.", nameof(key));
        }

        if (write is null)
        {
            throw new ArgumentNullException(nameof(write));
        }

        lock (_gate)
        {
            if ((_committed.TryGetValue(key, out var committed) && generation <= committed) ||
                (_pending.TryGetValue(key, out var pending) && generation <= pending.Generation))
            {
                return false;
            }

            _pending[key] = new Entry(key, generation, write, _clock());
            return true;
        }
    }

    /// <summary>True when something for a key starting with <paramref name="keyPrefix"/> is still unwritten.</summary>
    public bool HasPending(string keyPrefix)
    {
        lock (_gate)
        {
            return _pending.Keys.Any(k => k.StartsWith(keyPrefix, StringComparison.Ordinal));
        }
    }

    /// <summary>The generation last written for <paramref name="key"/> (0 = none).</summary>
    public long CommittedGeneration(string key)
    {
        lock (_gate)
        {
            return _committed.TryGetValue(key, out var value) ? value : 0;
        }
    }

    /// <summary>
    /// Writes every pending document that is due (or all of them when <paramref name="force"/>), each at most once
    /// per call, in key order. Waits up to <paramref name="timeoutMs"/> for another runner (-1 = forever); returns
    /// the number written, or -1 when the runner could not be acquired in time.
    /// </summary>
    public int RunPending(bool force = false, int timeoutMs = Timeout.Infinite)
    {
        if (!Monitor.TryEnter(_io, timeoutMs))
        {
            return -1;
        }

        try
        {
            List<Entry> batch;
            var now = _clock();
            lock (_gate)
            {
                batch = _pending.Values
                    .Where(e => force || e.DueAt <= now)
                    .OrderBy(e => e.Key, StringComparer.Ordinal)
                    .ToList();
            }

            var written = 0;
            foreach (var entry in batch)
            {
                Exception? error = null;
                try
                {
                    entry.Write();
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                lock (_gate)
                {
                    var stillCurrent = _pending.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry);
                    if (error is null)
                    {
                        written++;
                        Interlocked.Increment(ref _written);
                        if (!_committed.TryGetValue(entry.Key, out var committed) || entry.Generation > committed)
                        {
                            _committed[entry.Key] = entry.Generation;
                        }

                        if (stillCurrent)
                        {
                            _pending.Remove(entry.Key);
                        }

                        if (entry.Attempts > 0)
                        {
                            Report($"{entry.Key}: written after {entry.Attempts} failed attempt(s).");
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref _failures);
                        _lastError = $"{entry.Key}: {error.GetType().Name}: {error.Message}";
                        if (stillCurrent)
                        {
                            entry.Attempts++;
                            entry.DueAt = _clock() + BackoffSeconds[Math.Min(entry.Attempts, BackoffSeconds.Length) - 1];
                            Report($"Could not write {entry.Key} (attempt {entry.Attempts}; kept in memory and retried): {error.GetType().Name}: {error.Message}");
                        }
                        else
                        {
                            Report($"Could not write {entry.Key} ({error.GetType().Name}: {error.Message}); a newer version is queued.");
                        }
                    }
                }
            }

            return written;
        }
        finally
        {
            Monitor.Exit(_io);
        }
    }

    /// <summary>
    /// Writes everything pending now, on the calling thread (save switch, shutdown). Bounded by
    /// <paramref name="timeoutMs"/> while another runner holds the files. True when nothing is left pending.
    /// </summary>
    public bool Flush(int timeoutMs)
    {
        if (RunPending(force: true, timeoutMs) < 0)
        {
            lock (_gate)
            {
                Report($"Flush timed out after {timeoutMs} ms with {_pending.Count} document(s) pending.");
            }

            return false;
        }

        return PendingCount == 0;
    }

    /// <summary>Messages for the log (taken on the main thread; the queue never logs from a worker).</summary>
    public IReadOnlyList<string> DrainMessages()
    {
        lock (_gate)
        {
            if (_messages.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = _messages.ToList();
            _messages.Clear();
            return list;
        }
    }

    private void Report(string message)
    {
        if (_messages.Count < 64)
        {
            _messages.Enqueue(message);
        }
    }

    private sealed class Entry
    {
        public Entry(string key, long generation, Action write, double dueAt)
        {
            Key = key;
            Generation = generation;
            Write = write;
            DueAt = dueAt;
        }

        public string Key { get; }

        public long Generation { get; }

        public Action Write { get; }

        public int Attempts { get; set; }

        public double DueAt { get; set; }
    }
}
