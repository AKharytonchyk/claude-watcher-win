namespace ClaudeWatcher.Core;

/// <summary>
/// Display-ready projection of one agent — everything the row needs, already
/// formatted, so the WinUI layer only binds (no logic in XAML). Built by
/// <see cref="FleetBuilder"/>. Analogue of the macOS <c>AgentVM</c>, minus the
/// AppKit/SwiftUI bits.
/// </summary>
public sealed record AgentView
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AgentState State { get; init; }
    public required string StateText { get; init; }   // "working" / waiting reason
    public required string TimeText { get; init; }    // elapsed, e.g. "4m"
    public required int Pid { get; init; }
    public required string RootId { get; init; }
    public required string Origin { get; init; }      // where it's rooted: "Windows" / "Ubuntu"
    public required string ShortCwd { get; init; }

    /// <summary>Hosting app, e.g. "Terminal" / "VS Code". Null when undetectable.</summary>
    public string? Host { get; init; }

    /// <summary>
    /// The row's provenance line, pre-composed so XAML stays logic-free: the host app
    /// and — only when it adds information — where the agent is rooted.
    /// </summary>
    public required string OriginText { get; init; }

    public string? Branch { get; init; }

    /// <summary>
    /// Where the agent sits, in one field: the git branch when it's in a repo, else
    /// the collapsed path. Showing both crowds the row and truncates each to
    /// uselessness, and the agent's name already carries the project.
    /// </summary>
    public string Location => Branch is { Length: > 0 } b ? b : ShortCwd;
    public string? Intent { get; init; }              // trimmed last prompt / title
    public string? ModelLabel { get; init; }          // "Opus 4.8"
    public int? ContextTokens { get; init; }
    public int? ContextWindow { get; init; }
    public double? ContextPct { get; init; }          // 0..1
}
