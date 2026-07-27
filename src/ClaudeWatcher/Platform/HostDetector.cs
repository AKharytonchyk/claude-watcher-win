namespace ClaudeWatcher.Platform;

/// <summary>
/// Which app is hosting an agent — "Terminal", "VS Code", … — found by walking the
/// process tree up from the session pid to the first recognizable terminal or editor.
/// The Windows answer to the macOS <c>HostDetector</c>.
///
/// Both a Windows Terminal session and a VS Code session write to the same native
/// sessions directory, so the root alone can't tell them apart; without this every
/// native row looked identical.
///
/// Only meaningful for native sessions. A WSL agent's pid belongs to the distro's
/// namespace and matches no Windows process, so callers must not ask.
/// </summary>
public static class HostDetector
{
    /// <summary>
    /// Executable → label, checked against each ancestor in order. Terminals and
    /// editors win over shells, which is why the shell entries sit at the bottom: a
    /// pwsh running inside Windows Terminal should read "Terminal", and only a shell
    /// with no recognizable host above it falls back to naming itself.
    /// </summary>
    private static readonly (string Exe, string Label)[] Hosts =
    [
        ("windowsterminal.exe", "Terminal"),
        ("wt.exe",              "Terminal"),
        ("code.exe",            "VS Code"),
        ("code - insiders.exe", "VS Code Insiders"),
        ("codium.exe",          "VSCodium"),
        ("cursor.exe",          "Cursor"),
        ("devenv.exe",          "Visual Studio"),
        ("rider64.exe",         "Rider"),
        ("idea64.exe",          "IntelliJ"),
        ("alacritty.exe",       "Alacritty"),
        ("wezterm-gui.exe",     "WezTerm"),
        ("hyper.exe",           "Hyper"),
        ("conemu64.exe",        "ConEmu"),
        ("mintty.exe",          "mintty"),
        ("conhost.exe",         "Console"),
        ("openconsole.exe",     "Console"),
        // Shells last — only reached when nothing above hosts them.
        ("pwsh.exe",            "PowerShell"),
        ("powershell.exe",      "PowerShell"),
        ("cmd.exe",             "Command Prompt"),
        ("bash.exe",            "Bash"),
        ("nu.exe",              "Nushell"),
    ];

    // pid → label. Pruned to the live set, because an unbounded cache would both grow
    // for the app's lifetime and hand a recycled pid the previous process's host —
    // which would then mislabel the row and focus the wrong app on click.
    private static readonly Dictionary<int, string?> Cache = new();
    private static readonly object Gate = new();

    /// <summary>The hosting app's label, or null if nothing recognizable was found.</summary>
    public static string? For(int pid)
    {
        if (pid <= 0) return null;

        lock (Gate)
            if (Cache.TryGetValue(pid, out var hit)) return hit;

        var host = Detect(pid);

        lock (Gate) Cache[pid] = host;
        return host;
    }

    /// <summary>Forget pids that are no longer running.</summary>
    public static void Prune(IEnumerable<int> livePids)
    {
        var live = livePids.ToHashSet();
        lock (Gate)
            foreach (var pid in Cache.Keys.Where(p => !live.Contains(p)).ToList())
                Cache.Remove(pid);
    }

    private static string? Detect(int pid)
    {
        var ancestry = ProcessTree.Ancestry(pid);

        // Walk outward from the agent, and for each ancestor prefer the strongest
        // match; the table order decides "terminal beats shell".
        var best = -1;
        string? label = null;
        foreach (var node in ancestry)
        {
            var exe = node.Exe;
            if (string.IsNullOrEmpty(exe)) continue;

            for (var i = 0; i < Hosts.Length; i++)
            {
                if (!exe.Equals(Hosts[i].Exe, StringComparison.OrdinalIgnoreCase)) continue;
                if (best < 0 || i < best) { best = i; label = Hosts[i].Label; }
                break;
            }
        }
        return label;
    }
}
