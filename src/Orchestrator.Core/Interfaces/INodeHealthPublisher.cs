using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Abstraction for publishing node health updates to connected clients.
/// Implemented by the Blazor dashboard via SignalR. A no-op default is
/// registered in non-dashboard deployments.
/// </summary>
public interface INodeHealthPublisher
{
    Task PublishAsync(NodeHealthStatus status, string nodeId, string displayName, CancellationToken ct = default);
}

/// <summary>No-op publisher used when no dashboard is present.</summary>
public sealed class NullNodeHealthPublisher : INodeHealthPublisher
{
    public Task PublishAsync(NodeHealthStatus status, string nodeId, string displayName, CancellationToken ct = default)
        => Task.CompletedTask;
}
