namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Persists routing fallback configuration.
/// </summary>
public interface IRoutingOptionsPersistence
{
    /// <summary>
    /// Saves fallback chains under the Routing configuration section.
    /// </summary>
    Task SaveFallbackChainsAsync(
        IReadOnlyDictionary<string, List<string>> fallbackChains,
        CancellationToken cancellationToken = default);
}
