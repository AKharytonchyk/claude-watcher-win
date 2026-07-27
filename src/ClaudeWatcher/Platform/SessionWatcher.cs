using ClaudeWatcher.Core.Roots;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Emits a coalesced "sessions changed" signal across all roots. Native roots get
/// an event-driven <see cref="FileSystemWatcher"/> for low latency; WSL roots are
/// POLLED (FileSystemWatcher does not fire reliably over the \\wsl$ 9P share).
/// Polling also covers native dirs that don't exist yet. All signals funnel
/// through a short debounce so a burst of file events raises one <see cref="Changed"/>.
///
/// UNVERIFIED (Windows-only).
/// </summary>
public sealed class SessionWatcher(IReadOnlyList<IWatchRoot> roots) : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly List<FileSystemWatcher> _fsWatchers = new();
    private readonly HashSet<string> _watchedDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _signatures = new(); // rootId → last dir signature
    private Timer? _pollTimer;
    private Timer? _debounceTimer;
    private readonly object _gate = new();

    /// <summary>Raised (debounced) whenever any root's sessions may have changed.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<IWatchRoot> Roots => roots;

    public void Start()
    {
        SyncWatchers();

        // Poll all roots (cheap signature) — catches WSL + late-created native dirs.
        _pollTimer = new Timer(_ => PollOnce(), null, TimeSpan.Zero, PollInterval);
    }

    /// <summary>
    /// Arm a <see cref="FileSystemWatcher"/> for every native root whose directory
    /// exists, and drop watchers whose directory went away.
    ///
    /// A watcher created on a path that does not exist yet never fires, not even once
    /// the directory appears — so an absent dir must be left unwatched and re-armed
    /// later. Polling still covers it in the meantime; this only upgrades it back to
    /// instant events. Called from <see cref="Start"/> and each poll, and only does
    /// work when the set of existing directories actually changes.
    /// </summary>
    private void SyncWatchers()
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root.IsWsl) continue;                 // WSL is polled, not watched
            try
            {
                if (Directory.Exists(root.SessionsDir)) wanted.Add(root.SessionsDir);
            }
            catch (Exception) { /* unreachable path — leave it to polling */ }
        }

        List<FileSystemWatcher> retired;
        lock (_gate)
        {
            if (wanted.SetEquals(_watchedDirs)) return;   // nothing to do — the common case

            retired = new List<FileSystemWatcher>(_fsWatchers);
            _fsWatchers.Clear();
            _watchedDirs.Clear();
        }

        // Dispose and construct OUTSIDE the lock. Disposing a watcher can run pending
        // event callbacks, and those call ScheduleChanged, which takes _gate — holding
        // it here would deadlock.
        foreach (var w in retired) { try { w.Dispose(); } catch { } }

        var created = new List<(string Dir, FileSystemWatcher Watcher)>();
        foreach (var dir in wanted)
        {
            try
            {
                var w = new FileSystemWatcher(dir, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };
                w.Created += OnFsEvent;
                w.Changed += OnFsEvent;
                w.Deleted += OnFsEvent;
                w.Renamed += OnFsEvent;
                created.Add((dir, w));
            }
            catch (Exception) { /* fall back to polling for this root */ }
        }

        lock (_gate)
        {
            foreach (var (dir, w) in created)
            {
                _fsWatchers.Add(w);
                _watchedDirs.Add(dir);
            }
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => ScheduleChanged();

    private void PollOnce()
    {
        SyncWatchers();   // a sessions dir may have just appeared (or vanished)

        var changed = false;
        foreach (var root in roots)
        {
            var sig = Signature(root.SessionsDir);
            lock (_gate)
            {
                if (!_signatures.TryGetValue(root.Id, out var prev) || prev != sig)
                {
                    _signatures[root.Id] = sig;
                    changed = true;
                }
            }
        }
        if (changed) ScheduleChanged();
    }

    /// <summary>Cheap directory fingerprint: name+size+mtime of each *.json.</summary>
    private static string Signature(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return "";
            var parts = new List<string>();
            foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
            {
                var fi = new FileInfo(f);
                parts.Add($"{fi.Name}:{fi.Length}:{fi.LastWriteTimeUtc.Ticks}");
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
        catch (Exception) { return ""; }              // stopped distro, race, etc.
    }

    private void ScheduleChanged()
    {
        lock (_gate)
        {
            _debounceTimer ??= new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
            _debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();       // stop re-arming before tearing the watchers down

        List<FileSystemWatcher> retired;
        lock (_gate)
        {
            retired = new List<FileSystemWatcher>(_fsWatchers);
            _fsWatchers.Clear();
            _watchedDirs.Clear();
        }
        foreach (var w in retired) { try { w.Dispose(); } catch { } }   // outside the lock

        _debounceTimer?.Dispose();
    }
}
