using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using SplitBrain.Dashboard.Hubs;

namespace SplitBrain.Dashboard.Services;

/// <summary>
/// In-memory store of live dashboard state.
/// Blazor components subscribe to <see cref="OnChange"/> and re-render on updates.
/// </summary>
public sealed class DashboardState
{
    private readonly Dictionary<string, NodeHealthSnapshot> _nodeHealth = [];
    private readonly List<StructuredLogEntry> _recentLogs = [];
    private readonly Dictionary<string, TaskStatusUpdate> _agentTasks = [];
    private readonly Dictionary<string, List<AgentStepEvent>> _agentSteps = [];
    private readonly List<MetricSnapshot> _recentMetrics = [];
    private readonly List<SystemAlert> _activeAlerts = [];
    private readonly List<TokenUsageRecord> _recentTokenUsage = [];

    private const int MaxLogs = 500;
    private const int MaxMetrics = 1000;
    private const int MaxTokenRecords = 200;

    public event Action? OnChange;

    public IReadOnlyDictionary<string, NodeHealthSnapshot> NodeHealth => _nodeHealth;
    public IReadOnlyList<StructuredLogEntry> RecentLogs => _recentLogs;
    public IReadOnlyDictionary<string, TaskStatusUpdate> AgentTasks => _agentTasks;
    public IReadOnlyDictionary<string, List<AgentStepEvent>> AgentSteps => _agentSteps;
    public IReadOnlyList<MetricSnapshot> RecentMetrics => _recentMetrics;
    public IReadOnlyList<SystemAlert> ActiveAlerts => _activeAlerts;
    public IReadOnlyList<TokenUsageRecord> RecentTokenUsage => _recentTokenUsage;

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

    public void AddAgentStepEvent(AgentStepEvent step)
    {
        if (!_agentSteps.TryGetValue(step.TaskId, out var steps))
        {
            steps = [];
            _agentSteps[step.TaskId] = steps;
        }
        // Insert in order; avoid duplicates by StepIndex
        if (!steps.Any(s => s.StepIndex == step.StepIndex))
        {
            steps.Add(step);
            steps.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
        }
        NotifyChanged();
    }

    public void AddMetricSnapshot(MetricSnapshot snapshot)
    {
        _recentMetrics.Insert(0, snapshot);
        if (_recentMetrics.Count > MaxMetrics)
            _recentMetrics.RemoveAt(_recentMetrics.Count - 1);
        NotifyChanged();
    }

    public void AddAlert(SystemAlert alert)
    {
        if (!_activeAlerts.Any(a => a.AlertId == alert.AlertId))
        {
            _activeAlerts.Insert(0, alert);
            NotifyChanged();
        }
    }

    public void DismissAlert(string alertId)
    {
        var idx = _activeAlerts.FindIndex(a => a.AlertId == alertId);
        if (idx >= 0)
        {
            _activeAlerts.RemoveAt(idx);
            NotifyChanged();
        }
    }

    public void AddTokenUsageRecord(TokenUsageRecord record)
    {
        _recentTokenUsage.Insert(0, record);
        if (_recentTokenUsage.Count > MaxTokenRecords)
            _recentTokenUsage.RemoveAt(_recentTokenUsage.Count - 1);
        NotifyChanged();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
