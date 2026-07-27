using ClaudeWatcher.Core;
using ClaudeWatcher.Core.Roots;
using ClaudeWatcher.Platform;
using ClaudeWatcher.UI;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClaudeWatcher;

/// <summary>
/// Tray-only app: no main window. On launch we discover roots, wire the watcher
/// to the Core pipeline, and render the tray glyph. The flyout opens only on a
/// user click (Constitution §2). Disk reads happen off the UI thread; UI/tray
/// updates are marshaled back via the dispatcher.
///
/// UNVERIFIED (Windows-only): the wiring is real, but WinUI/H.NotifyIcon calls
/// need a Windows build to confirm.
/// </summary>
public partial class App : Application
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly FleetViewModel _vm = new();
    private readonly TranscriptReader _transcripts = new();
    private readonly PrChecker _prs = new();

    private IReadOnlyList<IWatchRoot> _roots = Array.Empty<IWatchRoot>();
    private Dictionary<string, IWatchRoot> _rootById = new();
    private ClaudeSource? _source;
    private SessionWatcher? _watcher;
    private TaskbarIcon? _tray;
    private FlyoutWindow? _flyout;
    private IntPtr _trayIcon;   // current tray HICON; we own it (see SetTrayIcon)
    private DispatcherQueueTimer? _spin;
    private AgentState? _dominant;
    private int _frame;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _roots = WatchRoots.Discover();
        _rootById = _roots.ToDictionary(r => r.Id);
        _source = new ClaudeSource(_roots);

        _tray = new TaskbarIcon { ToolTipText = "Claude Watcher" };
        _tray.ForceCreate();
        // Without this, the click is held back by the double-click disambiguation
        // timer — which fires on a timer thread, where WinUI windows can't be touched.
        _tray.NoLeftClickDelay = true;
        _tray.LeftClickCommand = new RelayCommand(ToggleFlyout);

        _watcher = new SessionWatcher(_roots);
        _watcher.Changed += (_, _) => Refresh();
        _watcher.Start();

        // A PR lookup lands asynchronously; rebuild so the pill appears.
        _prs.Updated += Refresh;

        Refresh();
    }

    /// <summary>
    /// Rebuild the fleet snapshot. Runs off the UI thread (watcher callback): disk
    /// reads here, UI/tray mutation marshaled onto the dispatcher.
    /// </summary>
    private void Refresh()
    {
        if (_source is null) return;
        var sessions = _source.LiveSessions();

        // Resolve each session's repo path once — branch and PR lookups both need it.
        var repoPath = sessions.ToDictionary(
            s => s.Id,
            s => _rootById.TryGetValue(s.RootId, out var r) ? r.ResolvePath(s.Cwd) : null);
        var branchOf = sessions.ToDictionary(
            s => s.Id,
            s => repoPath[s.Id] is { } path ? GitBranch.Read(path) : null);

        var (views, counts) = FleetBuilder.Build(
            sessions,
            detail: s => _rootById.TryGetValue(s.RootId, out var r)
                ? _transcripts.Detail(s.Id, s.Cwd, r.HomeDir) : new SessionDetail(),
            branch: s => branchOf[s.Id],
            pr: s => _prs.Lookup(repoPath[s.Id], branchOf[s.Id]),
            homePrefix: s => _rootById.TryGetValue(s.RootId, out var r) && !r.IsWsl ? r.HomeDir : null,
            now: DateTimeOffset.Now,
            // A WSL pid is a Linux pid — it matches no Windows process, so don't ask.
            host: s => s.IsWsl ? null : HostDetector.For(s.Pid));

        // Keep the caches bounded to what's actually running.
        _transcripts.Prune(sessions.Select(s => s.Id));
        HostDetector.Prune(sessions.Where(s => !s.IsWsl).Select(s => s.Pid));
        _prs.Prune(sessions.Select(s => PrChecker.KeyFor(repoPath[s.Id], branchOf[s.Id])));

        _dispatcher.TryEnqueue(() =>
        {
            _vm.Update(views, counts);
            if (_tray is not null)
            {
                SetTrayIcon(counts.Dominant, _frame);
                SetAnimation(counts.Dominant);
                _tray.ToolTipText = $"Claude Watcher — {SummaryText.For(counts)}";
            }
        });
    }

    /// <summary>
    /// Swap in a freshly drawn glyph. We hand the shell a raw HICON (see
    /// <see cref="TrayIconRenderer"/>) and only release the previous handle once the
    /// replacement is in place, so the tray never points at a destroyed icon.
    /// </summary>
    private void SetTrayIcon(AgentState? dominant, int frame = 0)
    {
        if (_tray is null) return;

        var icon = TrayIconRenderer.CreateStateIcon(dominant, frame);
        if (icon == IntPtr.Zero) return;

        _tray.TrayIcon.UpdateIcon(icon);
        if (_trayIcon != IntPtr.Zero) TrayIconRenderer.DestroyIcon(_trayIcon);
        _trayIcon = icon;
    }

    /// <summary>
    /// Spin the spark while anything is working, and stop dead otherwise. This is the
    /// only thing in the app that ticks continuously, so it must not run when there is
    /// nothing to animate (Constitution §3) — idle and needs-you glyphs are static.
    /// </summary>
    private void SetAnimation(AgentState? dominant)
    {
        _dominant = dominant;

        if (dominant != AgentState.Working)
        {
            _spin?.Stop();
            return;
        }

        if (_spin is null)
        {
            _spin = _dispatcher.CreateTimer();
            _spin.Interval = TimeSpan.FromMilliseconds(160);   // ~6 fps: legible, near-free
            _spin.Tick += (_, _) =>
            {
                _frame = (_frame + 1) % TrayGlyph.Frames;
                SetTrayIcon(_dominant, _frame);
            };
        }
        if (!_spin.IsRunning) _spin.Start();
    }

    private void ToggleFlyout()
    {
        // Defense in depth: the tray click can arrive off the UI thread, and creating
        // or showing a Window there throws where nobody is listening.
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(ToggleFlyout);
            return;
        }

        _flyout ??= new FlyoutWindow(_vm, OnOpenAgent);
        _flyout.ToggleNearTray();
    }

    private void OnOpenAgent(AgentView agent) => TerminalFocus.Focus(agent.Pid, agent.RootId);

    public void Quit()
    {
        _spin?.Stop();
        _watcher?.Dispose();
        _tray?.Dispose();
        if (_trayIcon != IntPtr.Zero) TrayIconRenderer.DestroyIcon(_trayIcon);
        Exit();
    }
}

/// <summary>Minimal ICommand shim so we don't pull in a full MVVM toolkit yet.</summary>
internal sealed class RelayCommand(Action execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
