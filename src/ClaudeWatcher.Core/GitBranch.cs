namespace ClaudeWatcher.Core;

/// <summary>
/// Best-effort current branch by reading <c>.git/HEAD</c>. Returns a short SHA for
/// a detached HEAD, or null when there's no plain git dir (e.g. worktrees).
/// Port of the macOS <c>ClaudeWatcher_gitBranch</c>. Pure file IO — the caller
/// passes a Windows-accessible repo path (see <c>IWatchRoot.ResolvePath</c>).
/// </summary>
public static class GitBranch
{
    public static string? Read(string repoDir)
    {
        var head = Path.Combine(repoDir, ".git", "HEAD");
        string line;
        try { line = File.ReadAllText(head).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        const string prefix = "ref: refs/heads/";
        if (line.StartsWith(prefix, StringComparison.Ordinal)) return line[prefix.Length..];
        return line.Length >= 7 ? line[..7] : null;
    }
}
