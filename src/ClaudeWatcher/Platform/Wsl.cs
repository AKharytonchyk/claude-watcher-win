using System.Diagnostics;
using System.Text;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Thin wrapper over <c>wsl.exe</c> for distro discovery and path translation.
///
/// UNVERIFIED (Windows-only): written on macOS, not yet compiled/run on Windows.
/// Gotchas baked in: <c>wsl -l -q</c> emits UTF-16, and a stopped distro must be
/// skipped silently. Verify on a real box.
/// </summary>
public static class Wsl
{
    /// <summary>Installed distro names. Honors the CWATCH_WSL override (comma-separated).</summary>
    public static IReadOnlyList<string> Distros()
    {
        var overrideList = Environment.GetEnvironmentVariable("CWATCH_WSL");
        if (!string.IsNullOrWhiteSpace(overrideList))
            return overrideList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // `wsl -l -q` lists installed distros, one per line — in UTF-16LE.
        var raw = Run("wsl.exe", new[] { "-l", "-q" }, Encoding.Unicode);
        if (raw is null) return Array.Empty<string>();
        return raw.Replace("\0", "")
                  .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();
    }

    /// <summary>The distro user's <c>$HOME</c> (POSIX), or null if the distro won't start.</summary>
    public static string? Home(string distro)
    {
        var home = Run("wsl.exe", new[] { "-d", distro, "--", "printf", "%s", "$HOME" }, Encoding.UTF8);
        // `$HOME` isn't expanded by printf's format arg; run through the shell instead.
        if (string.IsNullOrWhiteSpace(home) || !home.StartsWith('/'))
            home = Run("wsl.exe", new[] { "-d", distro, "--", "sh", "-c", "printf %s \"$HOME\"" }, Encoding.UTF8);
        home = home?.Replace("\0", "").Trim();
        return string.IsNullOrEmpty(home) ? null : home;
    }

    /// <summary>UNC prefix for a distro's filesystem, e.g. <c>\\wsl$\Ubuntu</c>.</summary>
    public static string UncRoot(string distro) => $@"\\wsl$\{distro}";

    /// <summary>Rebase a POSIX path under the distro's UNC root.</summary>
    public static string ToWindowsPath(string distro, string posixPath) =>
        UncRoot(distro) + posixPath.Replace('/', '\\');

    private static string? Run(string exe, string[] args, Encoding stdoutEncoding)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = stdoutEncoding,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return null;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            return p.HasExited && p.ExitCode == 0 ? outp : null;
        }
        catch
        {
            return null;
        }
    }
}
