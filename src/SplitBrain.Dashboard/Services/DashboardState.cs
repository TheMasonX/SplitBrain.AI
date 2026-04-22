using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using SplitBrain.Dashboard.Hubs;

namespace SplitBrain.Dashboard.Services;

/// <summary>
/// In-memory store of the latest node health snapshots.
/// Blazor components subscribe to OnChange and re-render on updates.
/// Updated by the SignalR DashboardHub when new health snapshots arrive.
/// </summary>
public sealed class DashboardState
{
    private readonly Dictionary<string, NodeHealthSnapshot> _nodeHealth = [];
    private readonly List<StructuredLogEntry> _recentLogs = [];
    private readonly Dictionary<string, TaskStatusUpdate> _agentTasks = [];
    private const int MaxLogs = 500;

    public event Action? OnChange;

    public IReadOnlyDictionary<string, NodeHealthSnapshot> NodeHealth => _nodeHealth;
    public IReadOnlyList<StructuredLogEntry> RecentLogs => _recentLogs;
    public IReadOnlyDictionary<string, TaskStatusUpdate> AgentTasks => _agentTasks;

    public void UpdateNodeHealth(NodeHealthSnapshot snapshot)
    {
        _nodeHealth[snapshot.NodeId] = snapshot;
        NotifyChanged();
    }

    public void AddLogEntry(StructuredLogEntry entry)
    {
        _recentLogs.Insert(0, entry);
        if (_recentLogs.Count > MaxLogs)
            _recentLogs.RemoveAt(_recentLogs.Count - 1);
        NotifyChanged();
    }

    public void UpdateTaskStatus(TaskStatusUpdate update)
    {
        _agentTasks[update.TaskId] = update;
        NotifyChanged();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
