namespace ClaudeWatcher.Platform;

/// <summary>Open pull request for a repo, if any.</summary>
public sealed record PrInfo(int Number, string Url, bool IsDraft);

/// <summary>
/// The ONLY outbound network path: an optional open-PR lookup via the `gh` CLI,
/// cached. Must stay disable-able — honor CWATCH_OFFLINE (Constitution §1).
///
/// STUB: TODO — run `gh pr status`/`gh pr list` from the session cwd (native), or
/// `wsl.exe -d <distro> -- gh …` for WSL repos. Cache per repo; never block the UI.
/// </summary>
public sealed class PrChecker
{
    public static bool Offline =>
        Environment.GetEnvironmentVariable("CWATCH_OFFLINE") is { Length: > 0 };

    public PrInfo? Lookup(string cwd, string rootId)
    {
        if (Offline) return null;
        // TODO(phase 3): implement + cache.
        return null;
    }
}
