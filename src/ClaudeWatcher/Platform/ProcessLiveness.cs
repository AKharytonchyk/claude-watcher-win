using System.Diagnostics;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Liveness checks. Native = Windows PID; WSL = Linux PID inside the distro
/// namespace (Win32 CANNOT see it — see AGENTS.md gotchas).
///
/// STUB: correct in shape; verify on Windows. The WSL path spawns a process per
/// call — batch per distro before shipping (TODO).
/// </summary>
public static class ProcessLiveness
{
    public static bool IsWindowsPidAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }  // no such process
        catch (InvalidOperationException) { return false; }
    }

    public static bool IsWslPidAlive(string distro, int pid)
    {
        // TODO(phase 2): batch these; fall back to session-file mtime staleness
        // when wsl.exe is unavailable or the distro is stopped.
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
