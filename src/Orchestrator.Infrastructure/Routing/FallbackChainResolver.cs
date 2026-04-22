using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Routing;

/// <summary>
/// Resolves a <see cref="FallbackChainConfig"/> at runtime, executing each step in order
/// until one succeeds. Steps referencing unavailable nodes or models are silently skipped.
/// </summary>
public sealed class FallbackChainResolver
{
    private readonly INodeRegistry _registry;
    private readonly IModelRegistry _modelRegistry;
    private readonly ILogger<FallbackChainResolver> _logger;

    public FallbackChainResolver(
        INodeRegistry registry,
        IModelRegistry modelRegistry,
        ILogger<FallbackChainResolver> logger)
    {
        _registry      = registry;
        _modelRegistry = modelRegistry;
        _logger        = logger;
    }

    /// <summary>
    /// Executes the fallback chain for the given task type.
    /// Returns the first successful inference result, or throws if all steps fail.
    /// </summary>
    public async Task<InferenceResult> ExecuteAsync(
        TaskType taskType,
        InferenceRequest request,
        FallbackChainConfig chain,
        CancellationToken ct = default)
    {
        Exception? lastException = null;

        foreach (var step in chain.Steps)
        {
            ct.ThrowIfCancellationRequested();

            var node = ResolveNode(step);
            if (node is null)
            {
                _logger.LogDebug(
                    "FallbackChainResolver: skipping step for model '{Model}' — no healthy node found",
                    step.ModelId);
                continue;
            }

            var timeout = step.TimeoutOverrideMs > 0
                ? TimeSpan.FromMilliseconds(step.TimeoutOverrideMs)
                : TimeSpan.FromSeconds(60);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            try
            {
                _logger.LogDebug(
                    "FallbackChainResolver: trying model '{Model}' on node '{Node}'",
                    step.ModelId, node.NodeId);

                var stepRequest = request with { Model = step.ModelId };
                var result = await node.ExecuteAsync(stepRequest, linked.Token);

                _logger.LogDebug(
                    "FallbackChainResolver: model '{Model}' succeeded on node '{Node}'",
                    step.ModelId, node.NodeId);

                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "FallbackChainResolver: step for model '{Model}' timed out after {Ms}ms — trying next",
                    step.ModelId, step.TimeoutOverrideMs);
                lastException = new TimeoutException($"Step for model '{step.ModelId}' timed out.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "FallbackChainResolver: step for model '{Model}' failed — trying next",
                    step.ModelId);
                lastException = ex;
            }
        }

        throw new InvalidOperationException(
            $"All fallback steps for task '{taskType}' failed.",
            lastException);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private IInferenceNode? ResolveNode(FallbackStep step)
    {
        var allNodes = _registry.GetAllNodes();

        // Prefer explicitly listed nodes
        if (step.PreferredNodeIds is { Count: > 0 })
        {
            foreach (var nodeId in step.PreferredNodeIds)
            {
                var reg = allNodes.FirstOrDefault(n =>
                    string.Equals(n.Config.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

                if (reg is not null && IsNodeAvailable(reg, step.ModelId))
                    return reg.Node;
            }
        }

        // Fall back to any healthy node that has the model available
        foreach (var reg in allNodes)
        {
            if (IsNodeAvailable(reg, step.ModelId))
                return reg.Node;
        }

        return null;
    }

    private bool IsNodeAvailable(NodeRegistration reg, string modelId)
    {
        if (reg.LastHealth?.State != HealthState.Healthy)
            return false;

        var available = _modelRegistry.GetAvailableModels(reg.Config.NodeId);
        return available.Any(m => string.Equals(m, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
