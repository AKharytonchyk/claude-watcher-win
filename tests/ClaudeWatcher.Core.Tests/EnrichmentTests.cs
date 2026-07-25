using ClaudeWatcher.Core;
using Xunit;

namespace ClaudeWatcher.Core.Tests;

public class GitBranchTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "cw-git-" + Guid.NewGuid().ToString("N"));

    private void WriteHead(string contents)
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".git"));
        File.WriteAllText(Path.Combine(_repo, ".git", "HEAD"), contents);
    }

    [Fact]
    public void Reads_branch_from_symbolic_ref()
    {
        WriteHead("ref: refs/heads/docs/v0.3.0-sync\n");
        Assert.Equal("docs/v0.3.0-sync", GitBranch.Read(_repo));
    }

    [Fact]
    public void Detached_head_returns_short_sha()
    {
        WriteHead("35fca5a1b2c3d4e5f6\n");
        Assert.Equal("35fca5a", GitBranch.Read(_repo));
    }

    [Fact]
    public void No_git_dir_returns_null() => Assert.Null(GitBranch.Read(_repo));

    public void Dispose()
    {
        try { if (Directory.Exists(_repo)) Directory.Delete(_repo, true); } catch { }
    }
}

public class FleetBuilderTests
{
    private static AgentSession Session(AgentState state, string? waitingReason = null) => new()
    {
        ProviderId = "claude", ProviderName = "Claude Code", ProviderGlyph = "",
        Id = "s", Name = "proj", Pid = 1, Cwd = "/home/u/proj",
        State = state, WaitingReason = waitingReason,
        StateSince = DateTimeOffset.Now.AddMinutes(-4),
        RootId = "wsl:Ubuntu", Origin = "Ubuntu",
    };

    [Fact]
    public void Waiting_uses_reason_for_state_text()
    {
        var v = FleetBuilder.Enrich(Session(AgentState.Waiting, "awaiting your response"),
            new SessionDetail(), branch: null, homePrefix: null, now: DateTimeOffset.Now);
        Assert.Equal("awaiting your response", v.StateText);
        Assert.Equal("4m", v.TimeText);
        Assert.Equal("Ubuntu", v.Origin);
    }

    [Fact]
    public void Working_uses_summary_word()
    {
        var v = FleetBuilder.Enrich(Session(AgentState.Working),
            new SessionDetail(), null, null, DateTimeOffset.Now);
        Assert.Equal("working", v.StateText);
    }

    [Fact]
    public void Context_pressure_and_model_come_from_transcript()
    {
        var detail = new SessionDetail { ContextTokens = 500_000, Model = "claude-opus-4-8", LastPrompt = "  do the thing  " };
        var v = FleetBuilder.Enrich(Session(AgentState.Idle), detail, "main", homePrefix: null, DateTimeOffset.Now);

        Assert.Equal(500_000, v.ContextTokens);
        Assert.Equal(1_000_000, v.ContextWindow);        // opus-4 → 1M
        Assert.Equal(0.5, v.ContextPct);
        Assert.Equal("Opus 4.8", v.ModelLabel);
        Assert.Equal("main", v.Branch);
        Assert.Equal("do the thing", v.Intent);          // trimmed
    }

    [Fact]
    public void ShortCwd_collapses_home_prefix()
    {
        var v = FleetBuilder.Enrich(Session(AgentState.Idle), new SessionDetail(), null,
            homePrefix: "/home/u", now: DateTimeOffset.Now);
        Assert.Equal("~/proj", v.ShortCwd);
    }

    [Fact]
    public void Build_counts_states()
    {
        var sessions = new[] { Session(AgentState.Waiting), Session(AgentState.Working), Session(AgentState.Working) };
        var (views, counts) = FleetBuilder.Build(sessions,
            _ => new SessionDetail(), _ => null, _ => null, DateTimeOffset.Now);

        Assert.Equal(3, views.Count);
        Assert.Equal(new StatusCounts(1, 2, 0), counts);
        Assert.Equal(AgentState.Waiting, counts.Dominant);
    }
}

public class DotGlyphTests
{
    [Fact]
    public void Buffer_has_expected_size()
    {
        Assert.Equal(32 * 32 * 4, DotGlyph.Bgra(32, 0, 0, 0).Length);
    }

    [Fact]
    public void Center_is_opaque_and_colored_corners_transparent()
    {
        const int size = 32;
        var px = DotGlyph.Bgra(size, r: 0xE5, g: 0x48, b: 0x4D); // BGRA order in buffer

        var mid = ((size / 2) * size + (size / 2)) * 4;
        Assert.Equal(0x4D, px[mid + 0]); // B
        Assert.Equal(0x48, px[mid + 1]); // G
        Assert.Equal(0xE5, px[mid + 2]); // R
        Assert.Equal(255, px[mid + 3]);  // A (inside disc)

        Assert.Equal(0, px[3]);          // top-left corner alpha → transparent
    }

    [Fact]
    public void Hex_overload_parses_color()
    {
        var (r, g, b) = DotGlyph.ParseHex("#30A46C");
        Assert.Equal(0x30, r);
        Assert.Equal(0xA4, g);
        Assert.Equal(0x6C, b);
    }
}
