using ClaudeWatcher.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

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

    private readonly FlyoutView _view;
    private bool _visible;

    public FlyoutWindow(FleetViewModel vm, Action<AgentView> onOpen)
    {
        InitializeComponent();

        _view = new FlyoutView(vm, onOpen);
        _view.CloseRequested += Hide;
        Content = _view;

        SystemBackdrop = new DesktopAcrylicBackdrop();

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(new SizeInt32(WidthDip, HeightDip));

        // Hide when the user clicks away.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) Hide();
        };
    }

    /// <summary>Show near the tray if hidden, otherwise hide. User-initiated only.</summary>
    public void ToggleNearTray()
    {
        if (_visible) { Hide(); return; }
        PositionBottomRight();
        _visible = true;
        AppWindow.Show();
        Activate();
    }

    private void Hide()
    {
        _visible = false;
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
