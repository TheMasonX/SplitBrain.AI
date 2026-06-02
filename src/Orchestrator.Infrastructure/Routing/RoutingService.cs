using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Queue;

namespace Orchestrator.Infrastructure.Routing;

public sealed class RoutingService : IRoutingService
{
    private const int LargeContextTokenThreshold = 5_000;
    private const int QueueFallbackThreshold = 2;
    private const int DefaultVramMb = 8_192;

    private readonly INodeRegistry _registry;
    private readonly INodeHealthCache? _healthCache;
    private readonly IMetricsCollector? _metrics;
    private readonly IPromptHistory? _history;
    private readonly ILogger<RoutingService> _logger;
    private readonly RoutingOptions _routingOptions;
    private readonly Func<string, IInferenceQueue> _queueFactory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IInferenceQueue> _queues = new();

    public RoutingService(
        INodeRegistry registry,
        Func<string, IInferenceQueue> queueFactory,
        ILogger<RoutingService> logger,
        INodeHealthCache? healthCache = null,
        IMetricsCollector? metrics = null,
        IPromptHistory? history = null,
        IOptions<RoutingOptions>? routingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(queueFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _queueFactory = queueFactory;
        _healthCache = healthCache;
        _metrics = metrics;
        _history = history;
        _logger = logger;
        _routingOptions = routingOptions?.Value ?? new RoutingOptions();
    }

    public RoutingService(
        IInferenceNode nodeA,
        IInferenceQueue nodeAQueue,
        ILogger<RoutingService> logger,
        IInferenceNode? nodeB = null,
        IInferenceQueue? nodeBQueue = null,
        INodeHealthCache? healthCache = null,
        IMetricsCollector? metrics = null,
        IPromptHistory? history = null,
        IInferenceNode? nodeC = null,
        IInferenceQueue? nodeCQueue = null,
        IOptions<RoutingOptions>? routingOptions = null)
        : this(new SimpleNodeRegistry(nodeA, nodeB, nodeC),
               nodeId => CreateQueueForNode(nodeId, nodeA.NodeId, nodeAQueue, nodeB?.NodeId, nodeBQueue, nodeC?.NodeId, nodeCQueue),
               logger, healthCache, metrics, history, routingOptions)
    { }

    private static IInferenceQueue CreateQueueForNode(string nodeId, string aId, IInferenceQueue aQ, string? bId, IInferenceQueue? bQ, string? cId, IInferenceQueue? cQ)
    {
        if (nodeId == aId) return aQ;
        if (bId is not null && nodeId == bId && bQ is not null) return bQ;
        if (cId is not null && nodeId == cId && cQ is not null) return cQ;
        return new NodeQueue(capacity: 32);
    }

    private sealed class SimpleNodeRegistry : INodeRegistry
    {
        private readonly List<NodeRegistration> _regs;
        public SimpleNodeRegistry(IInferenceNode a, IInferenceNode? b, IInferenceNode? c)
        {
            _regs = new List<NodeRegistration>();
            _regs.Add(new NodeRegistration { Config = new NodeConfiguration { DisplayName = $"Node{a.NodeId}", NodeId = a.NodeId, Provider = NodeProviderType.Ollama, Role = NodeRole.Fast }, Node = a });
            if (b is not null) _regs.Add(new NodeRegistration { Config = new NodeConfiguration { DisplayName = $"Node{b.NodeId}", NodeId = b.NodeId, Provider = NodeProviderType.Ollama, Role = NodeRole.Deep }, Node = b });
            if (c is not null) _regs.Add(new NodeRegistration { Config = new NodeConfiguration { DisplayName = $"Node{c.NodeId}", NodeId = c.NodeId, Provider = NodeProviderType.CopilotSdk, Role = NodeRole.Hybrid }, Node = c });
        }
        public IReadOnlyList<NodeRegistration> GetAllNodes() => _regs;
        public IReadOnlyList<NodeRegistration> GetHealthyNodes() => _regs;
        public IReadOnlyList<NodeRegistration> GetNodesByRole(NodeRole role) => _regs.Where(r => r.Config.Role == role).ToList();
        public IReadOnlyList<NodeRegistration> GetNodesByTag(string tag) => _regs.Where(r => r.Config.Tags.Contains(tag)).ToList();
        public NodeRegistration? GetNode(string nodeId) => _regs.FirstOrDefault(r => r.Config.NodeId == nodeId);
        public void RegisterNode(NodeConfiguration config) => throw new NotSupportedException();
        public void DeregisterNode(string nodeId) => throw new NotSupportedException();
        public void UpdateNodeHealth(string nodeId, NodeHealthStatus status) { }
        public Task SaveTopologyAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public async Task<InferenceResult> RouteAsync(TaskType taskType, InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var registrations = _registry.GetAllNodes();
        if (registrations.Count == 0) throw new InvalidOperationException("No nodes are registered in the topology.");
        var target = SelectNode(registrations, taskType, request);
        _logger.LogInformation("Routing taskType={TaskType} -> node={NodeId} role={Role}", taskType, target.Node.NodeId, target.Config.Role);
        var item = new InferenceQueueItem { Request = request, TaskType = taskType };
        var queue = GetQueue(target.Node.NodeId);
        if (!queue.TryEnqueue(item))
        {
            _logger.LogWarning("Queue full on node={NodeId}, trying fallback", target.Node.NodeId);
            var fallback = registrations.Where(r => r.Node.NodeId != target.Node.NodeId).OrderByDescending(r => ComputeScore(r, taskType, request)).FirstOrDefault();
            if (fallback is not null && GetQueue(fallback.Node.NodeId).TryEnqueue(item)) target = fallback;
            else { _logger.LogError("All node queues full"); return await target.Node.ExecuteAsync(request, cancellationToken); }
        }
        _ = DrainAsync(target, registrations, item, cancellationToken);
        var historyId = _history?.Add(request.Prompt, taskType, target.Node.NodeId);
        var result = await item.Completion.Task.WaitAsync(cancellationToken);
        if (historyId is not null) _history!.Complete(historyId, result.Text, success: true);
        return result;
    }

    public IInferenceNode SelectNode(TaskType taskType, InferenceRequest request) { var regs = _registry.GetAllNodes(); return SelectNode(regs, taskType, request).Node; }

    internal NodeRegistration SelectNode(IReadOnlyList<NodeRegistration> registrations, TaskType taskType, InferenceRequest request)
    {
        var available = registrations.Where(r => !IsUnavailable(r.Node.NodeId)).ToList();
        if (available.Count == 0) available = [.. registrations];
        if (taskType == TaskType.Autocomplete) return available.FirstOrDefault(r => r.Config.Role == NodeRole.Fast) ?? available[0];
        var notOverloaded = available.Where(r => GetQueue(r.Node.NodeId).Count <= QueueFallbackThreshold).ToList();
        if (notOverloaded.Count == 0) notOverloaded = available;
        return notOverloaded.OrderByDescending(r => ComputeScore(r, taskType, request)).First();
    }

    private double ComputeScore(NodeRegistration registration, TaskType taskType, InferenceRequest request)
    {
        var node = registration.Node; var role = registration.Config.Role; var health = _healthCache?.Get(node.NodeId);
        double vramRatio = registration.Config.Provider == NodeProviderType.CopilotSdk ? 0.75 : Math.Clamp((double)(health?.AvailableVramMb ?? (node.Capabilities?.VramMb > 0 ? node.Capabilities.VramMb : DefaultVramMb)) / (node.Capabilities?.VramMb > 0 ? node.Capabilities.VramMb : DefaultVramMb), 0.0, 1.0);
        var depth = GetQueue(node.NodeId).Count; var queueFactor = 1.0 / (depth + 1.0);
        var roleFit = (role, taskType) switch { (NodeRole.Fast, TaskType.Autocomplete or TaskType.Chat) => 1.0, (NodeRole.Fast, _) => 0.4, (NodeRole.Deep, TaskType.Review or TaskType.Refactor or TaskType.TestGeneration or TaskType.AgentStep) => 1.0, (NodeRole.Deep, TaskType.Chat) => 0.6, (NodeRole.Deep, _) => 0.5, (NodeRole.Hybrid, TaskType.Review or TaskType.Refactor or TaskType.TestGeneration or TaskType.AgentStep) => 0.9, (NodeRole.Hybrid, _) => 0.7, (NodeRole.Standby, _) => 0.1, _ => 0.5 };
        var latencyMs = health?.Status == NodeStatus.Degraded ? 8_000 : 500; var latencyPenalty = 1.0 - Math.Clamp(latencyMs / 10_000.0, 0.0, 1.0);
        var tokens = EstimateTokens(request.Prompt);
        var contextFit = role is NodeRole.Deep or NodeRole.Hybrid ? Math.Clamp(tokens / (double)LargeContextTokenThreshold, 0.0, 1.0) : 1.0 - Math.Clamp(tokens / (double)LargeContextTokenThreshold, 0.0, 1.0);
        return (0.35 * vramRatio) + (0.25 * queueFactor) + (0.20 * roleFit) + (0.10 * latencyPenalty) + (0.10 * contextFit);
    }

    private IInferenceQueue GetQueue(string nodeId) => _queues.GetOrAdd(nodeId, id => _queueFactory(id));
    private bool IsUnavailable(string nodeId) => _healthCache?.Get(nodeId)?.Status == NodeStatus.Unavailable;

    private Task DrainAsync(NodeRegistration target, IReadOnlyList<NodeRegistration> allRegistrations, InferenceQueueItem item, CancellationToken ct)
    {
        if (item.Completion.Task.IsCompleted) return Task.CompletedTask;
        return Task.Run(async () => await DrainWithFallbackAsync(target, allRegistrations, item, ct), ct);
    }

    private async Task DrainWithFallbackAsync(NodeRegistration primary, IReadOnlyList<NodeRegistration> allRegistrations, InferenceQueueItem item, CancellationToken ct)
    {
        var chain = new List<IInferenceNode> { primary.Node };
        if (_routingOptions.FallbackChains.TryGetValue(primary.Node.NodeId, out var fallbackIds))
            foreach (var id in fallbackIds) { var fb = allRegistrations.FirstOrDefault(r => r.Node.NodeId == id); if (fb is not null) chain.Add(fb.Node); }
        Exception? lastEx = null;
        foreach (var candidate in chain)
        {
            if (item.Completion.Task.IsCompleted) return;
            try { var result = await candidate.ExecuteAsync(item.Request, ct); item.Completion.TrySetResult(result); _metrics?.Record(new RequestMetric { TaskId = item.TaskId, NodeId = result.NodeId, Model = result.Model, TaskType = item.TaskType.ToString(), TokensIn = result.TokensIn, TokensOut = result.TokensOut, LatencyMs = result.LatencyMs, Success = true }); return; }
            catch (OperationCanceledException) { item.Completion.TrySetCanceled(ct); return; }
            catch (Exception ex) when (candidate != chain[^1]) { lastEx = ex; _logger.LogWarning(ex, "Node {NodeId} failed", candidate.NodeId); _metrics?.Record(new RequestMetric { TaskId = item.TaskId, NodeId = candidate.NodeId, Model = candidate.Capabilities.Model, TaskType = item.TaskType.ToString(), LatencyMs = 0, Success = false }); }
            catch (Exception ex) { lastEx = ex; _metrics?.Record(new RequestMetric { TaskId = item.TaskId, NodeId = candidate.NodeId, Model = candidate.Capabilities.Model, TaskType = item.TaskType.ToString(), LatencyMs = 0, Success = false }); item.Completion.TrySetException(ex); return; }
        }
        if (lastEx is not null) item.Completion.TrySetException(lastEx);
    }

    private static bool IsConnectivityException(Exception ex) { if (ex is System.Net.Http.HttpRequestException httpEx) { if (httpEx.InnerException is System.Net.Sockets.SocketException) return true; if (httpEx.InnerException is TaskCanceledException) return true; } return false; }
    private static int EstimateTokens(string text) => text.Length / 4;
}
