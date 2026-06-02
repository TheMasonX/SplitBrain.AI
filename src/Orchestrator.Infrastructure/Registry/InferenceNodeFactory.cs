using Microsoft.Extensions.Logging;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Infrastructure.Registry;

public sealed class InferenceNodeFactory : IInferenceNodeFactory
{
    private readonly IServiceProvider _services;
    public InferenceNodeFactory(IServiceProvider services) { _services = services; }

    public IInferenceNode Create(NodeConfiguration config)
    {
        return config.Provider switch
        {
            NodeProviderType.Ollama => CreateViaFactory(config),
            NodeProviderType.CopilotSdk => CreateViaFactory(config),
            NodeProviderType.Worker => CreateViaFactory(config),
            NodeProviderType.LlamaCpp => CreateViaFactory(config),
            _ => throw new NotSupportedException($"Provider '{config.Provider}' is not registered. Implement IInferenceNode and add a case to InferenceNodeFactory.")
        };
    }

    private IInferenceNode CreateViaFactory(NodeConfiguration config)
    {
        var factory = (Func<NodeConfiguration, IInferenceNode>?)_services.GetService(typeof(Func<NodeConfiguration, IInferenceNode>));
        if (factory is not null) return factory(config);
        var logger = (ILogger<InferenceNodeFactory>?)_services.GetService(typeof(ILogger<InferenceNodeFactory>));
        logger?.LogWarning("No Func<NodeConfiguration, IInferenceNode> registered for node '{NodeId}' provider '{Provider}'. Register a factory in DI.", config.NodeId, config.Provider);
        throw new InvalidOperationException($"Cannot create node '{config.NodeId}' provider '{config.Provider}' — no provider factory registered.");
    }
}
