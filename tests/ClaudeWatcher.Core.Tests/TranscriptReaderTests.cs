using ClaudeWatcher.Core;
using Xunit;

namespace ClaudeWatcher.Core.Tests;

public sealed class TranscriptReaderTests : IDisposable
{
    private readonly string _home;

    public TranscriptReaderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "cw-home-" + Guid.NewGuid().ToString("N"));
    }

    private void WriteTranscript(string cwd, string sessionId, params string[] lines)
    {
        // Folder = cwd with every non-alphanumeric char replaced by '-'.
        var encoded = new string(cwd.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var dir = Path.Combine(_home, ".claude", "projects", encoded);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, sessionId + ".jsonl"), string.Join("\n", lines) + "\n");
    }

    [Fact]
    public void Mines_title_prompt_said_and_usage()
    {
        const string cwd = "/home/u/proj";
        WriteTranscript(cwd, "sess-1",
            """{"type":"ai-title","aiTitle":"My Session"}""",
            """{"type":"last-prompt","lastPrompt":"do the thing"}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Sure, done."}],"usage":{"input_tokens":100,"cache_read_input_tokens":50,"cache_creation_input_tokens":25},"model":"claude-opus-4-8"}}""");

        var d = new TranscriptReader().Detail(new Session { SessionId = "sess-1", Cwd = cwd }, _home);

        Assert.Equal("My Session", d.Title);
        Assert.Equal("do the thing", d.LastPrompt);
        Assert.Equal("Sure, done.", d.LastSaid);
        Assert.Equal(175, d.ContextTokens);              // 100 + 50 + 25
        Assert.Equal("claude-opus-4-8", d.Model);
    }

    [Fact]
    public void Newest_assistant_turn_wins()
    {
        const string cwd = "/x";
        WriteTranscript(cwd, "s",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"older"}],"usage":{"input_tokens":10}}}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"newer"}],"usage":{"input_tokens":20}}}""");

        var d = new TranscriptReader().Detail(new Session { SessionId = "s", Cwd = cwd }, _home);

        Assert.Equal("newer", d.LastSaid);
        Assert.Equal(20, d.ContextTokens);
    }

    [Fact]
    public void Missing_transcript_yields_empty_detail()
    {
        var d = new TranscriptReader().Detail(new Session { SessionId = "nope", Cwd = "/x" }, _home);
        Assert.Null(d.Title);
        Assert.Null(d.LastSaid);
        Assert.Null(d.ContextTokens);
    }

    [Fact]
    public void Rejects_unsafe_session_id()
    {
        // A traversal-y id must resolve to no path (defense-in-depth).
        var d = new TranscriptReader().Detail(new Session { SessionId = "../../etc/passwd", Cwd = "/x" }, _home);
        Assert.Null(d.Title);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true); }
        catch { /* best effort */ }
    }
}
