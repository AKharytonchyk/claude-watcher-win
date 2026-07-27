using System.Diagnostics;
using System.Text.Json;
using ClaudeWatcher.Core;

namespace ClaudeWatcher.Platform;

/// <summary>
/// The ONLY outbound network path: the open-PR lookup for an agent's branch, via the
/// `gh` CLI. Disable-able with CWATCH_OFFLINE (Constitution §1), which short-circuits
/// before any process is spawned.
///
/// Never blocks a refresh. <see cref="Lookup"/> answers instantly from cache and, on a
/// miss, schedules the `gh` call on a background thread; when that lands it raises
/// <see cref="Updated"/> so the app can rebuild. Spawning `gh` inline would put ~500 ms
/// per repo into every refresh, and refreshes happen as often as every 1.5 s.
/// </summary>
public sealed class PrChecker
{
    /// <summary>PR state changes slowly; re-asking often would be rude to the network.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, (DateTimeOffset At, PrRef? Pr)> _cache = new();
    private readonly HashSet<string> _inFlight = new();
    private readonly object _gate = new();

    /// <summary>Raised (off the UI thread) when a lookup finished and the cache changed.</summary>
    public event Action? Updated;

    public static bool Offline =>
        Environment.GetEnvironmentVariable("CWATCH_OFFLINE") is { Length: > 0 };

    /// <summary>
    /// The open PR for <paramref name="branch"/> in the repo at <paramref name="repoPath"/>,
    /// or null when unknown, absent, or offline. Returns immediately.
    /// </summary>
    public PrRef? Lookup(string? repoPath, string? branch)
    {
        if (Offline || string.IsNullOrWhiteSpace(repoPath) || string.IsNullOrWhiteSpace(branch))
            return null;

        var key = repoPath + "|" + branch;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                if (DateTimeOffset.UtcNow - hit.At < Ttl) return hit.Pr;
            }
            if (!_inFlight.Add(key)) return hit.Pr;   // already being fetched; serve what we have
        }

        _ = Task.Run(() =>
        {
            var pr = Query(repoPath!, branch!);
            lock (_gate)
            {
                _cache[key] = (DateTimeOffset.UtcNow, pr);
                _inFlight.Remove(key);
            }
            Updated?.Invoke();
        });

        lock (_gate) return _cache.TryGetValue(key, out var stale) ? stale.Pr : null;
    }

    /// <summary>Forget repos/branches nothing is running in any more.</summary>
    public void Prune(IEnumerable<string> liveKeys)
    {
        var live = liveKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var key in _cache.Keys.Where(k => !live.Contains(k)).ToList())
                _cache.Remove(key);
    }

    /// <summary>Cache key for a session, so callers can build the live set for Prune.</summary>
    public static string KeyFor(string? repoPath, string? branch) => repoPath + "|" + branch;

    private static PrRef? Query(string repoPath, string branch)
    {
        try
        {
            var psi = new ProcessStartInfo("gh")
            {
                ArgumentList =
                {
                    "pr", "list",
                    "--head", branch,
                    "--state", "open",
                    "--json", "number,url,isDraft",
                    "--limit", "1",
                },
                // Run in the repo so gh resolves the right remote — not the app's cwd.
                WorkingDirectory = Directory.Exists(repoPath) ? repoPath : Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null) return null;

            var json = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit((int)Timeout.TotalMilliseconds)) { TryKill(p); return null; }
            if (p.ExitCode != 0) return null;   // not a repo, no remote, not authenticated, offline

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("number", out var n) || !n.TryGetInt32(out var number)) continue;
                var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var draft = el.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True;
                return new PrRef(number, url, draft);
            }
            return null;
        }
        catch (Exception)
        {
            return null;   // gh missing, path gone, malformed output — all "no PR"
        }
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
    }
}
