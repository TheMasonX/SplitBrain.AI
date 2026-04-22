using Microsoft.AspNetCore.SignalR;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using SplitBrain.Dashboard.Hubs;

namespace SplitBrain.Dashboard.Services;

/// <summary>
/// Publishes node health updates to all connected dashboard clients via SignalR.
/// Register this as INodeHealthPublisher in the Dashboard DI container.
/// </summary>
public sealed class SignalRNodeHealthPublisher : INodeHealthPublisher
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    private readonly DashboardState _state;

    public SignalRNodeHealthPublisher(
        IHubContext<DashboardHub, IDashboardClient> hub,
        DashboardState state)
    {
        _hub = hub;
        _state = state;
    }

    public async Task PublishAsync(
        NodeHealthStatus status,
        string nodeId,
        string displayName,
        CancellationToken ct = default)
    {
        var snapshot = new NodeHealthSnapshot
        {
            NodeId = nodeId,
            DisplayName = displayName,
            Health = status,
            Timestamp = DateTimeOffset.UtcNow
        };

        _state.UpdateNodeHealth(snapshot);
        await _hub.Clients.All.ReceiveNodeHealthUpdate(snapshot);
    }
}
