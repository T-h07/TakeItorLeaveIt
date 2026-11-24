using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Menu;
using TakeItOrLeaveIt.Core.Persistence;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Core.Tests;

/// <summary>
/// Thread-safe in-memory file system with fault injection (Phase 06): failing reads, writes and replaces, writes that
/// do not read back, a crash between the temporary write and the rename, and a blocking write.
/// </summary>
internal sealed class FaultyFileSystem : ISettingsFileSystem
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public HashSet<string> FailReadsOf { get; } = new(StringComparer.Ordinal);

    public bool FailWrites { get; set; }

    public bool FailReplace { get; set; }

    /// <summary>The temporary file gets different bytes than requested (torn or corrupted write).</summary>
    public bool CorruptWrites { get; set; }

    /// <summary>When set, a write waits for this event (simulates a slow disk).</summary>
    public ManualResetEventSlim? WriteGate { get; set; }

    /// <summary>Signalled when a write starts waiting on <see cref="WriteGate"/>.</summary>
    public ManualResetEventSlim WriteStarted { get; } = new(false);

    public int Writes { get; private set; }

    public string this[string path]
    {
        get
        {
            lock (_gate)
            {
                return _files[path];
            }
        }

        set
        {
            lock (_gate)
            {
                _files[path] = value;
            }
        }
    }

    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate)
            {
                return _files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            }
        }
    }

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(path);
        }
    }

    public string ReadAllText(string path)
    {
        lock (_gate)
        {
            if (FailReadsOf.Contains(path))
            {
                throw new IOException("file is locked by another process (simulated)");
            }

            return _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);
        }
    }

    public void WriteAllText(string path, string contents)
    {
        if (WriteGate is { } gate)
        {
            WriteStarted.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
        }

        lock (_gate)
        {
            if (FailWrites)
            {
                throw new IOException("disk full (simulated)");
            }

            Writes++;
            _files[path] = CorruptWrites ? contents.Substring(0, contents.Length / 2) : contents;
        }
    }

    public void ReplaceFile(string source, string destination)
    {
        lock (_gate)
        {
            if (FailReplace)
            {
                throw new IOException("replace failed (simulated crash before the rename)");
            }

            _files[destination] = _files[source];
            _files.Remove(source);
        }
    }

    public void CopyFile(string source, string destination)
    {
        lock (_gate)
        {
            _files[destination] = _files[source];
        }
    }

    public void DeleteFile(string path)
    {
        lock (_gate)
        {
            _files.Remove(path);
        }
    }

    public void CreateDirectory(string path)
    {
    }

    public IEnumerable<string> ListFiles(string directory, string prefix)
    {
        lock (_gate)
        {
            return _files.Keys.Where(k => string.Equals(Path.GetDirectoryName(k), directory, StringComparison.Ordinal) &&
                                          Path.GetFileName(k).StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }
    }
}

/// <summary>Manual clock for back-off and file time stamps.</summary>
internal sealed class TestClock
{
    public double Seconds { get; set; }

    public DateTime Utc { get; set; } = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(double seconds)
    {
        Seconds += seconds;
        Utc = Utc.AddSeconds(seconds);
    }
}

/// <summary>A menu controller wired to the real <see cref="JsonPolicyStore"/> on a fault-injecting file system.</summary>
internal sealed class DiskMenuHarness
{
    public DiskMenuHarness(FaultyFileSystem? fs = null, Func<DiscoveryResult>? discovery = null)
    {
        Fs = fs ?? new FaultyFileSystem();
        Source = new FakeDiscoverySource { Next = discovery ?? TakeItOrLeaveIt.Core.Tests.Catalog.Standard };
        Catalog = new ProductCatalogService(Source);
        Store = new JsonPolicyStore("root", Fs, () => Clock.Utc);
        Provider = new InMemoryPolicyProvider();
        Menu = new ModMenuController(Catalog, Store, Provider, Log);
    }

    public FaultyFileSystem Fs { get; }

    public TestClock Clock { get; } = new();

    public FakeDiscoverySource Source { get; }

    public ProductCatalogService Catalog { get; }

    public JsonPolicyStore Store { get; }

    public InMemoryPolicyProvider Provider { get; }

    public RecordingLog Log { get; } = new();

    public ModMenuController Menu { get; }

    public DiskMenuHarness Load(string save)
    {
        Catalog.Refresh(Catalog.BeginSave(save));
        return this;
    }

    public string PathOf(string save) => Store.PathFor(save);

    /// <summary>A restart: a new controller and store over the same files.</summary>
    public DiskMenuHarness Restart() => new(Fs, Source.Next);
}
