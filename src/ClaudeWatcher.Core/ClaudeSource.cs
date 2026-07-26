using System.Text.Json;
using ClaudeWatcher.Core.Roots;

namespace ClaudeWatcher.Core;

/// <summary>Branding for Claude Code.</summary>
public static class ClaudeBrand
{
    // TODO(ui): pick the real provider glyph (Segoe Fluent Icons codepoint).
    public static readonly AgentBrand Instance = new("claude", "Claude Code", "");
}

/// <summary>
/// The Claude Code agent source: reads <c>&lt;root&gt;/*.json</c> across every watch
/// root, decodes the <see cref="Session"/> schema, drops dead processes, and
/// normalizes the survivors to <see cref="AgentSession"/> (state, waiting reason,
/// origin tag). Pure logic — roots are injected, so this is fully unit-testable
/// with a fake root. Port of the macOS <c>SessionStore</c> + <c>ClaudeAdapter</c>.
///
/// Transcript enrichment (last intent/said, tokens, model) is layered on later by
/// the caller via <see cref="TranscriptReader"/>; this class only handles
/// liveness + state.
/// </summary>
public sealed class ClaudeSource(IReadOnlyList<IWatchRoot> roots) : IAgentSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AgentBrand Brand => ClaudeBrand.Instance;

    public bool IsAvailable => roots.Any(r => SafeDirExists(r.SessionsDir));

    public IReadOnlyList<AgentSession> LiveSessions()
    {
        var sessions = new List<(AgentSession Agent, double SortKey)>();
        foreach (var root in roots)
            foreach (var session in ReadRoot(root))
                sessions.Add((Normalize(session, root), session.StartedAt ?? 0));

        // Oldest first, matching the macOS ordering.
        return sessions.OrderBy(s => s.SortKey).Select(s => s.Agent).ToList();
    }

    /// <summary>Live sessions from one root. A stopped/absent dir yields nothing.</summary>
    private static IEnumerable<Session> ReadRoot(IWatchRoot root)
    {
        if (!SafeDirExists(root.SessionsDir)) yield break;

        string[] files;
        try { files = Directory.GetFiles(root.SessionsDir, "*.json"); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (var file in files)
        {
            Session? session = null;
            try
            {
                var json = File.ReadAllText(file);
                session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
            }
            catch (IOException) { }              // mid-write / vanished — skip
            catch (JsonException) { }            // malformed — skip

            // A pid of 0 or below is never a real process, and passing one to a
            // signal-based liveness check is actively dangerous: `kill -0 0` targets
            // the caller's process group and `kill -0 -1` every permitted process, so
            // a malformed session file would report a phantom agent alive forever.
            if (session is { SessionId.Length: > 0, Pid: > 0 } s &&
                root.IsAlive(s.Pid, s.StartedDate()))
                yield return s;
        }
    }

    private static AgentSession Normalize(Session s, IWatchRoot root)
    {
        var state = StatusClassifier.Classify(s);
        return new AgentSession
        {
            ProviderId = ClaudeBrand.Instance.Id,
            ProviderName = ClaudeBrand.Instance.DisplayName,
            ProviderGlyph = ClaudeBrand.Instance.Glyph,
            Id = s.SessionId,
            Name = s.DisplayName(),
            Pid = s.Pid,
            Cwd = s.Cwd,
            State = state,
            WaitingReason = state == AgentState.Waiting ? StatusClassifier.WaitingReason(s.WaitingFor) : null,
            StateSince = s.StatusDate(),
            StartedAt = s.StartedDate(),
            RootId = root.Id,
            Origin = root.Origin,
        };
    }

    private static bool SafeDirExists(string path)
    {
        try { return Directory.Exists(path); }
        catch (IOException) { return false; }             // e.g. stopped WSL distro
        catch (UnauthorizedAccessException) { return false; }
    }
}
