using System.Text;
using ClaudeWatcher.Core;
using Xunit;

namespace ClaudeWatcher.Core.Tests;

/// <summary>
/// Covers the tail-read path: only the last 1 MiB is parsed, so anything the window
/// misses must be carried over from the previous parse — and carrying must STOP when
/// the file was rewritten rather than appended to.
/// </summary>
public sealed class TranscriptTailTests : IDisposable
{
    private const int TailWindow = 1 << 20;

    private readonly string _home = Path.Combine(Path.GetTempPath(), "cw-tail-" + Guid.NewGuid().ToString("N"));
    private const string Cwd = "/home/u/proj";

    private string PathFor(string sessionId)
    {
        var encoded = new string(Cwd.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var dir = Path.Combine(_home, ".claude", "projects", encoded);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, sessionId + ".jsonl");
    }

    private static string Title(string t) => $"{{\"type\":\"ai-title\",\"aiTitle\":\"{t}\"}}";
    private static string Prompt(string p) => $"{{\"type\":\"last-prompt\",\"lastPrompt\":\"{p}\"}}";

    private static string Assistant(string text, int tokens, string model = "claude-opus-5") =>
        $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":\"{text}\"}}]," +
        $"\"usage\":{{\"input_tokens\":{tokens}}},\"model\":\"{model}\"}}}}";

    /// <summary>A filler line that is valid JSON but contributes no mined field.</summary>
    private static string Filler(int i) => $"{{\"type\":\"noise\",\"i\":{i},\"pad\":\"{new string('x', 200)}\"}}";

    /// <summary>Filler totalling at least <paramref name="bytes"/>.</summary>
    private static IEnumerable<string> FillerOfAtLeast(int bytes)
    {
        var produced = 0;
        for (var i = 0; produced < bytes; i++)
        {
            var line = Filler(i);
            produced += line.Length + 1;
            yield return line;
        }
    }

    private static void Write(string path, IEnumerable<string> lines) =>
        File.WriteAllText(path, string.Join("\n", lines) + "\n");

    private static void Append(string path, IEnumerable<string> lines) =>
        File.AppendAllText(path, string.Join("\n", lines) + "\n");

    [Fact]
    public void Reads_newest_entries_from_a_file_far_larger_than_the_window()
    {
        var path = PathFor("big");
        Write(path, FillerOfAtLeast(TailWindow + 500_000)
            .Append(Title("Big Session"))
            .Append(Prompt("what is up"))
            .Append(Assistant("all good", 4321)));

        Assert.True(new FileInfo(path).Length > TailWindow);

        var d = new TranscriptReader().Detail(new Session { SessionId = "big", Cwd = Cwd }, _home);

        Assert.Equal("Big Session", d.Title);
        Assert.Equal("what is up", d.LastPrompt);
        Assert.Equal("all good", d.LastSaid);
        Assert.Equal(4321, d.ContextTokens);
        Assert.Equal("claude-opus-5", d.Model);
    }

    [Fact]
    public void Carries_forward_fields_a_later_huge_append_pushed_out_of_the_window()
    {
        var path = PathFor("carry");
        var reader = new TranscriptReader();

        // First pass sees everything.
        Write(path, new[] { Title("Carried"), Prompt("original ask"), Assistant("first reply", 100) });
        var first = reader.Detail(new Session { SessionId = "carry", Cwd = Cwd }, _home);
        Assert.Equal("Carried", first.Title);
        Assert.Equal("original ask", first.LastPrompt);

        // One turn appends more than the whole window, so the tail can no longer see
        // the title or the prompt — they must survive via carry-forward.
        Append(path, FillerOfAtLeast(TailWindow + 200_000).Append(Assistant("second reply", 200)));

        var second = reader.Detail(new Session { SessionId = "carry", Cwd = Cwd }, _home);

        Assert.Equal("Carried", second.Title);           // carried
        Assert.Equal("original ask", second.LastPrompt); // carried
        Assert.Equal("second reply", second.LastSaid);   // fresh
        Assert.Equal(200, second.ContextTokens);         // fresh
    }

    [Fact]
    public void Newer_values_always_win_over_carried_ones()
    {
        var path = PathFor("fresh-wins");
        var reader = new TranscriptReader();

        Write(path, new[] { Title("Old"), Prompt("old ask"), Assistant("old reply", 10) });
        reader.Detail(new Session { SessionId = "fresh-wins", Cwd = Cwd }, _home);

        Append(path, new[] { Title("New"), Prompt("new ask"), Assistant("new reply", 20) });
        var d = reader.Detail(new Session { SessionId = "fresh-wins", Cwd = Cwd }, _home);

        Assert.Equal("New", d.Title);
        Assert.Equal("new ask", d.LastPrompt);
        Assert.Equal("new reply", d.LastSaid);
        Assert.Equal(20, d.ContextTokens);
    }

    [Fact]
    public void A_rewrite_that_shrank_the_file_drops_carried_values()
    {
        var path = PathFor("shrink");
        var reader = new TranscriptReader();

        Write(path, new[] { Title("Before"), Prompt("before ask"), Assistant("before reply", 100) });
        Assert.Equal("Before", reader.Detail(new Session { SessionId = "shrink", Cwd = Cwd }, _home).Title);

        // --rewind / transcript prune: rewritten shorter, with no title or prompt.
        Write(path, new[] { Assistant("after reply", 200) });

        var d = reader.Detail(new Session { SessionId = "shrink", Cwd = Cwd }, _home);

        Assert.Null(d.Title);            // must NOT be carried across a rewrite
        Assert.Null(d.LastPrompt);
        Assert.Equal("after reply", d.LastSaid);
        Assert.Equal(200, d.ContextTokens);
    }

    [Fact]
    public void A_rewrite_that_kept_the_length_is_caught_by_the_fingerprint()
    {
        var path = PathFor("same-length");
        var reader = new TranscriptReader();

        Write(path, new[] { Title("Before"), Prompt("before ask"), Assistant("before reply", 100) });
        var before = reader.Detail(new Session { SessionId = "same-length", Cwd = Cwd }, _home);
        Assert.Equal("Before", before.Title);
        var length = new FileInfo(path).Length;

        // Same byte count, different content, and no title/prompt any more. A length
        // check alone would happily keep serving the stale carried title.
        var replacement = new[] { Assistant("after reply", 200) }.ToList();
        var padded = string.Join("\n", replacement) + "\n";
        padded += new string('\n', (int)Math.Max(0, length - padded.Length));
        File.WriteAllText(path, padded);
        Assert.Equal(length, new FileInfo(path).Length);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));   // ensure mtime differs

        var after = reader.Detail(new Session { SessionId = "same-length", Cwd = Cwd }, _home);

        Assert.Null(after.Title);
        Assert.Null(after.LastPrompt);
        Assert.Equal("after reply", after.LastSaid);
    }

    [Fact]
    public void A_plain_append_is_not_mistaken_for_a_rewrite()
    {
        var path = PathFor("append");
        var reader = new TranscriptReader();

        Write(path, new[] { Title("Kept"), Prompt("kept ask"), Assistant("r1", 100) });
        reader.Detail(new Session { SessionId = "append", Cwd = Cwd }, _home);

        for (var i = 0; i < 5; i++)
        {
            Append(path, new[] { Assistant($"r{i + 2}", 100 + i) });
            var d = reader.Detail(new Session { SessionId = "append", Cwd = Cwd }, _home);
            Assert.Equal("Kept", d.Title);            // still carried across every append
            Assert.Equal("kept ask", d.LastPrompt);
            Assert.Equal($"r{i + 2}", d.LastSaid);
        }
    }

    [Fact]
    public void Multibyte_text_straddling_the_window_edge_does_not_corrupt_parsed_lines()
    {
        var path = PathFor("utf8");
        // Emoji + CJK padding so the 1 MiB boundary lands mid-character.
        var pad = string.Concat(Enumerable.Repeat("日本語🎉", 60));
        var lines = new List<string>();
        var produced = 0;
        for (var i = 0; produced < TailWindow + 100_000; i++)
        {
            var line = $"{{\"type\":\"noise\",\"i\":{i},\"pad\":\"{pad}\"}}";
            produced += Encoding.UTF8.GetByteCount(line) + 1;
            lines.Add(line);
        }
        lines.Add(Prompt("naïve café 🎉 中文"));
        lines.Add(Assistant("réponse 日本語", 777));
        Write(path, lines);

        var d = new TranscriptReader().Detail(new Session { SessionId = "utf8", Cwd = Cwd }, _home);

        Assert.Equal("naïve café 🎉 中文", d.LastPrompt);
        Assert.Equal("réponse 日本語", d.LastSaid);
        Assert.Equal(777, d.ContextTokens);
    }

    [Fact]
    public void Truncate_to_empty_then_regrow_past_the_window_still_finds_fields()
    {
        // The macOS PR documents this as a known limitation (the zero-byte read marks
        // the session fully read, so lastPrompt stays unset). Guard against it here.
        var path = PathFor("truncate");
        var reader = new TranscriptReader();

        Write(path, new[] { Prompt("first ask"), Assistant("first", 10) });
        Assert.Equal("first ask", reader.Detail(new Session { SessionId = "truncate", Cwd = Cwd }, _home).LastPrompt);

        File.WriteAllText(path, "");
        var empty = reader.Detail(new Session { SessionId = "truncate", Cwd = Cwd }, _home);
        Assert.Null(empty.LastPrompt);

        Write(path, FillerOfAtLeast(TailWindow + 50_000)
            .Prepend(Prompt("regrown ask"))
            .Append(Assistant("regrown", 20)));

        var d = reader.Detail(new Session { SessionId = "truncate", Cwd = Cwd }, _home);

        Assert.Equal(20, d.ContextTokens);
        Assert.Equal("regrown ask", d.LastPrompt);   // needs the one-time full read
    }

    [Fact]
    public void A_utf8_bom_does_not_hide_the_first_line()
    {
        // Reading raw bytes (unlike File.ReadAllText) keeps the BOM, which makes line 1
        // fail to parse while later lines still succeed — so tokens would resolve while
        // the prompt silently vanished.
        var path = PathFor("bom");
        var body = string.Join("\n", new[] { Prompt("first line matters"), Assistant("reply", 42) }) + "\n";
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.Equal(0xEF, File.ReadAllBytes(path)[0]);   // BOM really is there

        var d = new TranscriptReader().Detail(new Session { SessionId = "bom", Cwd = Cwd }, _home);

        Assert.Equal("first line matters", d.LastPrompt);
        Assert.Equal(42, d.ContextTokens);
    }

    [Fact]
    public void Cache_is_keyed_by_path_so_a_shared_session_id_across_cwds_does_not_collide()
    {
        var reader = new TranscriptReader();
        const string idA = "dup";

        var encodedB = new string("/other/dir".Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var dirB = Path.Combine(_home, ".claude", "projects", encodedB);
        Directory.CreateDirectory(dirB);

        Write(PathFor(idA), new[] { Prompt("from A"), Assistant("a", 1) });
        Write(Path.Combine(dirB, idA + ".jsonl"), new[] { Prompt("from B"), Assistant("b", 2) });

        var a = reader.Detail(idA, Cwd, _home);
        var b = reader.Detail(idA, "/other/dir", _home);

        Assert.Equal("from A", a.LastPrompt);
        Assert.Equal("from B", b.LastPrompt);   // would be "from A" if keyed by sessionId
    }

    [Fact]
    public void Prune_drops_entries_for_sessions_that_are_gone()
    {
        var reader = new TranscriptReader();
        Write(PathFor("keep"), new[] { Assistant("k", 1) });
        Write(PathFor("drop"), new[] { Assistant("d", 2) });

        reader.Detail(new Session { SessionId = "keep", Cwd = Cwd }, _home);
        reader.Detail(new Session { SessionId = "drop", Cwd = Cwd }, _home);
        Assert.Equal(2, reader.CacheCount);

        reader.Prune(new[] { "keep" });

        Assert.Equal(1, reader.CacheCount);
    }

    [Fact]
    public void Unchanged_file_is_served_from_cache()
    {
        var path = PathFor("cached");
        var reader = new TranscriptReader();
        Write(path, new[] { Title("T"), Prompt("p"), Assistant("s", 5) });

        var first = reader.Detail(new Session { SessionId = "cached", Cwd = Cwd }, _home);
        var second = reader.Detail(new Session { SessionId = "cached", Cwd = Cwd }, _home);

        Assert.Same(first, second);   // same instance ⇒ no re-parse
    }

    [Fact]
    public async Task Concurrent_readers_and_prunes_do_not_corrupt_the_cache()
    {
        // This port enriches on a background thread and refreshes can overlap, so the
        // cache must tolerate concurrent Detail()/Prune() without throwing or hanging.
        var reader = new TranscriptReader();
        var ids = Enumerable.Range(0, 24).Select(i => "sess" + i).ToArray();
        foreach (var id in ids)
            Write(PathFor(id), new[] { Title($"T{id}"), Prompt($"p{id}"), Assistant($"s{id}", 10) });

        var errors = new List<Exception>();
        var work = ids.Select(id => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 120; i++)
                {
                    var d = reader.Detail(new Session { SessionId = id, Cwd = Cwd }, _home);
                    Assert.Equal($"T{id}", d.Title);          // never another session's detail
                    Assert.Equal($"p{id}", d.LastPrompt);
                    if (i % 7 == 0) reader.Prune(ids.Take(12));
                    if (i % 11 == 0) File.AppendAllText(PathFor(id), Assistant($"s{id}", 11) + "\n");
                }
            }
            catch (Exception ex) { lock (errors) errors.Add(ex); }
        })).ToArray();

        var all = Task.WhenAll(work);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.Same(all, finished);   // a hang here means the cache deadlocked
        await all;                   // surface any exception the tasks threw
        Assert.Empty(errors);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true); }
        catch { /* best effort */ }
    }
}
