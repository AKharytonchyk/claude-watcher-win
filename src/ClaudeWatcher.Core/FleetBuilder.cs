namespace ClaudeWatcher.Core;

/// <summary>
/// Enriches normalized <see cref="AgentSession"/>s into display-ready
/// <see cref="AgentView"/>s: state wording, elapsed time, model label, context
/// pressure, trimmed intent, collapsed cwd. Pure — transcript/branch lookups are
/// injected as delegates, so it's fully unit-testable and the WinUI layer stays
/// logic-free. Analogue of the macOS <c>AgentsModel</c> enrichment step.
/// </summary>
public static class FleetBuilder
{
    private const int IntentMaxLength = 80;

    public static AgentView Enrich(AgentSession s, SessionDetail d, string? branch,
                                   string? homePrefix, DateTimeOffset now)
    {
        var model = d.Model ?? s.Model;
        int? tokens = d.ContextTokens ?? s.ContextTokens;
        int? window = tokens is int t ? ContextWindow.For(t, model) : null;
        double? pct = tokens is int tk && window is int w && w > 0 ? (double)tk / w : null;
        var rawIntent = d.LastPrompt ?? d.Title ?? s.LastIntent;

        return new AgentView
        {
            Id = s.Id,
            Name = s.Name,
            State = s.State,
            StateText = s.State == AgentState.Waiting ? s.WaitingReason ?? "needs you" : s.State.SummaryWord(),
            TimeText = Elapsed.Short(s.StateSince ?? s.StartedAt, now),
            Pid = s.Pid,
            RootId = s.RootId,
            Origin = s.Origin,
            ShortCwd = ShortenCwd(s.Cwd, homePrefix),
            Branch = branch,
            Intent = string.IsNullOrWhiteSpace(rawIntent) ? null : TextUtil.OneLine(rawIntent!, IntentMaxLength),
            ModelLabel = ContextWindow.HumanModel(model),
            ContextTokens = tokens,
            ContextWindow = window,
            ContextPct = pct,
        };
    }

    /// <summary>
    /// Build the full fleet snapshot. <paramref name="detail"/>/<paramref name="branch"/>/
    /// <paramref name="homePrefix"/> are per-session lookups the app wires to
    /// disk (TranscriptReader, GitBranch) or fakes provide in tests.
    /// </summary>
    public static (IReadOnlyList<AgentView> Views, StatusCounts Counts) Build(
        IReadOnlyList<AgentSession> sessions,
        Func<AgentSession, SessionDetail> detail,
        Func<AgentSession, string?> branch,
        Func<AgentSession, string?> homePrefix,
        DateTimeOffset now)
    {
        var views = sessions.Select(s => Enrich(s, detail(s), branch(s), homePrefix(s), now)).ToList();
        return (views, StatusCounts.Count(views.Select(v => v.State)));
    }

    /// <summary>Collapse a leading home prefix to "~". No-op when prefix is null.</summary>
    private static string ShortenCwd(string cwd, string? homePrefix)
    {
        if (string.IsNullOrEmpty(homePrefix)) return cwd;
        if (cwd == homePrefix) return "~";
        var sep = cwd.Contains('\\') ? '\\' : '/';
        var withSep = homePrefix.EndsWith(sep) ? homePrefix : homePrefix + sep;
        return cwd.StartsWith(withSep, StringComparison.Ordinal) ? "~" + cwd[homePrefix.Length..] : cwd;
    }
}
