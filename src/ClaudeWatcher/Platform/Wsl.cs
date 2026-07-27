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

    /// <summary>
    /// Rebase a POSIX path onto something Windows can open. Paths on a mounted
    /// Windows drive map back to that drive; everything else goes under the UNC root.
    /// </summary>
    public static string ToWindowsPath(string distro, string posixPath) =>
        DrivePath(posixPath) ?? UncRoot(distro) + posixPath.Replace('/', '\\');

    /// <summary>
    /// <c>/mnt/c/foo</c> → <c>C:\foo</c>, or null if this isn't a drive mount.
    /// Agents run from WSL very often sit on a Windows drive, and reaching those
    /// through <c>\\wsl$\Distro\mnt\c\…</c> is a needless round trip through 9P —
    /// slow, and it silently fails often enough that git/branch lookups come back
    /// empty. Assumes the default <c>/mnt</c> automount root (configurable in
    /// wsl.conf; a custom root just falls through to the UNC path).
    /// </summary>
    private static string? DrivePath(string posixPath)
    {
        const string prefix = "/mnt/";
        if (!posixPath.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var rest = posixPath[prefix.Length..];
        if (rest.Length == 0 || !char.IsAsciiLetter(rest[0])) return null;
        // Guard against /mnt/wsl, /mnt/host and friends — a drive is one letter.
        if (rest.Length > 1 && rest[1] != '/') return null;

        var tail = rest.Length > 1 ? rest[1..].Replace('/', '\\') : "\\";
        return $"{char.ToUpperInvariant(rest[0])}:{tail}";
    }

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
