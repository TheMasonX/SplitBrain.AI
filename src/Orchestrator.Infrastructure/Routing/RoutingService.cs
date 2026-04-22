using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Routing;

public sealed class RoutingService : IRoutingService
{
    /// <summary>Context token threshold above which Node B is preferred (§6.3).</summary>
    private const int LargeContextTokenThreshold = 5_000;

    /// <summary>If Node B queue depth exceeds this, fall back to Node A (§6.3).</summary>
    private const int NodeBQueueFallbackThreshold = 2;

    /// <summary>Assumed VRAM capacity per node in MB — used when health cache is cold.</summary>
    private const int DefaultVramMb = 8_192;

    private readonly IInferenceNode _nodeA;
    private readonly IInferenceNode? _nodeB;
    private readonly IInferenceNode? _nodeC;
    private readonly IInferenceQueue _nodeAQueue;
    private readonly IInferenceQueue? _nodeBQueue;
    private readonly IInferenceQueue? _nodeCQueue;
    private readonly INodeHealthCache? _healthCache;
    private readonly IMetricsCollector? _metrics;
    private readonly IPromptHistory? _history;
    private readonly ILogger<RoutingService> _logger;
    private readonly RoutingOptions _routingOptions;

    /// <summary>All registered nodes keyed by NodeId for fallback chain resolution.</summary>
    private readonly Dictionary<string, (IInferenceNode Node, IInferenceQueue Queue)> _nodeMap;

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
    {
        _nodeA = nodeA;
        _nodeB = nodeB;
        _nodeC = nodeC;
        _nodeAQueue = nodeAQueue;
        _nodeBQueue = nodeBQueue;
        _nodeCQueue = nodeCQueue;
        _healthCache = healthCache;
        _metrics = metrics;
        _history = history;
        _logger = logger;
        _routingOptions = routingOptions?.Value ?? new RoutingOptions();

        _nodeMap = new Dictionary<string, (IInferenceNode, IInferenceQueue)>
        {
            [nodeA.NodeId] = (nodeA, nodeAQueue)
        };
        if (nodeB is not null && nodeBQueue is not null)
            _nodeMap[nodeB.NodeId] = (nodeB, nodeBQueue);
        if (nodeC is not null && nodeCQueue is not null)
            _nodeMap[nodeC.NodeId] = (nodeC, nodeCQueue);
    }

    public async Task<InferenceResult> RouteAsync(
        TaskType taskType,
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var target = SelectNode(taskType, request);
        _logger.LogInformation(
            "Routing taskType={TaskType} → node={NodeId}",
            taskType, target.NodeId);

        var item = new InferenceQueueItem
        {
            Request = request,
            TaskType = taskType
        };

        var queue = target.NodeId switch
        {
            "B" => _nodeBQueue ?? _nodeAQueue,
            "C" => _nodeCQueue ?? _nodeAQueue,
            _   => _nodeAQueue
        };
        if (!queue.TryEnqueue(item))
        {
            _logger.LogWarning("Queue full on node={NodeId}, falling back to Node A", target.NodeId);
            target = _nodeA;
            if (!_nodeAQueue.TryEnqueue(item))
            {
                _logger.LogError("Node A queue also full — executing inline");
                return await _nodeA.ExecuteAsync(request, cancellationToken);
            }
        }

        _ = DrainAsync(target, item, cancellationToken);

        var historyId = _history?.Add(request.Prompt, taskType, target.NodeId);
        var result = await item.Completion.Task.WaitAsync(cancellationToken);

        if (historyId is not null)
            _history!.Complete(historyId, result.Text, success: true);

        return result;
    }

    // ---------------------------------------------------------------------------
    // Node selection: hard rules §6.3 first, then §6.2 scoring
    // ---------------------------------------------------------------------------

    internal IInferenceNode SelectNode(TaskType taskType, InferenceRequest request)
    {
        // Hard rule 1: Autocomplete always forces Node A (latency-sensitive)
        if (taskType == TaskType.Autocomplete)
            return _nodeA;

        // Hard rule 2: No Node B registered → Node A
        if (_nodeB is null)
            return _nodeA;

        // Hard rule 3: Node B queue depth > threshold → fall back to Node A
        if (_nodeBQueue is not null && _nodeBQueue.Count > NodeBQueueFallbackThreshold)
        {
            _logger.LogDebug("Node B queue depth {Depth} exceeds threshold — routing to Node A",
                _nodeBQueue.Count);
            return _nodeA;
        }

        // Hard rule 4: Node B reported Unavailable in health cache → fall back
        var healthB = _healthCache?.Get(_nodeB?.NodeId ?? "B");
        if (healthB?.Status == NodeStatus.Unavailable)
        {
            _logger.LogDebug("Node B is Unavailable (cached) — routing to Node A");
            return _nodeA;
        }

        // Scoring §6.2: pick the highest-scoring available node (A, B, C)
        var scoreA = ComputeScore(_nodeA, _nodeAQueue, taskType, request);
        var scoreB = _nodeB is not null ? ComputeScore(_nodeB, _nodeBQueue, taskType, request) : double.MinValue;
        var scoreC = _nodeC is not null ? ComputeScore(_nodeC, _nodeCQueue, taskType, request) : double.MinValue;

        _logger.LogDebug(
            "Node scores — A={ScoreA:F3} B={ScoreB:F3} C={ScoreC:F3} taskType={TaskType}",
            scoreA, scoreB, scoreC, taskType);

        if (scoreC >= scoreA && scoreC >= scoreB && _nodeC is not null)
            return _nodeC;

        return scoreB >= scoreA && _nodeB is not null ? _nodeB : _nodeA;
    }

    // ---------------------------------------------------------------------------
    // §6.2 Scoring function
    //
    //   score = (0.35 * availableVramRatio)
    //         + (0.25 * (1 / (queueDepth + 1)))   ← +1 prevents div-by-zero
    //         + (0.20 * modelFitScore)
    //         + (0.10 * latencyPenalty)
    //         + (0.10 * contextFitScore)
    // ---------------------------------------------------------------------------

    private double ComputeScore(
        IInferenceNode node,
        IInferenceQueue? queue,
        TaskType taskType,
        InferenceRequest request)
    {
        var health = _healthCache?.Get(node.NodeId);

        // availableVramRatio: how much VRAM headroom the node has (0–1)
        var totalVram = node.Capabilities?.VramMb > 0 ? node.Capabilities.VramMb : DefaultVramMb;
        var availableVram = health?.AvailableVramMb ?? totalVram;
        var vramRatio = Math.Clamp((double)availableVram / totalVram, 0.0, 1.0);

        // queueDepth factor: prefer shorter queues
        var depth = queue?.Count ?? 0;
        var queueFactor = 1.0 / (depth + 1.0);

        // modelFitScore: Node B/C are better for deep tasks; Node A for latency tasks; Node C always capable
        var modelFit = node.NodeId switch
        {
            "B" => taskType is TaskType.Review or TaskType.Refactor or TaskType.TestGeneration ? 1.0 : 0.4,
            "C" => taskType is TaskType.Review or TaskType.Refactor or TaskType.TestGeneration or TaskType.AgentStep ? 0.9 : 0.6,
            _   => taskType is TaskType.Autocomplete or TaskType.Chat ? 1.0 : 0.5
        };

        // latencyPenalty: penalise based on last observed latency (0 = fast, 1 = slow)
        // Use 10 000 ms as the "worst" reference point
        var latencyMs = health?.Status == NodeStatus.Degraded ? 8_000 : 500;
        var latencyPenalty = 1.0 - Math.Clamp(latencyMs / 10_000.0, 0.0, 1.0);

        // contextFitScore: Node B/C win on large contexts; Node A wins on small
        var tokens = EstimateTokens(request.Prompt);
        var contextFit = node.NodeId is "B" or "C"
            ? Math.Clamp(tokens / (double)LargeContextTokenThreshold, 0.0, 1.0)
            : 1.0 - Math.Clamp(tokens / (double)LargeContextTokenThreshold, 0.0, 1.0);

        // Node C (cloud) has no local VRAM — treat as fully available but apply a small
        // cost penalty to prefer local nodes when they are healthy.
        var adjustedVramRatio = node.NodeId == "C" ? 0.75 : vramRatio;

        return (0.35 * adjustedVramRatio)
             + (0.25 * queueFactor)
             + (0.20 * modelFit)
             + (0.10 * latencyPenalty)
             + (0.10 * contextFit);
    }

    private Task DrainAsync(IInferenceNode node, InferenceQueueItem item, CancellationToken ct)
    {
        if (item.Completion.Task.IsCompleted)
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            await DrainWithFallbackAsync(node, item, ct);
        }, ct);
    }

    private async Task DrainWithFallbackAsync(IInferenceNode node, InferenceQueueItem item, CancellationToken ct)
    {
        // Build the ordered list of nodes to try: primary + configured fallback chain
        var chain = new List<IInferenceNode> { node };
        if (_routingOptions.FallbackChains.TryGetValue(node.NodeId, out var fallbackIds))
        {
            foreach (var id in fallbackIds)
            {
                if (_nodeMap.TryGetValue(id, out var entry))
                    chain.Add(entry.Node);
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
                    "Node {NodeId} {Reason} — trying next fallback", candidate.NodeId, reason);

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
                // All nodes in the chain exhausted — propagate the last exception
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

        // All nodes in the chain were unreachable
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
            // Covers TaskCanceledException wrapping a timeout inside HttpRequestException
            if (httpEx.InnerException is TaskCanceledException)
                return true;
        }
        return false;
    }

    /// <summary>Rough token estimate: ~4 chars per token.</summary>
    private static int EstimateTokens(string text) => text.Length / 4;
}
