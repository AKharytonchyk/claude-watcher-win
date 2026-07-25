namespace ClaudeWatcher.Core;

/// <summary>
/// State of a single agent (and, aggregated, the fleet).
/// Ordered by urgency — <see cref="Waiting"/> (blocked on you) first.
/// </summary>
public enum AgentState
{
    Waiting = 0, // red    — blocked on the user (permission prompt, question, …)
    Working = 1, // yellow — actively busy
    Idle    = 2, // green  — done / waiting quietly
}

/// <summary>
/// Semantic color token for a state. Core stays UI-agnostic — the WinUI layer
/// maps these to theme <c>Brush</c>es / accent-aware resources.
/// </summary>
public enum StateColor { Red, Yellow, Green }

public static class AgentStateExtensions
{
    public static StateColor Color(this AgentState s) => s switch
    {
        AgentState.Waiting => StateColor.Red,
        AgentState.Working => StateColor.Yellow,
        _                  => StateColor.Green,
    };

    public static string Emoji(this AgentState s) => s switch
    {
        AgentState.Waiting => "🔴",
        AgentState.Working => "🟡",
        _                  => "🟢",
    };

    /// <summary>Short label for the human-readable summary line.</summary>
    public static string SummaryWord(this AgentState s) => s switch
    {
        AgentState.Waiting => "needs you",
        AgentState.Working => "working",
        _                  => "idle",
    };
}
