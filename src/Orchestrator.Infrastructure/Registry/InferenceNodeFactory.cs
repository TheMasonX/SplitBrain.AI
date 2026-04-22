using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Infrastructure.Registry;

/// <summary>
/// Creates IInferenceNode instances from NodeConfiguration.
/// Provider-specific construction is delegated to provider factories registered in DI.
/// </summary>
public sealed class InferenceNodeFactory : IInferenceNodeFactory
{
    private readonly IServiceProvider _services;

    public InferenceNodeFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IInferenceNode Create(NodeConfiguration config)
    {
        return config.Provider switch
        {
            NodeProviderType.Ollama => CreateOllamaNode(config),
            NodeProviderType.CopilotSdk => CreateCopilotNode(config),
            NodeProviderType.Worker => CreateWorkerNode(config),
            _ => throw new NotSupportedException(
                $"Provider '{config.Provider}' is not registered. " +
                $"Implement IInferenceNode and add a case to InferenceNodeFactory.")
        };
    }

    private IInferenceNode CreateOllamaNode(NodeConfiguration config)
    {
        var ollamaConfig = config.Ollama
            ?? throw new InvalidOperationException(
                $"Node '{config.NodeId}' has Provider=Ollama but no Ollama config section.");

        var factory = (Func<NodeConfiguration, IInferenceNode>?)_services.GetService(
            typeof(Func<NodeConfiguration, IInferenceNode>));

        if (factory is not null)
            return factory(config);

        // Fallback: try to resolve a named provider from DI
        var logger = (ILogger<InferenceNodeFactory>)_services.GetService(
            typeof(ILogger<InferenceNodeFactory>))!;
        logger.LogWarning(
            "No Func<NodeConfiguration, IInferenceNode> registered for Ollama node '{NodeId}'. " +
            "Register a factory in DI or use OllamaInferenceNode directly.",
            config.NodeId);

        throw new InvalidOperationException(
            $"Cannot create Ollama node '{config.NodeId}' — no provider factory registered.");
    }

    private IInferenceNode CreateCopilotNode(NodeConfiguration config)
    {
        var factory = (Func<NodeConfiguration, IInferenceNode>?)_services.GetService(
            typeof(Func<NodeConfiguration, IInferenceNode>));

        if (factory is not null)
            return factory(config);

        throw new InvalidOperationException(
            $"Cannot create Copilot node '{config.NodeId}' — no provider factory registered.");
    }

    private IInferenceNode CreateWorkerNode(NodeConfiguration config)
    {
        _ = config.Worker
            ?? throw new InvalidOperationException(
                $"Node '{config.NodeId}' has Provider=Worker but no Worker config section.");

        var factory = (Func<NodeConfiguration, IInferenceNode>?)_services.GetService(
            typeof(Func<NodeConfiguration, IInferenceNode>));

        if (factory is not null)
            return factory(config);

        throw new InvalidOperationException(
            $"Cannot create Worker node '{config.NodeId}' — no provider factory registered. " +
            $"Register a Func<NodeConfiguration, IInferenceNode> in DI that handles Worker nodes.");
    }
}
