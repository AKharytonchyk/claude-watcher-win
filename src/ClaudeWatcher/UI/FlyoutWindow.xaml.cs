using ClaudeWatcher.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ClaudeWatcher.UI;

/// <summary>
/// The flyout shown when the user clicks the tray icon. Opens ONLY on user action
/// (Constitution §2). Borderless, acrylic, anchored bottom-right (above the tray),
/// and hides when it loses focus.
///
/// UNVERIFIED (Windows-only): windowing/backdrop/positioning APIs need a real box.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    public FleetViewModel VM { get; }
    private readonly Action<AgentView> _onOpen;
    private bool _visible;

    public FlyoutWindow(FleetViewModel vm, Action<AgentView> onOpen)
    {
        VM = vm;
        _onOpen = onOpen;
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

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

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AgentView agent)
        {
            _onOpen(agent);
            Hide();
        }
    }

    private void OnQuit(object sender, RoutedEventArgs e) =>
        (Application.Current as App)?.Quit();
}
