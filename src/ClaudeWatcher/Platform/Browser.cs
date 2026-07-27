using System.Diagnostics;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Opens a URL in the user's default browser. Uses ShellExecute rather than the WinRT
/// launcher, which is the dependable path for an unpackaged app.
/// </summary>
public static class Browser
{
    /// <summary>
    /// Open <paramref name="url"/>, returning whether it was handed off. Only http(s)
    /// is allowed: the URL arrives from `gh` output, and ShellExecute would happily
    /// run other schemes.
    /// </summary>
    public static bool Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        try
        {
            using var p = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;   // no handler registered, or the shell refused
        }
    }
}
