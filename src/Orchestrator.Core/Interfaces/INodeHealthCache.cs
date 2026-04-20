using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Stores the last-known <see cref="NodeHealth"/> for each node, refreshed by
/// the heartbeat loop.  Consumers read stale-but-cheap data instead of probing
/// Ollama on the hot routing path.
/// </summary>
public interface INodeHealthCache
{
    /// <summary>Updates the cached health for a node.</summary>
    void Set(NodeHealth health);

    /// <summary>
    /// Returns the most recently cached health for <paramref name="nodeId"/>,
    /// or <c>null</c> if no data has been received yet (cold cache).
    /// </summary>
    NodeHealth? Get(string nodeId);

    /// <summary>Returns all currently cached node health snapshots.</summary>
    IReadOnlyList<NodeHealth> GetAll();
}
