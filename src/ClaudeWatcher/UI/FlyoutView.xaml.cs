using ClaudeWatcher.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClaudeWatcher.UI;

/// <summary>
/// The flyout's content (a FrameworkElement so x:Bind works — a Window is not a
/// FrameworkElement). Hosted by <see cref="FlyoutWindow"/>.
///
/// UNVERIFIED (Windows-only).
/// </summary>
public sealed partial class FlyoutView : UserControl
{
    public FleetViewModel VM { get; }
    private readonly Action<AgentView> _onOpen;

    /// <summary>Raised after the user acts on a row, so the host can dismiss.</summary>
    public event Action? CloseRequested;

    public FlyoutView(FleetViewModel vm, Action<AgentView> onOpen)
    {
        VM = vm;
        _onOpen = onOpen;
        InitializeComponent();
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AgentView agent)
        {
            _onOpen(agent);
            CloseRequested?.Invoke();
        }
    }

    /// <summary>
    /// The PR pill was clicked: open it in the browser and dismiss, the same way acting
    /// on a row does. The row's own click never runs — the Button consumes it.
    /// </summary>
    private void OnPrClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgentView agent } && agent.Pr is { } pr &&
            Platform.Browser.Open(pr.Url))
            CloseRequested?.Invoke();
    }

    /// <summary>A header pill was clicked: filter that state in or out of the list.</summary>
    private void OnPillClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StatePill pill })
            VM.ToggleState(pill.State);
    }

    private void OnQuit(object sender, RoutedEventArgs e) =>
        (Application.Current as App)?.Quit();
}
