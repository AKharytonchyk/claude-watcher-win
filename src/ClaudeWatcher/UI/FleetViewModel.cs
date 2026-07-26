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

    /// <summary>Replace the current fleet. Must be called on the UI thread.</summary>
    public void Update(IReadOnlyList<AgentView> views, StatusCounts counts)
    {
        Agents.Clear();
        foreach (var v in views) Agents.Add(v);

        Pills.Clear();
        foreach (var (state, count) in counts.Present) Pills.Add(new StatePill(state, count));

        Summary = SummaryText.For(counts);
        IsEmpty = views.Count == 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One header count badge: a state and how many agents are in it.</summary>
public sealed record StatePill(AgentState State, int Count);
