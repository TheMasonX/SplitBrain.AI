using Microsoft.AspNetCore.SignalR;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using SplitBrain.Dashboard.Hubs;

namespace SplitBrain.Dashboard.Services;

/// <summary>
/// Forwards metric snapshots, system alerts, agent step events, and token usage
/// records to all connected dashboard clients via SignalR, while also keeping
/// the in-process <see cref="DashboardState"/> up to date.
/// </summary>
public sealed class SignalRDashboardPublisher
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    private readonly DashboardState _state;

    public SignalRDashboardPublisher(
        IHubContext<DashboardHub, IDashboardClient> hub,
        DashboardState state)
    {
        _hub = hub;
        _state = state;
    }

    public async Task PublishAgentStepAsync(AgentStepEvent step, CancellationToken ct = default)
    {
        _state.AddAgentStepEvent(step);
        await _hub.Clients.All.ReceiveAgentStepEvent(step);
    }

    public async Task PublishTokenUsageAsync(TokenUsageRecord record, CancellationToken ct = default)
    {
        _state.AddTokenUsageRecord(record);
        await _hub.Clients.All.ReceiveTokenUsageUpdate(record);
    }

    public async Task PublishMetricAsync(MetricSnapshot snapshot, CancellationToken ct = default)
    {
        _state.AddMetricSnapshot(snapshot);
        await _hub.Clients.All.ReceiveMetricUpdate(snapshot);
    }

    public async Task PublishAlertAsync(SystemAlert alert, CancellationToken ct = default)
    {
        _state.AddAlert(alert);
        await _hub.Clients.All.ReceiveAlert(alert);
    }
}
