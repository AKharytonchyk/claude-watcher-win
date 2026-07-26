using ClaudeWatcher.Core;
using ClaudeWatcher.Core.Roots;
using Xunit;

namespace ClaudeWatcher.Core.Tests;

/// <summary>A watch root over a real temp directory, with scripted liveness.</summary>
internal sealed class FakeRoot : IWatchRoot, IDisposable
{
    private readonly HashSet<int> _alivePids;
    public string Id { get; }
    public string Origin { get; }
    public bool IsWsl { get; }
    public string? Distro { get; }
    public string SessionsDir { get; }

    public FakeRoot(string id, string origin, IEnumerable<int> alivePids, bool exists = true, bool isWsl = false)
    {
        Id = id;
        Origin = origin;
        IsWsl = isWsl;
        Distro = isWsl ? origin : null;
        _alivePids = alivePids.ToHashSet();
        SessionsDir = Path.Combine(Path.GetTempPath(), "cw-test-" + Guid.NewGuid().ToString("N"));
        if (exists) Directory.CreateDirectory(SessionsDir);
    }

    public string HomeDir => Path.GetTempPath();
    public string ResolvePath(string sessionCwd) => sessionCwd;

    /// <summary>Records what liveness was asked about, so tests can assert on it.</summary>
    public List<(int Pid, DateTimeOffset? StartedAt)> LivenessQueries { get; } = new();

    public bool IsAlive(int pid, DateTimeOffset? startedAt)
    {
        LivenessQueries.Add((pid, startedAt));
        return _alivePids.Contains(pid);
    }

    public void Write(int pid, string sessionId, string cwd, string status,
                      string? waitingFor = null, double? startedAt = null)
    {
        var wf = waitingFor is null ? "" : $"\"waitingFor\":\"{waitingFor}\",";
        var st = startedAt is null ? "" : $"\"startedAt\":{startedAt.Value},";
        var json = $$"""
            {"pid":{{pid}},"sessionId":"{{sessionId}}","cwd":"{{cwd}}",{{st}}"status":"{{status}}",{{wf}}"kind":"interactive"}
            """;
        File.WriteAllText(Path.Combine(SessionsDir, $"{pid}.json"), json);
    }

    public void WriteRaw(string fileName, string contents) =>
        File.WriteAllText(Path.Combine(SessionsDir, fileName), contents);

    public void Dispose()
    {
        try { if (Directory.Exists(SessionsDir)) Directory.Delete(SessionsDir, recursive: true); }
        catch { /* best effort */ }
    }
}

public class ClaudeSourceTests
{
    [Fact]
    public void Reads_and_normalizes_live_sessions()
    {
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { 100, 200 });
        root.Write(100, "sess-a", "/home/u/proj-a", "waiting", waitingFor: "permission prompt", startedAt: 2000);
        root.Write(200, "sess-b", "/home/u/proj-b", "busy", startedAt: 1000);

        var agents = new ClaudeSource(new[] { root }).LiveSessions();

        Assert.Equal(2, agents.Count);
        // oldest first → proj-b (started 1000) before proj-a (2000).
        Assert.Equal("proj-b", agents[0].Name);
        Assert.Equal(AgentState.Working, agents[0].State);
        Assert.Equal("proj-a", agents[1].Name);
        Assert.Equal(AgentState.Waiting, agents[1].State);
        Assert.Equal("awaiting your response", agents[1].WaitingReason);
        Assert.All(agents, a => Assert.Equal("native", a.RootId));
        Assert.All(agents, a => Assert.Equal("PowerShell", a.Origin));
    }

    [Fact]
    public void Drops_dead_processes()
    {
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { 1 }); // 2 is dead
        root.Write(1, "alive", "/x", "idle");
        root.Write(2, "dead", "/y", "busy");

        var agents = new ClaudeSource(new[] { root }).LiveSessions();

        Assert.Single(agents);
        Assert.Equal("alive", agents[0].Id);
    }

    [Fact]
    public void Waiting_reason_only_set_when_waiting()
    {
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { 1 });
        root.Write(1, "s", "/x", "busy", waitingFor: "permission prompt");

        var agent = Assert.Single(new ClaudeSource(new[] { root }).LiveSessions());
        Assert.Null(agent.WaitingReason);
    }

    [Fact]
    public void Skips_malformed_files_but_keeps_valid_ones()
    {
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { 1 });
        root.Write(1, "good", "/x", "idle");
        root.WriteRaw("garbage.json", "{ not json ]");

        var agent = Assert.Single(new ClaudeSource(new[] { root }).LiveSessions());
        Assert.Equal("good", agent.Id);
    }

    [Fact]
    public void Aggregates_across_roots_and_tags_origin()
    {
        using var native = new FakeRoot("native", "PowerShell", alivePids: new[] { 1 });
        using var wsl = new FakeRoot("wsl:Ubuntu", "Ubuntu", alivePids: new[] { 1 }, isWsl: true);
        native.Write(1, "win", "/c/proj", "idle", startedAt: 1);
        wsl.Write(1, "lin", "/home/u/proj", "waiting", waitingFor: "input needed", startedAt: 2);

        var agents = new ClaudeSource(new IWatchRoot[] { native, wsl }).LiveSessions();

        Assert.Equal(2, agents.Count);
        Assert.Equal("PowerShell", agents.Single(a => a.Id == "win").Origin);
        Assert.Equal("Ubuntu", agents.Single(a => a.Id == "lin").Origin);
        Assert.Equal("wsl:Ubuntu", agents.Single(a => a.Id == "lin").RootId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Drops_non_positive_pids_without_probing_liveness(int pid)
    {
        // `kill -0 0` hits the caller's process group and `kill -0 -1` every permitted
        // process, so such a pid must never reach the liveness probe at all.
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { pid });
        root.Write(pid, "phantom", "/x", "busy");

        var agents = new ClaudeSource(new[] { root }).LiveSessions();

        Assert.Empty(agents);
        Assert.Empty(root.LivenessQueries);
    }

    [Fact]
    public void Passes_the_declared_start_time_to_the_liveness_check()
    {
        // The root needs it to reject a recycled pid.
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { 42 });
        root.Write(42, "s", "/x", "idle", startedAt: 1_700_000_000_000);

        new ClaudeSource(new[] { root }).LiveSessions();

        var q = Assert.Single(root.LivenessQueries);
        Assert.Equal(42, q.Pid);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), q.StartedAt);
    }

    [Fact]
    public void Missing_start_time_is_passed_as_null_so_roots_can_fail_open()
    {
        using var root = new FakeRoot("native", "PowerShell", alivePids: new[] { 7 });
        root.Write(7, "s", "/x", "idle");   // no startedAt

        new ClaudeSource(new[] { root }).LiveSessions();

        Assert.Null(Assert.Single(root.LivenessQueries).StartedAt);
    }

    [Theory]
    // host present, native → the host alone; "Windows" adds nothing
    [InlineData("VS Code", "Windows", false, "VS Code")]
    [InlineData("Terminal", "Windows", false, "Terminal")]
    // host present, WSL → host AND distro, because the distro is real information
    [InlineData("Terminal", "Ubuntu", true, "Terminal · Ubuntu")]
    // no host (every WSL agent, and any unrecognized native host) → root stands alone
    [InlineData(null, "Ubuntu", true, "Ubuntu")]
    [InlineData(null, "Windows", false, "Windows")]
    [InlineData("", "Windows", false, "Windows")]
    public void Composes_the_provenance_line(string? host, string origin, bool isWsl, string expected) =>
        Assert.Equal(expected, FleetBuilder.OriginLine(host, origin, isWsl));

    [Fact]
    public void Host_lookup_is_surfaced_on_the_view()
    {
        using var root = new FakeRoot("native", "Windows", alivePids: new[] { 5 });
        root.Write(5, "s", "/x", "idle");
        var sessions = new ClaudeSource(new[] { root }).LiveSessions();

        var (views, _) = FleetBuilder.Build(sessions,
            detail: _ => new SessionDetail(), branch: _ => null, homePrefix: _ => null,
            now: DateTimeOffset.Now, host: _ => "VS Code");

        var v = Assert.Single(views);
        Assert.Equal("VS Code", v.Host);
        Assert.Equal("VS Code", v.OriginText);
        Assert.Equal("Windows", v.Origin);   // root label still available
    }

    [Fact]
    public void Wsl_sessions_are_flagged_so_callers_skip_windows_only_lookups()
    {
        using var native = new FakeRoot("native", "Windows", alivePids: new[] { 1 });
        using var wsl = new FakeRoot("wsl:Ubuntu", "Ubuntu", alivePids: new[] { 1 }, isWsl: true);
        native.Write(1, "win", "/c/proj", "idle", startedAt: 1);
        wsl.Write(1, "lin", "/home/u/proj", "idle", startedAt: 2);

        var agents = new ClaudeSource(new IWatchRoot[] { native, wsl }).LiveSessions();

        Assert.False(agents.Single(a => a.Id == "win").IsWsl);
        Assert.True(agents.Single(a => a.Id == "lin").IsWsl);
    }

    [Fact]
    public void Missing_directory_yields_nothing_and_is_not_available()
    {
        using var root = new FakeRoot("wsl:Stopped", "Stopped", alivePids: new[] { 1 }, exists: false, isWsl: true);
        var source = new ClaudeSource(new[] { root });

        Assert.False(source.IsAvailable);
        Assert.Empty(source.LiveSessions());
    }
}
