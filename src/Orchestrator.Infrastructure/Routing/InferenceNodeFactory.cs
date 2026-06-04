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

    /// <summary>
    /// Returns the registered <see cref="IInferenceNode"/> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="IInferenceNode"/> has been registered.
    /// Ensure <c>AddSplitBrainInfrastructure</c> (or equivalent DI setup) was called.
    /// </exception>
    public IInferenceNode GetNode()
    {
        return _services.GetService<IInferenceNode>()
            ?? throw new InvalidOperationException(
                "No IInferenceNode factory registered. Ensure AddSplitBrainInfrastructure was called.");
    }

    /// <summary>
    /// Returns the registered <see cref="IInferenceNode"/> instance resolved via
    /// <paramref name="factory"/>, applying the same missing-registration guard.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the supplied factory delegate resolves to <c>null</c>.
    /// </exception>
    public IInferenceNode GetNode(Func<IServiceProvider, IInferenceNode?> factory)
    {
        return factory(_services)
            ?? throw new InvalidOperationException(
                "No IInferenceNode factory registered. Ensure AddSplitBrainInfrastructure was called.");
    }
}
