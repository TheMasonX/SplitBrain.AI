using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public record NodeRegistration
{
    public required NodeConfiguration Config { get; init; }
    public required IInferenceNode Node { get; init; }
    public NodeHealthStatus? LastHealth { get; set; }
}

public interface INodeRegistry
{
    IReadOnlyList<NodeRegistration> GetAllNodes();
    IReadOnlyList<NodeRegistration> GetHealthyNodes();
    IReadOnlyList<NodeRegistration> GetNodesByRole(Configuration.NodeRole role);
    IReadOnlyList<NodeRegistration> GetNodesByTag(string tag);
    NodeRegistration? GetNode(string nodeId);
    void RegisterNode(NodeConfiguration config);
    void DeregisterNode(string nodeId);
    void UpdateNodeHealth(string nodeId, NodeHealthStatus status);
    Task SaveTopologyAsync(CancellationToken ct = default);
}
