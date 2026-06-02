namespace Orchestrator.Core.Interfaces;

public interface IRoutingOptionsPersistence
{
    Task SaveFallbackChainsAsync(IReadOnlyDictionary<string, List<string>> fallbackChains, CancellationToken cancellationToken = default);
}
