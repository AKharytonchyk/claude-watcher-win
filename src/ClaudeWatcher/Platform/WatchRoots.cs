using ClaudeWatcher.Core.Roots;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Discovers the roots to watch: the native Windows home, plus each installed
/// WSL distro. The crux of the port (see specs/0001).
///
/// UNVERIFIED (Windows-only): the native root is straightforward; the WSL path
/// depends on <see cref="Wsl"/> and needs a real box to confirm.
/// </summary>
public static class WatchRoots
{
    public static IReadOnlyList<IWatchRoot> Discover()
    {
        var roots = new List<IWatchRoot> { new NativeRoot() };
        foreach (var distro in Wsl.Distros())
        {
            var home = Wsl.Home(distro);          // null ⇒ distro stopped/unavailable → skip
            if (home is not null)
                roots.Add(new WslRoot(distro, home));
        }
        return roots;
    }
}

/// <summary>Claude Code running natively (PowerShell/cmd). Windows-PID liveness.</summary>
public sealed class NativeRoot : IWatchRoot
{
    public string Id => "native";
    // "Windows", not "PowerShell": this is *where the agent is rooted*, and a native
    // agent may well be under cmd, pwsh, or VS Code's own shell. The hosting app is
    // reported separately by HostDetector.
    public string Origin => "Windows";
    public bool IsWsl => false;
    public string? Distro => null;

    public string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string SessionsDir => Path.Combine(HomeDir, ".claude", "sessions");

    // Native cwd is already a Windows path.
    public string ResolvePath(string sessionCwd) => sessionCwd;

    public bool IsAlive(int pid, DateTimeOffset? startedAt) =>
        ProcessLiveness.IsWindowsPidAlive(pid, startedAt);
}

/// <summary>Claude Code inside a WSL distro. Linux-PID liveness via wsl.exe.</summary>
public sealed class WslRoot(string distro, string posixHome) : IWatchRoot
{
    public string Id => $"wsl:{distro}";
    public string Origin => distro;
    public bool IsWsl => true;
    public string? Distro => distro;

    public string HomeDir => Wsl.ToWindowsPath(distro, posixHome);
    public string SessionsDir => Path.Combine(HomeDir, ".claude", "sessions");

    // The session's cwd is a POSIX path inside the distro.
    public string ResolvePath(string sessionCwd) => Wsl.ToWindowsPath(distro, sessionCwd);

    // startedAt is unused: a WSL pid's start time isn't comparable to a Windows clock.
    public bool IsAlive(int pid, DateTimeOffset? startedAt) =>
        ProcessLiveness.IsWslPidAlive(distro, pid);
}
