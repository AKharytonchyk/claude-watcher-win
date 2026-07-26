using System.Text;
using System.Text.Json;

namespace ClaudeWatcher.Core;

/// <summary>Details mined from a session's transcript.</summary>
public sealed record SessionDetail
{
    public string? Title { get; init; }        // ai-generated session title
    public string? LastPrompt { get; init; }   // user's most recent prompt
    public string? LastSaid { get; init; }     // most recent assistant text
    public int? ContextTokens { get; init; }   // most recent turn's context size
    public string? Model { get; init; }        // model of that turn
}

/// <summary>
/// Reads transcript detail on demand from
/// <c>&lt;home&gt;/.claude/projects/&lt;encoded-cwd&gt;/&lt;sessionId&gt;.jsonl</c>.
/// Port of macOS <c>Transcript.swift</c>, including its tail-read performance fix.
///
/// Only the last <see cref="TailWindow"/> bytes are read. Reading the whole file and
/// splitting every line dominated the cost (~1.1 s on a 70 MB transcript) and
/// defeated the backwards early-exit scan by materializing everything first.
///
/// The window is deliberately NOT load-bearing: a single turn can append more than
/// the window, so any field the tail misses is <b>carried over</b> from the previous
/// parse — in an append-only file the newest entry is always at the end. A file that
/// was <b>rewritten</b> rather than appended to (Claude Code does this: <c>--rewind</c>,
/// and the 2.1.208 transcript prune) invalidates carried values, detected by a length
/// shrink or a changed fingerprint of the bytes ending where we last read.
/// </summary>
public sealed class TranscriptReader
{
    private const string Allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>How much of the tail to read. Larger than any realistic single turn.</summary>
    private const int TailWindow = 1 << 20;   // 1 MiB

    /// <summary>Bytes fingerprinted to tell "appended to" from "rewritten".</summary>
    private const int FingerprintLength = 512;

    /// <summary>Decode leniently: a multibyte sequence can straddle the window edge.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false,
                                                    throwOnInvalidBytes: false);

    private sealed class Entry
    {
        public required string SessionId { get; init; }
        public required long Length { get; init; }
        public required DateTime Mtime { get; init; }
        public required ulong Fingerprint { get; init; }
        public required bool FullyRead { get; init; }
        public required SessionDetail Detail { get; init; }
    }

    // Keyed by resolved PATH, not sessionId: the path is cwd + sessionId, so two live
    // sessions sharing an id across different cwds would otherwise serve each other's
    // detail. Bounded by Prune().
    //
    // Guarded by _gate: unlike the macOS app, this port enriches on a background
    // thread (App.Refresh runs off the UI thread), and two refreshes can overlap when
    // a watcher burst lands on top of a poll — concurrent Dictionary mutation can
    // corrupt it or spin forever.
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SessionDetail Detail(Session session, string homeDir) =>
        Detail(session.SessionId, session.Cwd, homeDir);

    /// <summary>Overload keyed by id/cwd so the app can enrich an <see cref="AgentSession"/> directly.</summary>
    public SessionDetail Detail(string sessionId, string cwd, string homeDir)
    {
        var path = TranscriptPath(sessionId, cwd, homeDir);
        if (path is null) return new SessionDetail();

        Entry? cached;
        lock (_gate) _cache.TryGetValue(path, out cached);

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete);
            var length = fs.Length;
            var mtime = File.GetLastWriteTimeUtc(path);

            // Untouched since last time — nothing to re-read.
            if (cached is not null && cached.Length == length && cached.Mtime == mtime)
                return cached.Detail;

            // Was this an append, or was the file rewritten underneath us? Only an
            // append lets us trust previously carried values.
            var appended = cached is not null
                        && length >= cached.Length
                        && Fingerprint(fs, cached.Length) == cached.Fingerprint;

            var (text, fromStart) = ReadTail(fs, length);
            var detail = appended ? Merge(Parse(text), cached!.Detail) : Parse(text);

            // Whether the whole file's contents are accounted for: either this read
            // covered it, or an earlier read did and everything since was appended.
            var fullyRead = (fromStart && length > 0) || (appended && cached!.FullyRead);

            // Gaps remain and we've never actually seen the whole file — read it once
            // to establish a baseline. Bounded to at most once per session per rewrite,
            // because FullyRead is then cached and carried across appends.
            if (!fullyRead && !Complete(detail) && length > 0)
            {
                var whole = ReadAll(fs);
                if (whole is not null)
                {
                    var full = Parse(whole);
                    detail = appended ? Merge(full, cached!.Detail) : full;
                    fullyRead = true;
                }
            }

            // Publish a fresh entry rather than mutating the cached one: a concurrent
            // reader holding the old reference must keep seeing a consistent snapshot.
            var entry = new Entry
            {
                SessionId = sessionId,
                Length = length,
                Mtime = mtime,
                Fingerprint = Fingerprint(fs, length),
                FullyRead = fullyRead,
                Detail = detail,
            };
            lock (_gate) _cache[path] = entry;

            return detail;
        }
        catch (IOException) { return cached?.Detail ?? new SessionDetail(); }
        catch (UnauthorizedAccessException) { return cached?.Detail ?? new SessionDetail(); }
    }

    /// <summary>
    /// Drop cache entries for sessions that are no longer live. Without this the
    /// cache grows for the lifetime of the app (one entry per transcript ever seen).
    /// </summary>
    public void Prune(IEnumerable<string> liveSessionIds)
    {
        var live = liveSessionIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var key in _cache.Where(kv => !live.Contains(kv.Value.SessionId))
                                      .Select(kv => kv.Key).ToList())
                _cache.Remove(key);
        }
    }

    /// <summary>Entries currently cached (for tests/diagnostics).</summary>
    public int CacheCount { get { lock (_gate) return _cache.Count; } }

    /// <summary>
    /// Read the last <see cref="TailWindow"/> bytes. Returns the decoded text and
    /// whether it covers the file from byte 0. When starting mid-file we begin one
    /// byte early and drop through the first newline, which discards the partial
    /// leading line — and keeps a multibyte character split across the boundary from
    /// corrupting a line we actually parse.
    /// </summary>
    private static (string Text, bool FromStart) ReadTail(FileStream fs, long length)
    {
        if (length <= 0) return ("", true);

        var start = Math.Max(0, length - TailWindow);
        var fromStart = start == 0;
        if (!fromStart) start -= 1;      // include the preceding byte to spot a clean line break

        var buffer = new byte[length - start];
        fs.Position = start;
        var read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var text = Utf8.GetString(buffer, 0, read);

        if (fromStart) return (text, true);

        var nl = text.IndexOf('\n');
        return (nl < 0 ? "" : text[(nl + 1)..], false);
    }

    private static string? ReadAll(FileStream fs)
    {
        if (fs.Length > int.MaxValue) return null;
        var buffer = new byte[fs.Length];
        fs.Position = 0;
        var read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Utf8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// FNV-1a over the <see cref="FingerprintLength"/> bytes ending at
    /// <paramref name="endOffset"/>. A pure append leaves those bytes untouched; a
    /// rewrite that kept or grew the length almost certainly changes them.
    /// </summary>
    private static ulong Fingerprint(FileStream fs, long endOffset)
    {
        if (endOffset <= 0 || endOffset > fs.Length) return 0;

        var start = Math.Max(0, endOffset - FingerprintLength);
        var buffer = new byte[endOffset - start];
        fs.Position = start;
        var read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

        var hash = 14695981039346656037UL;
        for (var i = 0; i < read; i++)
        {
            hash ^= buffer[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>All four mined fields present — the scan's stopping condition.</summary>
    private static bool Complete(SessionDetail d) =>
        d.Title is not null && d.LastPrompt is not null && d.LastSaid is not null &&
        d.ContextTokens is not null;

    /// <summary>
    /// Prefer freshly parsed values, falling back to what the previous parse found.
    /// Tokens and model move together — the model belongs to the turn that reported
    /// the usage, so mixing a fresh model with carried tokens would mislabel it.
    /// </summary>
    private static SessionDetail Merge(SessionDetail fresh, SessionDetail carried) => new()
    {
        Title = fresh.Title ?? carried.Title,
        LastPrompt = fresh.LastPrompt ?? carried.LastPrompt,
        LastSaid = fresh.LastSaid ?? carried.LastSaid,
        ContextTokens = fresh.ContextTokens ?? carried.ContextTokens,
        Model = fresh.ContextTokens is not null ? fresh.Model : carried.Model,
    };

    /// <summary>
    /// Claude Code stores transcripts under a folder that is the cwd with every
    /// non-alphanumeric character replaced by "-". Defense-in-depth: the id
    /// becomes a path component, so refuse anything that isn't a plain id.
    /// </summary>
    private static string? TranscriptPath(string sessionId, string cwd, string homeDir)
    {
        if (string.IsNullOrEmpty(sessionId) || !sessionId.All(c => Allowed.Contains(c) || c == '-'))
            return null;

        var encoded = new string(cwd.Select(c => Allowed.Contains(c) ? c : '-').ToArray());
        var path = Path.Combine(homeDir, ".claude", "projects", encoded, sessionId + ".jsonl");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Scan from the end so we hit the most recent entries first, stopping as soon
    /// as all four fields are found. Port of the macOS <c>parse()</c>.
    /// </summary>
    private static SessionDetail Parse(string content)
    {
        string? title = null, lastPrompt = null, lastSaid = null, model = null;
        int? contextTokens = null;

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (title is not null && lastPrompt is not null && lastSaid is not null && contextTokens is not null)
                break;

            var line = lines[i];
            if (!TryParse(line, out var obj)) continue;

            if (title is null && line.Contains("\"ai-title\"") &&
                obj.TryGetProperty("aiTitle", out var t) && t.ValueKind == JsonValueKind.String &&
                t.GetString() is { Length: > 0 } ts)
                title = ts;

            if (lastPrompt is null && line.Contains("\"last-prompt\"") &&
                obj.TryGetProperty("lastPrompt", out var p) && p.ValueKind == JsonValueKind.String &&
                p.GetString() is { Length: > 0 } ps)
                lastPrompt = ps;

            if (line.Contains("\"role\":\"assistant\"") &&
                obj.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("role", out var role) && role.GetString() == "assistant")
            {
                lastSaid ??= AssistantText(msg);
                if (contextTokens is null && msg.TryGetProperty("usage", out var usage) &&
                    usage.ValueKind == JsonValueKind.Object)
                {
                    var tokens = Int(usage, "input_tokens")
                               + Int(usage, "cache_read_input_tokens")
                               + Int(usage, "cache_creation_input_tokens");
                    if (tokens > 0)
                    {
                        contextTokens = tokens;
                        model = msg.TryGetProperty("model", out var m) ? m.GetString() : null;
                    }
                }
            }
        }

        return new SessionDetail
        {
            Title = title,
            LastPrompt = lastPrompt,
            LastSaid = lastSaid,
            ContextTokens = contextTokens,
            Model = model,
        };
    }

    /// <summary>Concatenated text blocks of an assistant message, if any.</summary>
    private static string? AssistantText(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        var parts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object &&
                block.TryGetProperty("type", out var ty) && ty.GetString() == "text" &&
                block.TryGetProperty("text", out var tx) && tx.GetString() is { } s)
                parts.Add(s);
        }
        var text = string.Join(" ", parts).Trim();
        return text.Length == 0 ? null : text;
    }

    private static int Int(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static bool TryParse(string line, out JsonElement obj)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            obj = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            obj = default;
            return false;
        }
    }
}

public static class TextUtil
{
    /// <summary>One-line, length-capped version of a possibly multi-line string.</summary>
    public static string OneLine(string text, int max)
    {
        var flat = text.Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : string.Concat(flat.AsSpan(0, max - 1), "…");
    }
}
