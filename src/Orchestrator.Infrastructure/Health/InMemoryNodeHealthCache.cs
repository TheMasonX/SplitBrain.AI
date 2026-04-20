using System.Collections.Concurrent;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Health;

/// <summary>
/// Thread-safe in-process health cache.  Updated by the heartbeat loop and
/// read by <see cref="Orchestrator.Infrastructure.Routing.RoutingService"/>
/// on every route decision.
/// </summary>
public sealed class InMemoryNodeHealthCache : INodeHealthCache
{
    private readonly ConcurrentDictionary<string, NodeHealth> _store = new();

    public void Set(NodeHealth health) =>
        _store[health.NodeId] = health;

    public NodeHealth? Get(string nodeId) =>
        _store.TryGetValue(nodeId, out var h) ? h : null;

    public IReadOnlyList<NodeHealth> GetAll() =>
        _store.Values.ToList().AsReadOnly();
}
