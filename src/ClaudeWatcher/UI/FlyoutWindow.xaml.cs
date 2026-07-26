using ClaudeWatcher.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace ClaudeWatcher.UI;

/// <summary>
/// Thin host for <see cref="FlyoutView"/>. Opens ONLY on a user click
/// (Constitution §2). Borderless, acrylic, anchored bottom-right (above the tray),
/// and hides when it loses focus.
///
/// UNVERIFIED (Windows-only): windowing/backdrop/positioning + sizing need a real
/// box. Size is fixed for now; make it fit content on-device.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const int WidthDip = 360;
    private const int HeightDip = 560;

    /// <summary>
    /// A tray click that lands within this of a dismissal is treated as "the user
    /// closed it", not "open it again" — see <see cref="ToggleNearTray"/>.
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);

    /// <summary>Grace period after showing, before click-away detection arms.</summary>
    private static readonly TimeSpan ShowGrace = TimeSpan.FromMilliseconds(400);

    private readonly FlyoutView _view;
    private readonly DispatcherQueueTimer _dismissWatch;
    private DateTimeOffset _dismissedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _shownAt = DateTimeOffset.MinValue;
    private bool _hadFocus;

    public FlyoutWindow(FleetViewModel vm, Action<AgentView> onOpen)
    {
        InitializeComponent();

        _view = new FlyoutView(vm, onOpen);
        _view.CloseRequested += Hide;
        Content = _view;

        SystemBackdrop = new DesktopAcrylicBackdrop();

        // A normal presenter, NOT CreateForContextMenu(): a context-menu window never
        // takes ordinary activation, so Activated/Deactivated never fire and the
        // click-away dismissal below silently does nothing — the flyout just sinks
        // behind other windows while still being "shown", which desynced the toggle.
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;      // it's a flyout; it must sit above the shell
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(new SizeInt32(WidthDip, HeightDip));

        // Dismiss when the user clicks away. Activated/Deactivated is unreliable for a
        // borderless always-on-top window that's hidden from switchers — it did not fire
        // at all here — so the authority is a cheap foreground-window poll that only
        // runs while the flyout is actually up. The event stays as a fast path.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) Dismiss();
        };

        _dismissWatch = DispatcherQueue.CreateTimer();
        _dismissWatch.Interval = TimeSpan.FromMilliseconds(200);
        _dismissWatch.Tick += (_, _) =>
        {
            if (!AppWindow.IsVisible) { _dismissWatch.Stop(); return; }

            if (IsForeground()) { _hadFocus = true; return; }
            if (DateTimeOffset.UtcNow - _shownAt < ShowGrace) return;

            // Only dismiss if we HELD focus and then lost it. Windows' foreground lock
            // can refuse focus to a background tray app entirely (the shell keeps it
            // after the icon click); dismissing on "not foreground" alone would hide the
            // flyout the instant it opened. No focus ever ⇒ dismissal is the tray click's
            // job, not ours.
            if (_hadFocus) Dismiss();
        };
    }

    /// <summary>True while this window owns the foreground.</summary>
    private bool IsForeground() => GetForegroundWindow() == WindowNative.GetWindowHandle(this);

    /// <summary>Hide and remember when, so a tray click now reads as "close".</summary>
    private void Dismiss()
    {
        if (!AppWindow.IsVisible) return;
        _dismissedAt = DateTimeOffset.UtcNow;
        Hide();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Show near the tray if hidden, otherwise hide. User-initiated only.
    /// Visibility is read from the window itself rather than tracked in a field: a
    /// click-away dismissal hides it behind our back, and a private flag would drift
    /// out of sync and leave every other click looking dead.
    /// </summary>
    public void ToggleNearTray()
    {
        if (AppWindow.IsVisible) { Hide(); return; }

        // Clicking the tray icon while the flyout is open deactivates it first, so it
        // has already hidden itself by the time we get here. Without this guard that
        // click would immediately reopen it, and it could never be closed from the tray.
        if (DateTimeOffset.UtcNow - _dismissedAt < ReopenGuard) return;

        PositionBottomRight();
        _shownAt = DateTimeOffset.UtcNow;
        _hadFocus = false;
        AppWindow.Show(activateWindow: true);
        Activate();
        // Ask for foreground explicitly. Windows may refuse (we're a background app
        // that didn't receive the click — the shell did), which is tolerated: the
        // dismissal watch only acts on focus it actually saw us hold.
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
        _dismissWatch.Start();
    }

    private void Hide()
    {
        _dismissWatch.Stop();
        AppWindow.Hide();
    }

    /// <summary>Place the flyout at the bottom-right work-area corner (above the tray).</summary>
    private void PositionBottomRight()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        const int margin = 12;
        var size = AppWindow.Size;
        var x = area.X + area.Width - size.Width - margin;
        var y = area.Y + area.Height - size.Height - margin;
        AppWindow.Move(new PointInt32(x, y));
    }
}
