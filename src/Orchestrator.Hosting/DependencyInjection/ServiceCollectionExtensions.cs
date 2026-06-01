using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeClient.Copilot;
using NodeClient.Ollama;
using NodeClient.Worker;
using Orchestrator.Agents.SemanticKernel;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.History;
using Orchestrator.Infrastructure.Metrics;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Registry;
using Orchestrator.Infrastructure.Routing;
using Polly;
using Polly.Timeout;

namespace Orchestrator.Hosting.DependencyInjection;

/// <summary>
/// Registers the shared SplitBrain infrastructure services used by both the
/// MCP server host and the Blazor Dashboard host.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all shared SplitBrain infrastructure services: options binding,
    /// resilient HTTP clients, inference node singletons, queues, routing,
    /// model registry, node registry, and health-check background service.
    /// <para>
    /// Host-specific services (Serilog sinks, MCP tools, Blazor/SignalR,
    /// OpenTelemetry resource names, publishers) must still be registered
    /// by each host's Program.cs.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSplitBrainInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---------------------------------------------------------------
        // Options binding
        // ---------------------------------------------------------------
        services.Configure<OllamaClientOptions>(
            configuration.GetSection(OllamaClientOptions.Section));

        services.Configure<OllamaClientOptions>("NodeB",
            configuration.GetSection("OllamaNodeB"));

        services.Configure<CopilotClientOptions>(
            configuration.GetSection(CopilotClientOptions.Section));

        services.Configure<RoutingOptions>(
            configuration.GetSection(RoutingOptions.Section));

        services.Configure<NodeTopologyConfig>(
            configuration.GetSection("NodeTopology"));

        // ---------------------------------------------------------------
        // Base typed HttpClient for Node A (default Ollama)
        // ---------------------------------------------------------------
        services.AddHttpClient<IOllamaClient, OllamaClient>();

        // ---------------------------------------------------------------
        // Resilient named HttpClients per Ollama topology node
        // ---------------------------------------------------------------
        var topologyConfig = configuration
            .GetSection("NodeTopology")
            .Get<NodeTopologyConfig>() ?? new NodeTopologyConfig();

        foreach (var node in topologyConfig.Nodes
            .Where(n => n.Provider == NodeProviderType.Ollama && n.Ollama is not null))
        {
            var ollamaConfig = node.Ollama!;
            services
                .AddHttpClient($"ollama-{node.NodeId}", client =>
                {
                    client.BaseAddress = new Uri(ollamaConfig.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(ollamaConfig.TimeoutSeconds * 2);
                })
                .AddResilienceHandler($"resilience-{node.NodeId}", pipeline =>
                {
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromMilliseconds(500),
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                            .HandleResult(r => !r.IsSuccessStatusCode)
                            .Handle<HttpRequestException>()
                            .Handle<TimeoutRejectedException>()
                    });
                    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.8,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        MinimumThroughput = 10,
                        BreakDuration = TimeSpan.FromSeconds(30),
                        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                            .HandleResult(r => !r.IsSuccessStatusCode)
                            .Handle<HttpRequestException>()
                            .Handle<TimeoutRejectedException>()
                    });
                    pipeline.AddTimeout(TimeSpan.FromSeconds(ollamaConfig.TimeoutSeconds));
                });
        }

        // ---------------------------------------------------------------
        // Worker node HttpClients + WorkerInferenceNode singletons
        // ---------------------------------------------------------------
        foreach (var workerNode in topologyConfig.Nodes
            .Where(n => n.Provider == NodeProviderType.Worker && n.Worker is not null))
        {
            var wc = workerNode.Worker!;
            services
                .AddHttpClient($"worker-{workerNode.NodeId}", client =>
                {
                    client.BaseAddress = new Uri(wc.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(wc.TimeoutSeconds * 2);
                });

            var capturedNode = workerNode;
            services.AddKeyedSingleton<WorkerInferenceNode>(capturedNode.NodeId, (sp, _) =>
            {
                var wConfig = capturedNode.Worker!;
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpFactory.CreateClient($"worker-{capturedNode.NodeId}");
                var workerOptions = Options.Create(new WorkerClientOptions
                {
                    BaseUrl = wConfig.BaseUrl,
                    TimeoutSeconds = wConfig.TimeoutSeconds
                });
                var client = new WorkerClient(httpClient, workerOptions);
                var logger = sp.GetRequiredService<ILogger<WorkerInferenceNode>>();
                return new WorkerInferenceNode(capturedNode.NodeId, wConfig, client, logger);
            });
        }

        // ---------------------------------------------------------------
        // Inference node singletons
        // ---------------------------------------------------------------

        // Node A
        services.AddSingleton<NodeAInferenceNode>();
        services.AddSingleton<IInferenceNode>(sp => sp.GetRequiredService<NodeAInferenceNode>());

        // Node B
        services.AddSingleton<NodeBInferenceNode>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<OllamaClientOptions>>();
            var nodeBOptions = optionsMonitor.Get("NodeB");
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpFactory.CreateClient();
            httpClient.BaseAddress = new Uri(nodeBOptions.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(nodeBOptions.TimeoutSeconds);
            var ollamaClient = new OllamaClient(httpClient, Options.Create(nodeBOptions));
            var logger = sp.GetRequiredService<ILogger<NodeBInferenceNode>>();
            return new NodeBInferenceNode(ollamaClient, logger);
        });

        // Node C — GitHub Copilot API
        // Defer async initialization to first access on the thread pool,
        // avoiding SynchronizationContext deadlocks during DI registration.
        services.AddSingleton(sp =>
        {
            var copilotOptions = sp.GetRequiredService<IOptions<CopilotClientOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<NodeCInferenceNode>>();
            return new Lazy<NodeCInferenceNode>(() =>
                Task.Run(() => NodeCInferenceNode.CreateAsync(copilotOptions, logger)).GetAwaiter().GetResult());
        });
        // Unwrap so consumers that inject NodeCInferenceNode directly still work.
        services.AddSingleton(sp => sp.GetRequiredService<Lazy<NodeCInferenceNode>>().Value);

        // ---------------------------------------------------------------
        // Queues
        // ---------------------------------------------------------------
        services.AddKeyedSingleton<IInferenceQueue>("nodeA", (_, _) => new NodeQueue(capacity: 64));
        services.AddKeyedSingleton<IInferenceQueue>("nodeB", (_, _) => new NodeQueue(capacity: 32));
        services.AddKeyedSingleton<IInferenceQueue>("nodeC", (_, _) => new NodeQueue(capacity: 32));

        // ---------------------------------------------------------------
        // Infrastructure services
        // ---------------------------------------------------------------
        services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
        services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
        services.AddSingleton<IPromptHistory, PromptHistoryService>();

        // ---------------------------------------------------------------
        // Routing
        // ---------------------------------------------------------------
        services.AddSingleton<IRoutingService>(sp => new RoutingService(
            nodeA: sp.GetRequiredService<IInferenceNode>(),
            nodeAQueue: sp.GetRequiredKeyedService<IInferenceQueue>("nodeA"),
            logger: sp.GetRequiredService<ILogger<RoutingService>>(),
            nodeB: sp.GetRequiredService<NodeBInferenceNode>(),
            nodeBQueue: sp.GetRequiredKeyedService<IInferenceQueue>("nodeB"),
            healthCache: sp.GetRequiredService<INodeHealthCache>(),
            metrics: sp.GetRequiredService<IMetricsCollector>(),
            history: sp.GetRequiredService<IPromptHistory>(),
            nodeC: sp.GetRequiredService<NodeCInferenceNode>(),
            nodeCQueue: sp.GetRequiredKeyedService<IInferenceQueue>("nodeC"),
            routingOptions: sp.GetRequiredService<IOptions<RoutingOptions>>()));

        // ---------------------------------------------------------------
        // Model registry
        // ---------------------------------------------------------------
        services.AddSingleton<IModelRegistry>(sp =>
        {
            var registry = new InMemoryModelRegistry();
            var config = sp.GetRequiredService<IConfiguration>();
            var models = config.GetSection("SplitBrain:Models").Get<List<ModelDefinition>>() ?? [];
            foreach (var m in models)
                registry.RegisterModel(m);
            return registry;
        });

        // ---------------------------------------------------------------
        // Node registry + health check
        // ---------------------------------------------------------------
        services.AddSingleton<IInferenceNodeFactory, InferenceNodeFactory>();

        services.AddSingleton<Func<NodeConfiguration, IInferenceNode>>(sp => config =>
            config.NodeId switch
            {
                "A" => sp.GetRequiredService<NodeAInferenceNode>(),
                "B" => sp.GetRequiredService<NodeBInferenceNode>(),
                "C" => sp.GetRequiredService<NodeCInferenceNode>(),
                _ when config.Provider == NodeProviderType.Worker =>
                    sp.GetRequiredKeyedService<WorkerInferenceNode>(config.NodeId),
                _ => throw new InvalidOperationException(
                    $"No IInferenceNode registered for NodeId '{config.NodeId}'.")
            });

        services.AddSingleton<INodeRegistry, NodeRegistry>();
        services.AddHostedService<NodeHealthCheckService>();

        // ---------------------------------------------------------------
        // Agent orchestration
        // ---------------------------------------------------------------
        services.AddSingleton<IKernelPlannerService, KernelPlannerService>();

        return services;
    }
}
