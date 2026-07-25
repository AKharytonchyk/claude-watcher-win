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
    public required string Origin { get; init; }      // "PowerShell" / "Ubuntu"
    public required string ShortCwd { get; init; }

    public string? Branch { get; init; }
    public string? Intent { get; init; }              // trimmed last prompt / title
    public string? ModelLabel { get; init; }          // "Opus 4.8"
    public int? ContextTokens { get; init; }
    public int? ContextWindow { get; init; }
    public double? ContextPct { get; init; }          // 0..1
}
