using System.Runtime.CompilerServices;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Core;

/// <summary>
/// Abstract base class for IInferenceNode implementations.
/// Consolidates the duplicated patterns (StreamAsync wrapper, GetHealthAsync,
/// volatile _health field, Capabilities/Health property, DisposeAsync).
/// Concrete nodes override only what differs.
/// </summary>
public abstract class InferenceNodeBase : IInferenceNode
{
    public abstract string NodeId { get; }
    public abstract NodeProviderType Provider { get; }
    public NodeHealthStatus Health => _health;
    public abstract NodeCapabilities Capabilities { get; }

    protected volatile NodeHealthStatus _health = new()
    {
        State = HealthState.Unavailable,
        LastChecked = DateTimeOffset.MinValue
    };

    public abstract Task<InferenceResult> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Default streaming implementation: executes the request and yields a single final chunk.
    /// Override in subclasses that support true token-by-token streaming.
    /// </summary>
    public virtual async IAsyncEnumerable<InferenceChunk> StreamAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(request, cancellationToken);
        yield return new InferenceChunk
        {
            Content = result.Text,
            IsFinal = true,
            FinalResult = result
        };
    }

    /// <summary>
    /// Default health check: calls <see cref="CheckHealthCoreAsync"/> and updates _health.
    /// Override entirely if the node has a non-standard health-check flow (e.g. WorkerInferenceNode).
    /// </summary>
    public virtual async Task<NodeHealthStatus> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        NodeHealthStatus status;
        try
        {
            var isHealthy = await CheckHealthCoreAsync(cancellationToken);
            status = new NodeHealthStatus
            {
                State = isHealthy ? HealthState.Healthy : HealthState.Degraded,
                LastChecked = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            status = new NodeHealthStatus
            {
                State = HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow
            };
        }
        _health = status;
        return status;
    }

    /// <summary>
    /// Override to call the provider-specific health endpoint.
    /// Return true for healthy, false for degraded.
    /// </summary>
    protected abstract Task<bool> CheckHealthCoreAsync(CancellationToken cancellationToken);

    public abstract Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
