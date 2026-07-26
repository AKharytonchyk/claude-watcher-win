namespace ClaudeWatcher.Core;

/// <summary>Identity/branding for an agent type (mirror of macOS <c>AgentBrand</c>).</summary>
public sealed record AgentBrand(string Id, string DisplayName, string Glyph);

/// <summary>
/// One running agent session, normalized so the model/UI never see an agent's
/// native format. On Windows the <c>Origin</c> distinguishes native vs. WSL
/// distros — the equivalent of the macOS host glyph. Mirror of macOS
/// <c>AgentSession</c>, plus <c>Origin</c>/<c>RootId</c> for the roots model.
/// </summary>
public sealed record AgentSession
{
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required string ProviderGlyph { get; init; }

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Pid { get; init; }
    public required string Cwd { get; init; }

    public required AgentState State { get; init; }
    public string? WaitingReason { get; init; }   // friendly reason, when Waiting
    public DateTimeOffset? StateSince { get; init; }
    public DateTimeOffset? StartedAt { get; init; }

    public string? LastIntent { get; init; }       // raw last user prompt (UI trims)
    public string? LastSaid { get; init; }
    public int? ContextTokens { get; init; }
    public string? Model { get; init; }

    // Windows roots model:
    public string RootId { get; init; } = "native"; // "native" | "wsl:Ubuntu"
    public string Origin { get; init; } = "Windows"; // display label: "Windows" | "Ubuntu"
    public bool IsWsl { get; init; }                // true when rooted in a WSL distro

    public string ProjectName => Path.GetFileName(Cwd.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : Cwd;
}

/// <summary>
/// A source of agent sessions. Each agent type ships one implementation.
/// Mirror of the macOS <c>AgentAdapter</c> protocol.
/// </summary>
public interface IAgentSource
{
    AgentBrand Brand { get; }

    /// <summary>Whether this agent's data is present on the machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Enumerate + normalize the currently-live sessions across all roots.</summary>
    IReadOnlyList<AgentSession> LiveSessions();
}
