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
/// UNVERIFIED (Windows-only).
/// </summary>
public static partial class TerminalFocus
{
    /// <summary>Returns true if a hosting window was found and foregrounded.</summary>
    public static bool Focus(int pid, string rootId)
    {
        if (rootId.StartsWith("wsl:", StringComparison.Ordinal)) return false; // no Windows window
        var ancestry = Ancestry(pid);
        foreach (var candidate in ancestry)
        {
            var hwnd = MainWindowFor(candidate);
            if (hwnd != IntPtr.Zero) return Foreground(hwnd);
        }
        return false;
    }

    /// <summary>The pid and its ancestors (self first), via the process table.</summary>
    private static List<int> Ancestry(int pid)
    {
        var parent = new Dictionary<int, int>();
        try
        {
            using var snap = new Snapshot();
            foreach (var (child, par) in snap.Pairs()) parent[child] = par;
        }
        catch { /* best effort */ }

        var chain = new List<int>();
        var cur = pid;
        for (var hops = 0; hops < 64; hops++)
        {
            chain.Add(cur);
            if (!parent.TryGetValue(cur, out var par) || par <= 0 || par == cur) break;
            cur = par;
        }
        return chain;
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
        catch { }

        // Fallback: first visible top-level window owned by this pid.
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var wpid);
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

    // MARK: - Toolhelp snapshot

    private sealed class Snapshot : IDisposable
    {
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private readonly IntPtr _h;
        public Snapshot() => _h = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        public IEnumerable<(int Pid, int Parent)> Pairs()
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(_h, ref e)) yield break;
            do { yield return ((int)e.th32ProcessID, (int)e.th32ParentProcessID); }
            while (Process32Next(_h, ref e));
        }

        public void Dispose() { if (_h != IntPtr.Zero && _h != new IntPtr(-1)) CloseHandle(_h); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr h);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int cmd);
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
}
