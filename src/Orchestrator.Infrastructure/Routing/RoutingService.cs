using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Utilities;

namespace Orchestrator.Infrastructure.Routing;

public sealed class RoutingService : IRoutingService
{
    /// <summary>Context token threshold above which Deep/Hybrid nodes are preferred (§6.3).</summary>
    private const int LargeContextTokenThreshold = 5_000;

    /// <summary>If a node's queue depth exceeds this, skip it during selection.</summary>
    private const int QueueFallbackThreshold = 2;

    /// <summary>Assumed VRAM capacity per node in MB -- used when health cache is cold.</summary>
    private const int DefaultVramMb = 8_192;

    private readonly INodeRegistry _registry;
    private readonly INodeHealthCache? _healthCache;
    private readonly IMetricsCollector? _metrics;
    private readonly IPromptHistory? _history;
    private readonly ILogger<RoutingService> _logger;
    private readonly RoutingOptions _routingOptions;

    /// <summary>Per-node queues created lazily by the factory; keyed by NodeId.</summary>
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

    // Compatibility constructor for existing callers/tests that used the older
    // RoutingService signature (nodeA, nodeAQueue, logger, nodeB?, nodeBQueue?, ...)
    // This wraps the provided nodes into a lightweight INodeRegistry and adapts
    // the queue factory to return the supplied per-node queues.
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
               logger,
               healthCache,
               metrics,
               history,
               routingOptions)
    {
    }

    private static IInferenceQueue CreateQueueForNode(string nodeId, string aId, IInferenceQueue aQ, string? bId, IInferenceQueue? bQ, string? cId, IInferenceQueue? cQ)
    {
        if (nodeId == aId) return aQ;
        if (bId is not null && nodeId == bId && bQ is not null) return bQ;
        if (cId is not null && nodeId == cId && cQ is not null) return cQ;
        // default fallback: small queue
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

    public async Task<InferenceResult> RouteAsync(
        TaskType taskType,
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var registrations = _registry.GetAllNodes();
        if (registrations.Count == 0)
            throw new InvalidOperationException("No nodes are registered in the topology.");

        var target = SelectNode(registrations, taskType, request);
        _logger.LogInformation(
            "Routing taskType={TaskType} -> node={NodeId}",
            taskType, target.NodeId);

        var item = new InferenceQueueItem
        {
            Request = request,
            TaskType = taskType
        };

        var queue = GetQueue(target.Node.NodeId);
        if (!queue.TryEnqueue(item))
        {
            _logger.LogWarning("Queue full on node={NodeId}, trying fallback", target.Node.NodeId);

            var fallback = registrations
                .Where(r => r.Node.NodeId != target.Node.NodeId)
                .OrderByDescending(r => ComputeScore(r, taskType, request))
                .FirstOrDefault();

            if (fallback is not null && GetQueue(fallback.Node.NodeId).TryEnqueue(item))
            {
                target = fallback;
            }
            else
            {
                _logger.LogError("All node queues full — executing inline on {NodeId}", target.Node.NodeId);
                return await target.Node.ExecuteAsync(request, cancellationToken);
            }
        }

        _ = DrainAsync(target, registrations, item, cancellationToken);

        var historyId = _history?.Add(request.Prompt, taskType, target.Node.NodeId);
        var result = await item.Completion.Task.WaitAsync(cancellationToken);

        if (historyId is not null)
            _history!.Complete(historyId, result.Text, success: true);

        return result;
    }

    // Backwards-compatible SelectNode overload that returns the IInferenceNode
    // (previous public API). Useful for existing tests.
    public IInferenceNode SelectNode(TaskType taskType, InferenceRequest request)
    {
        var regs = _registry.GetAllNodes();
        var chosen = SelectNode(regs, taskType, request);
        return chosen.Node;
    }

    // ---------------------------------------------------------------------------
    // Node selection: hard rules §6.3 first, then §6.2 scoring
    // ---------------------------------------------------------------------------

    internal NodeRegistration SelectNode(
        IReadOnlyList<NodeRegistration> registrations,
        TaskType taskType,
        InferenceRequest request)
    {
        var available = registrations
            .Where(r => !IsUnavailable(r.Node.NodeId))
            .ToList();

        if (available.Count == 0)
            available = [.. registrations]; // all degraded — try anyway

        // Hard rule 1: Autocomplete forces a Fast node (latency-sensitive)
        if (taskType == TaskType.Autocomplete)
        {
            return available.FirstOrDefault(r => r.Config.Role == NodeRole.Fast)
                ?? available[0];
        }

        // Hard rule 2: Skip nodes whose queue is over the threshold
        var notOverloaded = available
            .Where(r => GetQueue(r.Node.NodeId).Count <= QueueFallbackThreshold)
            .ToList();

        if (notOverloaded.Count == 0)
            notOverloaded = available;

        // §6.2 scoring — pick highest-scoring node
        return notOverloaded
            .OrderByDescending(r => ComputeScore(r, taskType, request))
            .First();
    }

    // ---------------------------------------------------------------------------
    // §6.2 Scoring function
    //
    //   score = (0.35 * availableVramRatio)
    //         + (0.25 * (1 / (queueDepth + 1)))
    //         + (0.20 * roleFitScore)
    //         + (0.10 * latencyPenalty)
    //         + (0.10 * contextFitScore)
    // ---------------------------------------------------------------------------

    private double ComputeScore(NodeRegistration registration, TaskType taskType, InferenceRequest request)
    {
        var node = registration.Node;
        var role = registration.Config.Role;
        var health = _healthCache?.Get(node.NodeId);

        // Cloud/Copilot nodes have no local VRAM — treat as 75% available (slight cost penalty vs local)
        double vramRatio;
        if (registration.Config.Provider == NodeProviderType.CopilotSdk)
        {
            vramRatio = 0.75;
        }
        else
        {
            var totalVram = node.Capabilities?.VramMb > 0 ? node.Capabilities.VramMb : DefaultVramMb;
            var availableVram = health?.AvailableVramMb ?? totalVram;
            vramRatio = Math.Clamp((double)availableVram / totalVram, 0.0, 1.0);
        }

        var depth = GetQueue(node.NodeId).Count;
        var queueFactor = 1.0 / (depth + 1.0);

        // Role fit: Deep/Hybrid nodes excel at reasoning; Fast at latency-sensitive tasks
        var roleFit = (role, taskType) switch
        {
            (NodeRole.Fast, TaskType.Autocomplete or TaskType.Chat) => 1.0,
            (NodeRole.Fast, _) => 0.4,
            (NodeRole.Deep, TaskType.Review or TaskType.Refactor or TaskType.TestGeneration or TaskType.AgentStep) => 1.0,
            (NodeRole.Deep, TaskType.Chat) => 0.6,
            (NodeRole.Deep, _) => 0.5,
            (NodeRole.Hybrid, TaskType.Review or TaskType.Refactor or TaskType.TestGeneration or TaskType.AgentStep) => 0.9,
            (NodeRole.Hybrid, _) => 0.7,
            (NodeRole.Standby, _) => 0.1,
            _ => 0.5
        };

        var latencyMs = health?.Status == NodeStatus.Degraded ? 8_000 : 500;
        var latencyPenalty = 1.0 - Math.Clamp(latencyMs / 10_000.0, 0.0, 1.0);

        var tokens = EstimateTokens(request.Prompt);
        var contextFit = role is NodeRole.Deep or NodeRole.Hybrid
            ? Math.Clamp(tokens / (double)LargeContextTokenThreshold, 0.0, 1.0)
            : 1.0 - Math.Clamp(tokens / (double)LargeContextTokenThreshold, 0.0, 1.0);

        return (0.35 * vramRatio)
             + (0.25 * queueFactor)
             + (0.20 * roleFit)
             + (0.10 * latencyPenalty)
             + (0.10 * contextFit);
    }

    private IInferenceQueue GetQueue(string nodeId) =>
        _queues.GetOrAdd(nodeId, id => _queueFactory(id));

    private bool IsUnavailable(string nodeId) =>
        _healthCache?.Get(nodeId)?.Status == NodeStatus.Unavailable;

    private Task DrainAsync(
        NodeRegistration target,
        IReadOnlyList<NodeRegistration> allRegistrations,
        InferenceQueueItem item,
        CancellationToken ct)
    {
        if (item.Completion.Task.IsCompleted)
            return Task.CompletedTask;

        return Task.Run(async () => await DrainWithFallbackAsync(target, allRegistrations, item, ct), ct);
    }

    private async Task DrainWithFallbackAsync(
        NodeRegistration primary,
        IReadOnlyList<NodeRegistration> allRegistrations,
        InferenceQueueItem item,
        CancellationToken ct)
    {
        var chain = new List<IInferenceNode> { primary.Node };
        if (_routingOptions.FallbackChains.TryGetValue(primary.Node.NodeId, out var fallbackIds))
        {
            foreach (var id in fallbackIds)
            {
                var fallbackReg = allRegistrations.FirstOrDefault(r => r.Node.NodeId == id);
                if (fallbackReg is not null)
                    chain.Add(fallbackReg.Node);
            }
        }

        Exception? lastEx = null;
        foreach (var candidate in chain)
        {
            if (item.Completion.Task.IsCompleted) return;
            try
            {
                var result = await candidate.ExecuteAsync(item.Request, ct);
                item.Completion.TrySetResult(result);

                _metrics?.Record(new RequestMetric
                {
                    TaskId    = item.TaskId,
                    NodeId    = result.NodeId,
                    Model     = result.Model,
                    TaskType  = item.TaskType.ToString(),
                    TokensIn  = result.TokensIn,
                    TokensOut = result.TokensOut,
                    LatencyMs = result.LatencyMs,
                    Success   = true
                });
                return;
            }
            catch (OperationCanceledException)
            {
                item.Completion.TrySetCanceled(ct);
                return;
            }
            catch (Exception ex) when (candidate != chain[^1])
            {
                lastEx = ex;
                var reason = IsConnectivityException(ex) ? "unreachable" : "failed";
                _logger.LogWarning(ex,
                    "Node {NodeId} {Reason} -- trying next fallback", candidate.NodeId, reason);

                _metrics?.Record(new RequestMetric
                {
                    TaskId    = item.TaskId,
                    NodeId    = candidate.NodeId,
                    Model     = candidate.Capabilities.Model,
                    TaskType  = item.TaskType.ToString(),
                    LatencyMs = 0,
                    Success   = false
                });
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _metrics?.Record(new RequestMetric
                {
                    TaskId    = item.TaskId,
                    NodeId    = candidate.NodeId,
                    Model     = candidate.Capabilities.Model,
                    TaskType  = item.TaskType.ToString(),
                    LatencyMs = 0,
                    Success   = false
                });
                item.Completion.TrySetException(ex);
                return;
            }
        }

        if (lastEx is not null)
            item.Completion.TrySetException(lastEx);
    }

    /// <summary>
    /// Returns true for network-level connectivity failures (DNS, TCP refused/timeout)
    /// that warrant trying the next node in the fallback chain.
    /// </summary>
    private static bool IsConnectivityException(Exception ex)
    {
        if (ex is System.Net.Http.HttpRequestException httpEx)
        {
            if (httpEx.InnerException is System.Net.Sockets.SocketException)
                return true;
            if (httpEx.InnerException is TaskCanceledException)
                return true;
        }
        return false;
    }
}
