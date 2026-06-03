using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Configuration;

/// <summary>
/// Coordinates routing and topology persistence to reduce cross-file inconsistency.
/// </summary>
public sealed class TopologyRoutingSaveCoordinator
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly IRoutingOptionsPersistence _routingPersistence;
    private readonly IOptionsMonitor<RoutingOptions> _routingOptionsMonitor;

    public TopologyRoutingSaveCoordinator(
        INodeRegistry nodeRegistry,
        IRoutingOptionsPersistence routingPersistence,
        IOptionsMonitor<RoutingOptions> routingOptionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(nodeRegistry);
        ArgumentNullException.ThrowIfNull(routingPersistence);
        ArgumentNullException.ThrowIfNull(routingOptionsMonitor);

        _nodeRegistry = nodeRegistry;
        _routingPersistence = routingPersistence;
        _routingOptionsMonitor = routingOptionsMonitor;
    }

    /// <summary>
    /// Persists fallback chains and topology as one coordinated operation.
    /// If topology persistence fails, routing is rolled back to the previous configuration snapshot.
    /// </summary>
    public async Task SaveAsync(
        IReadOnlyDictionary<string, List<string>> fallbackChains,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fallbackChains);

        ValidateFallbackChains(fallbackChains);

        var previousChains = Clone(_routingOptionsMonitor.CurrentValue.FallbackChains);

        await _routingPersistence.SaveFallbackChainsAsync(fallbackChains, cancellationToken).ConfigureAwait(false);

        try
        {
            await _nodeRegistry.SaveTopologyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _routingPersistence.SaveFallbackChainsAsync(previousChains, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (IOException ex)
        {
            await RollbackOrThrowAsync(previousChains, ex).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            await RollbackOrThrowAsync(previousChains, ex).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await RollbackOrThrowAsync(previousChains, ex).ConfigureAwait(false);
        }
    }

    private void ValidateFallbackChains(IReadOnlyDictionary<string, List<string>> fallbackChains)
    {
        var knownNodeIds = _nodeRegistry.GetAllNodes()
            .Select(n => n.Config.NodeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (source, chain) in fallbackChains)
        {
            if (!knownNodeIds.Contains(source))
            {
                throw new InvalidOperationException($"Fallback chain source '{source}' is not a registered node.");
            }

            foreach (var target in chain)
            {
                if (!knownNodeIds.Contains(target))
                {
                    throw new InvalidOperationException($"Fallback chain target '{target}' is not a registered node.");
                }

                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Fallback chain for '{source}' cannot contain itself.");
                }
            }
        }

        if (FallbackGraphValidator.TryGetFirstCyclePath(fallbackChains, out var cyclePath))
        {
            throw new InvalidOperationException($"Fallback chains contain a cycle: {cyclePath}.");
        }
    }

    private async Task RollbackOrThrowAsync(
        IReadOnlyDictionary<string, List<string>> previousChains,
        Exception rootCause)
    {
        Exception? rollbackFailure = null;
        try
        {
            await _routingPersistence.SaveFallbackChainsAsync(previousChains, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException rollbackEx)
        {
            rollbackFailure = rollbackEx;
        }
        catch (UnauthorizedAccessException rollbackEx)
        {
            rollbackFailure = rollbackEx;
        }
        catch (InvalidOperationException rollbackEx)
        {
            rollbackFailure = rollbackEx;
        }

        if (rollbackFailure is null)
        {
            throw new InvalidOperationException(
                "Topology save failed after routing save. Routing changes were rolled back.",
                rootCause);
        }

        throw new InvalidOperationException(
            "Topology save failed and routing rollback failed.",
            new AggregateException(rootCause, rollbackFailure));
    }

    private static Dictionary<string, List<string>> Clone(IReadOnlyDictionary<string, List<string>> source)
    {
        var clone = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            clone[key] = value.ToList();
        }

        return clone;
    }
}
