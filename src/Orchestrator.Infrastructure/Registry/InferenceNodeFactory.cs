using Microsoft.Extensions.Logging;
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
    private readonly ILogger<InferenceNodeFactory> _logger;

    public InferenceNodeFactory(IServiceProvider services, ILogger<InferenceNodeFactory> logger)
    {
        _services = services;
        _logger = logger;
    }

    public IInferenceNode Create(NodeConfiguration config)
    {
        // Validate provider-specific config sections are present
        ValidateProviderConfig(config);

        var factory = (Func<NodeConfiguration, IInferenceNode>?)_services.GetService(
            typeof(Func<NodeConfiguration, IInferenceNode>));

        if (factory is not null)
            return factory(config);

        _logger.LogWarning(
            "No Func<NodeConfiguration, IInferenceNode> registered for {Provider} node '{NodeId}'. " +
            "Register a factory in DI.",
            config.Provider, config.NodeId);

        throw new InvalidOperationException(
            $"Cannot create {config.Provider} node '{config.NodeId}' — no provider factory registered. " +
            $"Register a Func<NodeConfiguration, IInferenceNode> in DI that handles {config.Provider} nodes.");
    }

    private static void ValidateProviderConfig(NodeConfiguration config)
    {
        switch (config.Provider)
        {
        case NodeProviderType.Ollama:
            _ = config.Ollama
                ?? throw new InvalidOperationException(
                    $"Node '{config.NodeId}' has Provider=Ollama but no Ollama config section.");
            break;
                _ = config.Worker
                    ?? throw new InvalidOperationException(
                        $"Node '{config.NodeId}' has Provider=Worker but no Worker config section.");
                break;

            case NodeProviderType.CopilotSdk:
                // Copilot SDK has no required config section — token resolution happens at runtime
                break;

            default:
                throw new NotSupportedException(
                    $"Provider '{config.Provider}' is not registered. " +
                    $"Implement IInferenceNode and add a case to InferenceNodeFactory.");
        }
    }

    private IInferenceNode CreateLlamaCppNode(NodeConfiguration config)
    {
        _ = config.LlamaCpp
            ?? throw new InvalidOperationException(
                $"Node '{config.NodeId}' has Provider=LlamaCpp but no LlamaCpp config section.");

        var factory = (Func<NodeConfiguration, IInferenceNode>?)_services.GetService(
            typeof(Func<NodeConfiguration, IInferenceNode>));

        if (factory is not null)
            return factory(config);

        throw new InvalidOperationException(
            $"Cannot create LlamaCpp node '{config.NodeId}' — no provider factory registered. " +
            $"Register a Func<NodeConfiguration, IInferenceNode> in DI that handles LlamaCpp nodes, " +
            $"or wire NodeClient.LlamaCpp directly in Program.cs for NodeWorker deployments.");
    }
}
