using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.NodeWorker;

public sealed class NodeWorkerService : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);
    private const int MaxConsecutiveFailures = 5;

    private readonly IInferenceNode _node;
    private readonly INodeHealthCache _healthCache;
    private readonly ILogger<NodeWorkerService> _logger;

    private int _consecutiveFailures;

    public NodeStatus CurrentStatus { get; private set; } = NodeStatus.Unknown;

    public NodeWorkerService(IInferenceNode node, INodeHealthCache healthCache, ILogger<NodeWorkerService> logger)
    {
        _node = node;
        _healthCache = healthCache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NodeWorkerService started on node {NodeId}", _node.NodeId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nodeHealth = await _node.GetHealthAsync(stoppingToken);
                var legacyStatus = ToNodeStatus(nodeHealth.State);

                _healthCache.Set(new NodeHealth
                {
                    NodeId = _node.NodeId,
                    Status = legacyStatus,
                    QueueDepth = nodeHealth.ActiveRequests,
                    AvailableVramMb = nodeHealth.VramLoadedMB.HasValue && nodeHealth.VramTotalMB.HasValue
                        ? (int)(nodeHealth.VramTotalMB.Value - nodeHealth.VramLoadedMB.Value)
                        : 0,
                    CheckedAt = nodeHealth.LastChecked,
                    LastLatencyMs = nodeHealth.LatencyMs
                });

                if (legacyStatus != NodeStatus.Unavailable)
                {
                    if (_consecutiveFailures > 0) { _logger.LogInformation("Node {NodeId} recovered after {Failures} consecutive failure(s)", _node.NodeId, _consecutiveFailures); _consecutiveFailures = 0; }
                    CurrentStatus = legacyStatus;
                    _logger.LogInformation("Heartbeat node={NodeId} status={Status} latencyMs={Latency}", _node.NodeId, legacyStatus, nodeHealth.LatencyMs);
                }
                else { RecordFailure(); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Heartbeat failed on node {NodeId}", _node.NodeId);
                RecordFailure();
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }

        _logger.LogInformation("NodeWorkerService stopping on node {NodeId}", _node.NodeId);
    }

    private static NodeStatus ToNodeStatus(HealthState state) => state switch { HealthState.Healthy => NodeStatus.Healthy, HealthState.Degraded => NodeStatus.Degraded, _ => NodeStatus.Unavailable };

    private void RecordFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= MaxConsecutiveFailures) { CurrentStatus = NodeStatus.Unavailable; _logger.LogWarning("Node {NodeId} marked Unavailable after {Failures} consecutive failures", _node.NodeId, _consecutiveFailures); }
        else { CurrentStatus = NodeStatus.Degraded; _logger.LogWarning("Node {NodeId} degraded -- consecutive failures: {Failures}/{Max}", _node.NodeId, _consecutiveFailures, MaxConsecutiveFailures); }
    }
}
