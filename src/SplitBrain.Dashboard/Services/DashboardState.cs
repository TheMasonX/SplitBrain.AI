using System.Collections.Concurrent;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using SplitBrain.Dashboard.Hubs;

namespace SplitBrain.Dashboard.Services;

/// <summary>
/// In-memory store of live dashboard state.
/// Blazor components subscribe to <see cref="OnChange"/> and re-render on updates.
///
/// Thread-safety: All collections are guarded by <see cref="_lock"/> or use
/// ConcurrentDictionary so SignalR hub callbacks and Blazor render cycles
/// can overlap safely.
/// </summary>
public sealed class DashboardState
{
    private readonly object _lock = new();

    private readonly ConcurrentDictionary<string, NodeHealthSnapshot> _nodeHealth = new();
    private readonly List<StructuredLogEntry> _recentLogs = [];
    private readonly ConcurrentDictionary<string, TaskStatusUpdate> _agentTasks = new();
    private readonly ConcurrentDictionary<string, List<AgentStepEvent>> _agentSteps = new();
    private readonly List<MetricSnapshot> _recentMetrics = [];
    private readonly List<SystemAlert> _activeAlerts = [];
    private readonly List<TokenUsageRecord> _recentTokenUsage = [];

    /// <summary>Insertion-ordered keys for LRU eviction of _agentSteps.</summary>
    private readonly LinkedList<string> _agentStepsOrder = new();

    private const int MaxLogs = 500;
    private const int MaxMetrics = 1000;
    private const int MaxTokenRecords = 200;
    /// <summary>Maximum number of tracked task IDs in _agentSteps to prevent unbounded memory growth.</summary>
    private const int MaxTrackedTasks = 1000;

    public event Action? OnChange;

    // --- Public read-only properties return snapshot copies to avoid
    //     InvalidOperationException if a mutation occurs mid-enumeration. ---

    public IReadOnlyDictionary<string, NodeHealthSnapshot> NodeHealth
        => new Dictionary<string, NodeHealthSnapshot>(_nodeHealth);

    public IReadOnlyList<StructuredLogEntry> RecentLogs
    {
        get { lock (_lock) { return _recentLogs.ToList(); } }
    }

    public IReadOnlyDictionary<string, TaskStatusUpdate> AgentTasks
        => new Dictionary<string, TaskStatusUpdate>(_agentTasks);

    public IReadOnlyDictionary<string, List<AgentStepEvent>> AgentSteps
        => new Dictionary<string, List<AgentStepEvent>>(_agentSteps);

    public IReadOnlyList<MetricSnapshot> RecentMetrics
    {
        get { lock (_lock) { return _recentMetrics.ToList(); } }
    }

    public IReadOnlyList<SystemAlert> ActiveAlerts
    {
        get { lock (_lock) { return _activeAlerts.ToList(); } }
    }

    public IReadOnlyList<TokenUsageRecord> RecentTokenUsage
    {
        get { lock (_lock) { return _recentTokenUsage.ToList(); } }
    }

    public void UpdateNodeHealth(NodeHealthSnapshot snapshot)
    {
        _nodeHealth[snapshot.NodeId] = snapshot;
        NotifyChanged();
    }

    public void AddLogEntry(StructuredLogEntry entry)
    {
        lock (_lock)
        {
            _recentLogs.Insert(0, entry);
            if (_recentLogs.Count > MaxLogs)
                _recentLogs.RemoveAt(_recentLogs.Count - 1);
        }
        NotifyChanged();
    }

    public void UpdateTaskStatus(TaskStatusUpdate update)
    {
        _agentTasks[update.TaskId] = update;
        NotifyChanged();
    }

    public void AddAgentStepEvent(AgentStepEvent step)
    {
        // ConcurrentDictionary.GetOrAdd is atomic for key presence,
        // but the inner List still needs locking for mutation.
        var steps = _agentSteps.GetOrAdd(step.TaskId, _ =>
        {
            // Track insertion order for LRU eviction
            lock (_lock)
            {
                _agentStepsOrder.AddLast(step.TaskId);
                EvictOldestTasksIfNeeded();
            }
            return new List<AgentStepEvent>();
        });

        lock (_lock)
        {
            // Insert in order; avoid duplicates by StepIndex
            if (!steps.Any(s => s.StepIndex == step.StepIndex))
            {
                steps.Add(step);
                steps.Sort((a, b) => a.StepIndex.CompareTo(b.StepIndex));
            }
        }

        NotifyChanged();
    }

    public void AddMetricSnapshot(MetricSnapshot snapshot)
    {
        lock (_lock)
        {
            _recentMetrics.Insert(0, snapshot);
            if (_recentMetrics.Count > MaxMetrics)
                _recentMetrics.RemoveAt(_recentMetrics.Count - 1);
        }
        NotifyChanged();
    }

    public void AddAlert(SystemAlert alert)
    {
        lock (_lock)
        {
            if (!_activeAlerts.Any(a => a.AlertId == alert.AlertId))
            {
                _activeAlerts.Insert(0, alert);
            }
        }
        NotifyChanged();
    }

    public void DismissAlert(string alertId)
    {
        lock (_lock)
        {
            var idx = _activeAlerts.FindIndex(a => a.AlertId == alertId);
            if (idx >= 0)
            {
                _activeAlerts.RemoveAt(idx);
            }
        }
        NotifyChanged();
    }

    public void AddTokenUsageRecord(TokenUsageRecord record)
    {
        lock (_lock)
        {
            _recentTokenUsage.Insert(0, record);
            if (_recentTokenUsage.Count > MaxTokenRecords)
                _recentTokenUsage.RemoveAt(_recentTokenUsage.Count - 1);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Evicts the oldest task IDs from _agentSteps when the count exceeds
    /// <see cref="MaxTrackedTasks"/>. Must be called under <see cref="_lock"/>.
    /// </summary>
    private void EvictOldestTasksIfNeeded()
    {
        while (_agentStepsOrder.Count > MaxTrackedTasks)
        {
            var oldest = _agentStepsOrder.First;
            if (oldest is not null)
            {
                _agentStepsOrder.RemoveFirst();
                _agentSteps.TryRemove(oldest.Value, out _);
            }
        }
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
