using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Infrastructure.Registry;

/// <summary>
/// Background service that polls each enabled node at its configured health check interval.
/// Uses per-node SemaphoreSlim(1,1) to prevent concurrent health checks on the same node.
/// Results are pushed to INodeRegistry and, if present, to INodeHealthPublisher (SignalR dashboard).
/// </summary>
public sealed class NodeHealthCheckService : BackgroundService
{
    private readonly INodeRegistry _registry;
    private readonly INodeHealthPublisher _publisher;
    private readonly ILogger<NodeHealthCheckService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public NodeHealthCheckService(
        INodeRegistry registry,
        INodeHealthPublisher publisher,
        ILogger<NodeHealthCheckService> logger)
    {
        _registry = registry;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NodeHealthCheckService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nodes = _registry.GetAllNodes();
            var tasks = nodes.Select(n => CheckNodeAsync(n, stoppingToken));
            await Task.WhenAll(tasks);

            // Sleep for the minimum interval across all nodes (guarded against empty collection)
            var minInterval = nodes
                .Select(n => n.Config.HealthCheckIntervalMs)
                .DefaultIfEmpty(2000)
                .Min();

            await Task.Delay(minInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("NodeHealthCheckService stopped");
    }

    private async Task CheckNodeAsync(NodeRegistration registration, CancellationToken ct)
    {
        var sem = _semaphores.GetOrAdd(registration.Config.NodeId, _ => new SemaphoreSlim(1, 1));

        // Non-blocking: skip if already checking this node
        if (!await sem.WaitAsync(0, ct))
            return;

        try
        {
            var health = await registration.Node.GetHealthAsync(ct);
            _registry.UpdateNodeHealth(registration.Config.NodeId, health);

            await _publisher.PublishAsync(
                health,
                registration.Config.NodeId,
                registration.Config.DisplayName,
                ct);

            _logger.LogDebug(
                "Health check node={NodeId} state={State} latencyMs={Latency}",
                registration.Config.NodeId, health.State, health.LatencyMs);
        }
        catch (OperationCanceledException)
        {
            // Shutting down — no need to log
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for node {NodeId}", registration.Config.NodeId);
        }
        finally
        {
            sem.Release();
        }
    }
}
