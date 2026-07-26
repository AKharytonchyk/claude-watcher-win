using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeWatcher.Platform;

/// <summary>
/// Brings the terminal hosting a session to the front. Window-level only — there
/// is no public API to select a specific Windows Terminal tab/pane by tty. Walks
/// the process tree from the session pid up to the first ancestor that owns a
/// visible top-level window, then foregrounds it (with the AttachThreadInput
/// dance Windows requires). WSL sessions have a Linux pid that maps to no Windows
/// window, so they no-op.
///
/// Uses classic <c>[DllImport]</c> (not <c>[LibraryImport]</c>): the source
/// generator rejects <c>PROCESSENTRY32</c>'s inline string field (SYSLIB1051)
/// and would require unsafe code.
///
/// UNVERIFIED (Windows-only).
/// </summary>
public static class TerminalFocus
{
    /// <summary>Returns true if a hosting window was found and foregrounded.</summary>
    public static bool Focus(int pid, string rootId)
    {
        if (rootId.StartsWith("wsl:", StringComparison.Ordinal)) return false; // no Windows window
        foreach (var node in ProcessTree.Ancestry(pid))
        {
            var hwnd = MainWindowFor(node.Pid);
            if (hwnd != IntPtr.Zero) return Foreground(hwnd);
        }
        return false;
    }

    private static IntPtr MainWindowFor(int pid)
    {
        // Fast path via the BCL.
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle))
                return p.MainWindowHandle;
        }
        catch { /* process gone / access denied */ }

        // Fallback: first visible top-level window owned by this pid.
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var wpid);
            if (wpid == (uint)pid && IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false; // stop
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool Foreground(IntPtr hwnd)
    {
        const int SW_RESTORE = 9;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        var fg = GetForegroundWindow();
        var target = GetWindowThreadProcessId(hwnd, out _);
        var current = GetWindowThreadProcessId(fg, out _);
        if (target != current) AttachThreadInput(current, target, true);
        var ok = SetForegroundWindow(hwnd);
        if (target != current) AttachThreadInput(current, target, false);
        return ok;
    }

    // MARK: - Interop

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
}
