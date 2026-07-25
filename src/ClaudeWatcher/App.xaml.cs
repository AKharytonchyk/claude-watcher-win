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

    private IReadOnlyList<IWatchRoot> _roots = Array.Empty<IWatchRoot>();
    private Dictionary<string, IWatchRoot> _rootById = new();
    private ClaudeSource? _source;
    private SessionWatcher? _watcher;
    private TaskbarIcon? _tray;
    private FlyoutWindow? _flyout;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _roots = WatchRoots.Discover();
        _rootById = _roots.ToDictionary(r => r.Id);
        _source = new ClaudeSource(_roots);

        _tray = new TaskbarIcon { ToolTipText = "Claude Watcher" };
        _tray.ForceCreate();
        _tray.LeftClickCommand = new RelayCommand(ToggleFlyout);

        _watcher = new SessionWatcher(_roots);
        _watcher.Changed += (_, _) => Refresh();
        _watcher.Start();

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

        var (views, counts) = FleetBuilder.Build(
            sessions,
            detail: s => _rootById.TryGetValue(s.RootId, out var r)
                ? _transcripts.Detail(s.Id, s.Cwd, r.HomeDir) : new SessionDetail(),
            branch: s => _rootById.TryGetValue(s.RootId, out var r)
                ? GitBranch.Read(r.ResolvePath(s.Cwd)) : null,
            homePrefix: s => _rootById.TryGetValue(s.RootId, out var r) && !r.IsWsl ? r.HomeDir : null,
            now: DateTimeOffset.Now);

        _dispatcher.TryEnqueue(() =>
        {
            _vm.Update(views, counts);
            if (_tray is not null)
            {
                _tray.IconSource = TrayIconRenderer.DotBitmap(counts.Dominant);
                _tray.ToolTipText = $"Claude Watcher — {SummaryText.For(counts)}";
            }
        });
    }

    private void ToggleFlyout()
    {
        _flyout ??= new FlyoutWindow(_vm, OnOpenAgent);
        _flyout.ToggleNearTray();
    }

    private void OnOpenAgent(AgentView agent) => TerminalFocus.Focus(agent.Pid, agent.RootId);

    public void Quit()
    {
        _watcher?.Dispose();
        _tray?.Dispose();
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
