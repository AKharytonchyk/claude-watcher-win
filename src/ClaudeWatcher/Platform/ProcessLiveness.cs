using System.Diagnostics;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Liveness checks. Native = Windows PID; WSL = Linux PID inside the distro
/// namespace (Win32 CANNOT see it — see AGENTS.md gotchas).
///
/// Both paths guard against a <b>recycled pid</b>: a session file left by a crashed
/// agent names a pid the OS may have handed to something else, and a bare liveness
/// probe would happily report that stranger as your agent.
/// </summary>
public static class ProcessLiveness
{
    /// <summary>
    /// How far the OS-reported process start time may differ from the session's
    /// declared <c>startedAt</c> before we call it a different process. Generous:
    /// the two are recorded by different clocks at slightly different moments
    /// (observed skew on real sessions is seconds), so this only ever catches a pid
    /// belonging to a process from a clearly different era.
    /// </summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(120);

    public static bool IsWindowsPidAlive(int pid, DateTimeOffset? startedAt = null)
    {
        if (pid <= 0) return false;

        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;

            // Fail open: no declared start time, or a process whose start time we
            // can't read (access denied on a foreign owner), stays "alive".
            if (startedAt is null) return true;

            DateTime osStart;
            try { osStart = p.StartTime; }
            catch (Exception) { return true; }

            var skew = (new DateTimeOffset(osStart) - startedAt.Value).Duration();
            return skew <= StartTimeTolerance;
        }
        catch (ArgumentException) { return false; }  // no such process
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// WSL liveness. The pid lives in the distro's namespace, so this shells out to
    /// the distro. No start-time cross-check is possible: the session's
    /// <c>procStart</c> for a WSL agent is Linux jiffies in that namespace, which
    /// isn't comparable to any Windows clock — so WSL relies on the pid guard alone.
    /// </summary>
    public static bool IsWslPidAlive(string distro, int pid)
    {
        // Critical: a non-positive pid must never reach `kill`. `kill -0 -1` signals
        // every process the caller may signal.
        if (pid <= 0) return false;

        // TODO(phase 2): batch these per distro; fall back to session-file mtime
        // staleness when wsl.exe is unavailable or the distro is stopped.
        try
        {
            var psi = new ProcessStartInfo("wsl.exe")
            {
                ArgumentList = { "-d", distro, "--", "kill", "-0", pid.ToString() },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(2000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
