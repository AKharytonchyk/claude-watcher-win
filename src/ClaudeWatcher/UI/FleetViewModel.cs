using System.Collections.ObjectModel;
using System.ComponentModel;
using ClaudeWatcher.Core;

namespace ClaudeWatcher.UI;

/// <summary>
/// Observable snapshot the flyout binds to. Kept deliberately thin — all logic
/// lives in Core (<see cref="FleetBuilder"/>); this just holds the latest views
/// and summary. Analogue of the macOS <c>AgentsModel</c>.
///
/// UNVERIFIED (Windows-only).
/// </summary>
public sealed class FleetViewModel : INotifyPropertyChanged
{
    public ObservableCollection<AgentView> Agents { get; } = new();

    /// <summary>
    /// Per-state count badges for the header, in urgency order and only for states
    /// that actually have agents — the macOS popover's "● 1  ● 2" at a glance.
    /// </summary>
    public ObservableCollection<StatePill> Pills { get; } = new();

    private string _summary = "No running agents";
    public string Summary
    {
        get => _summary;
        private set { if (_summary != value) { _summary = value; OnChanged(nameof(Summary)); } }
    }

    private bool _isEmpty = true;
    public bool IsEmpty
    {
        get => _isEmpty;
        private set { if (_isEmpty != value) { _isEmpty = value; OnChanged(nameof(IsEmpty)); } }
    }

    // States the user has switched off via the header pills. Held here, not rebuilt
    // per refresh, so a filter survives the fleet changing underneath it.
    private readonly HashSet<AgentState> _muted = new();
    private IReadOnlyList<AgentView> _all = Array.Empty<AgentView>();
    private StatusCounts _counts;

    /// <summary>Replace the current fleet. Must be called on the UI thread.</summary>
    public void Update(IReadOnlyList<AgentView> views, StatusCounts counts)
    {
        _all = views;
        _counts = counts;
        Project();
    }

    /// <summary>
    /// Toggle a state's visibility. The pills double as filters: switching one off
    /// removes every agent in that state from the list. Counts keep showing the true
    /// totals — a filter shouldn't make you think agents disappeared.
    /// </summary>
    public void ToggleState(AgentState state)
    {
        if (!_muted.Remove(state)) _muted.Add(state);
        Project();
    }

    /// <summary>Apply the current filter to the last known fleet.</summary>
    private void Project()
    {
        var visible = _all.Where(v => !_muted.Contains(v.State)).ToList();

        Agents.Clear();
        foreach (var v in visible) Agents.Add(v);

        Pills.Clear();
        foreach (var (state, count) in _counts.Present)
            Pills.Add(new StatePill(state, count, !_muted.Contains(state)));

        Summary = SummaryText.For(_counts);
        IsEmpty = visible.Count == 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One header badge: a state, how many agents are in it, and whether that state is
/// currently shown. Doubles as the filter control.
/// </summary>
public sealed record StatePill(AgentState State, int Count, bool IsOn)
{
    /// <summary>Muted states read as switched off rather than merely quiet.</summary>
    public double Dim => IsOn ? 1.0 : 0.4;

    public string Tip => IsOn ? $"Hide {Label}" : $"Show {Label}";

    private string Label => State switch
    {
        AgentState.Waiting => "agents that need you",
        AgentState.Working => "working agents",
        _                  => "idle agents",
    };
}
