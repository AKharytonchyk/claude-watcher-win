namespace ClaudeWatcher.Core;

/// <summary>
/// Classification and human-readable summaries. Direct port of the macOS
/// <c>Status.swift</c> logic (minus the AppKit rendering).
/// </summary>
public static class StatusClassifier
{
    /// <summary>
    /// Classify one session from its reported status. "waiting" is the reliable
    /// "blocked on the user" signal Claude Code writes to the session file.
    /// ("shell" is idle-in-a-shell, so it groups with idle.)
    /// </summary>
    public static AgentState Classify(Session s) => s.Status switch
    {
        "waiting" => AgentState.Waiting,
        "busy"    => AgentState.Working,
        _         => AgentState.Idle,
    };

    /// <summary>
    /// Friendly wording for <c>waitingFor</c>. Interactive *questions* and tool
    /// *approvals* both surface as "permission prompt", so phrase that one
    /// neutrally — it always means "you need to respond".
    /// </summary>
    public static string WaitingReason(string? raw) => raw switch
    {
        "permission prompt" => "awaiting your response",
        "input needed"      => "awaiting your input",
        "dialog open"       => "dialog open",
        "worker request"    => "worker request",
        "sandbox request"   => "sandbox approval",
        { } other           => other,
        null                => "awaiting your input",
    };
}

/// <summary>How many agents are in each state.</summary>
public readonly record struct StatusCounts(int Waiting, int Working, int Idle)
{
    public int Total => Waiting + Working + Idle;

    /// <summary>Dominant urgency for the single tray glyph: red &gt; yellow &gt; green.</summary>
    public AgentState? Dominant =>
        Waiting > 0 ? AgentState.Waiting :
        Working > 0 ? AgentState.Working :
        Idle    > 0 ? AgentState.Idle    : null;

    /// <summary>(state, count) pairs in urgency order, only for non-empty states.</summary>
    public IEnumerable<(AgentState State, int Count)> Present
    {
        get
        {
            if (Waiting > 0) yield return (AgentState.Waiting, Waiting);
            if (Working > 0) yield return (AgentState.Working, Working);
            if (Idle > 0)    yield return (AgentState.Idle, Idle);
        }
    }

    public static StatusCounts Count(IEnumerable<AgentState> states)
    {
        int w = 0, k = 0, i = 0;
        foreach (var s in states)
        {
            switch (s)
            {
                case AgentState.Waiting: w++; break;
                case AgentState.Working: k++; break;
                default:                 i++; break;
            }
        }
        return new StatusCounts(w, k, i);
    }
}

public static class SummaryText
{
    /// <summary>Human one-liner, e.g. "1 needs you · 2 working · 3 idle".</summary>
    public static string For(StatusCounts c)
    {
        if (c.Total == 0) return "No running agents";
        var parts = new List<string>(3);
        if (c.Waiting > 0) parts.Add($"{c.Waiting} needs you");
        if (c.Working > 0) parts.Add($"{c.Working} working");
        if (c.Idle > 0)    parts.Add($"{c.Idle} idle");
        return string.Join(" · ", parts);
    }
}
