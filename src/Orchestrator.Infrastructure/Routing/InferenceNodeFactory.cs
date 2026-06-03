using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Infrastructure.Routing;

/// <summary>
/// Resolves <see cref="IInferenceNode"/> instances from the DI container
/// and enforces that at least one provider has been registered.
/// </summary>
public sealed class InferenceNodeFactory
{
    private readonly IServiceProvider _services;

    public InferenceNodeFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IInferenceNode GetNode()
    {
        return _services.GetService<IInferenceNode>()
            ?? throw new InvalidOperationException(
                "No IInferenceNode factory registered. Ensure AddSplitBrainInfrastructure was called.");
    }

    public IInferenceNode GetNode(Func<IServiceProvider, IInferenceNode?> factory)
    {
        return factory(_services)
            ?? throw new InvalidOperationException(
                "No IInferenceNode factory registered. Ensure AddSplitBrainInfrastructure was called.");
    }
}
