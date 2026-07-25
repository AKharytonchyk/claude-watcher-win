namespace ClaudeWatcher.Core.Roots;

/// <summary>
/// A place Claude Code writes sessions. The crux of the Windows port: one Claude
/// source aggregates several roots — the native Windows home and each WSL distro
/// home. This interface is the seam; concrete roots (which spawn <c>wsl.exe</c>
/// or call Win32) live in the app's <c>Platform/</c> layer, never in Core.
///
/// Generalizes the macOS <c>AgentAdapter.watchPaths</c> concept (see the macOS
/// repo's <c>specs/0001-agent-adapters.md</c>).
/// </summary>
public interface IWatchRoot
{
    /// <summary>Stable id, e.g. "native" or "wsl:Ubuntu".</summary>
    string Id { get; }

    /// <summary>Human label for the origin badge, e.g. "PowerShell" or "Ubuntu".</summary>
    string Origin { get; }

    /// <summary>True for a WSL distro root.</summary>
    bool IsWsl { get; }

    /// <summary>WSL distro name when <see cref="IsWsl"/>, else null.</summary>
    string? Distro { get; }

    /// <summary>
    /// The sessions directory to read (native path, or a <c>\\wsl$\…</c> path).
    /// May not exist if the distro is stopped — callers must tolerate that.
    /// </summary>
    string SessionsDir { get; }

    /// <summary>
    /// The home directory that contains <c>.claude</c> — a Windows path natively,
    /// or a <c>\\wsl$\&lt;distro&gt;\home\&lt;user&gt;</c> UNC path for WSL. Used to locate
    /// transcripts (<c>&lt;home&gt;/.claude/projects/…</c>).
    /// </summary>
    string HomeDir { get; }

    /// <summary>
    /// Translate a session's <c>cwd</c> (as written in its session file) to a
    /// Windows-accessible filesystem path. Native: the cwd unchanged. WSL: the
    /// POSIX cwd rebased under <c>\\wsl$\&lt;distro&gt;</c>. Used to read
    /// <c>.git/HEAD</c> for the branch.
    /// </summary>
    string ResolvePath(string sessionCwd);

    /// <summary>
    /// Whether the owning process is still running. Native roots use Win32; WSL
    /// roots must NOT feed the Linux pid to Win32 — they use
    /// <c>wsl.exe -d &lt;distro&gt; -- kill -0 &lt;pid&gt;</c> or mtime staleness.
    /// </summary>
    bool IsAlive(int pid);
}
