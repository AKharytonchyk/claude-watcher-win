using System.Runtime.InteropServices;

namespace ClaudeWatcher.Platform;

/// <summary>
/// One snapshot of the Windows process table, walked upward from a pid. Shared by
/// <see cref="TerminalFocus"/> (which wants the ancestor owning a window) and
/// <see cref="HostDetector"/> (which wants the ancestor's executable name).
///
/// Uses classic <c>[DllImport]</c> (not <c>[LibraryImport]</c>): the source generator
/// rejects <c>PROCESSENTRY32</c>'s inline string field (SYSLIB1051) and would require
/// unsafe code.
/// </summary>
internal static class ProcessTree
{
    /// <summary>An entry in a process's ancestry.</summary>
    internal readonly record struct Node(int Pid, string Exe);

    private const int MaxHops = 64;

    /// <summary>
    /// The pid and its ancestors, self first, with executable names. Empty when the
    /// snapshot fails. Only Windows pids: a WSL session's pid lives in the distro's
    /// namespace and is meaningless here.
    /// </summary>
    internal static List<Node> Ancestry(int pid)
    {
        var chain = new List<Node>();
        if (pid <= 0) return chain;

        var parent = new Dictionary<int, (int Parent, string Exe)>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return chain;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snap, ref e))
                do { parent[(int)e.th32ProcessID] = ((int)e.th32ParentProcessID, e.szExeFile); }
                while (Process32Next(snap, ref e));
        }
        finally { CloseHandle(snap); }

        var cur = pid;
        for (var hops = 0; hops < MaxHops; hops++)
        {
            if (!parent.TryGetValue(cur, out var entry)) { chain.Add(new Node(cur, "")); break; }
            chain.Add(new Node(cur, entry.Exe));
            if (entry.Parent <= 0 || entry.Parent == cur) break;
            cur = entry.Parent;
        }
        return chain;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
}
