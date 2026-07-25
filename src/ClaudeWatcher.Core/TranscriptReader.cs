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
/// <c>&lt;home&gt;/.claude/projects/&lt;encoded-cwd&gt;/&lt;sessionId&gt;.jsonl</c>, cached by
/// file modification time. Port of macOS <c>Transcript.swift</c>.
///
/// </summary>
public sealed class TranscriptReader
{
    private const string Allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private readonly Dictionary<string, (DateTime Mtime, SessionDetail Detail)> _cache = new();

    public SessionDetail Detail(Session session, string homeDir) =>
        Detail(session.SessionId, session.Cwd, homeDir);

    /// <summary>Overload keyed by id/cwd so the app can enrich an <see cref="AgentSession"/> directly.</summary>
    public SessionDetail Detail(string sessionId, string cwd, string homeDir)
    {
        var path = TranscriptPath(sessionId, cwd, homeDir);
        if (path is null) return new SessionDetail();

        var mtime = File.GetLastWriteTimeUtc(path);
        if (_cache.TryGetValue(sessionId, out var hit) && hit.Mtime == mtime)
            return hit.Detail;

        var detail = Parse(path);
        _cache[sessionId] = (mtime, detail);
        return detail;
    }

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
    private static SessionDetail Parse(string path)
    {
        string content;
        try { content = File.ReadAllText(path); }
        catch (IOException) { return new SessionDetail(); }

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
