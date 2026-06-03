using ApexCharts;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeClient.Copilot;
using NodeClient.Ollama;
using NodeClient.LlamaCpp;
using NodeClient.Worker;
using Orchestrator.Agents;
using Orchestrator.Agents.SemanticKernel;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Configuration;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.History;
using Orchestrator.Infrastructure.Metrics;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Registry;
using Orchestrator.Infrastructure.Routing;
using Polly;
using Polly.Timeout;
using Serilog;
using Serilog.Events;
using SplitBrain.Dashboard.Components;
using SplitBrain.Dashboard.Hubs;
using SplitBrain.Dashboard.Logging;
using SplitBrain.Dashboard.Services;

namespace SplitBrain.Dashboard
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddSignalR();
            builder.Services.AddApexCharts();

            // Dashboard services
            builder.Services.AddSingleton<DashboardState>();
            builder.Services.AddSingleton<INodeHealthPublisher, SignalRNodeHealthPublisher>();
            builder.Services.AddSingleton<ILogEntryPublisher, SignalRLogEntryPublisher>();
            builder.Services.AddSingleton<SignalRDashboardPublisher>();

            // Wire Serilog — pass IServiceProvider so the sink resolves ILogEntryPublisher
            // lazily on first Emit, avoiding re-entrant DI resolution during bootstrap.
            builder.Host.UseSerilog((ctx, sp, cfg) =>
            {
                cfg.MinimumLevel.Debug()
                   .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                   .WriteTo.Console()
                   .WriteTo.Sink(new SignalRLogSink(sp));
            });

            // OpenTelemetry — must be registered BEFORE builder.Build()
            var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
            builder.Services
                .AddOpenTelemetry()
                .ConfigureResource(r => r.AddService("SplitBrain.Dashboard", serviceVersion: "3.0.0"))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

            // Node topology (hot-reload from nodes.json)
            builder.Configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "nodes.json"),
                optional: true, reloadOnChange: true);
            builder.Configuration.AddJsonFile("nodes.json", optional: true, reloadOnChange: true);

            // Routing options (hot-reload from routing.json)
            builder.Configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "routing.json"),
                optional: true, reloadOnChange: true);
            builder.Configuration.AddJsonFile("routing.json", optional: true, reloadOnChange: true);

            builder.Services.Configure<NodeTopologyConfig>(
                builder.Configuration.GetSection("NodeTopology"));
            builder.Services.Configure<OllamaClientOptions>(
                builder.Configuration.GetSection(OllamaClientOptions.Section));
            builder.Services.Configure<CopilotClientOptions>(
                builder.Configuration.GetSection(CopilotClientOptions.Section));
            builder.Services.Configure<RoutingOptions>(
                builder.Configuration.GetSection(RoutingOptions.Section));
            builder.Services.AddSingleton<IRoutingOptionsPersistence>(sp =>
                new RoutingOptionsPersistence(Path.Combine(AppContext.BaseDirectory, "routing.json")));
            builder.Services.AddSingleton<TopologyRoutingSaveCoordinator>();

            var topologyConfig = builder.Configuration
                .GetSection("NodeTopology")
                .Get<NodeTopologyConfig>() ?? new NodeTopologyConfig();

            // Resilient HttpClients per Ollama node
            foreach (var node in topologyConfig.Nodes.Where(n => n.Provider == NodeProviderType.Ollama && n.Ollama is not null))
            {
                var ollamaConfig = node.Ollama!;
                builder.Services
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

            // HttpClients per Worker node
            foreach (var workerNode in topologyConfig.Nodes.Where(n => n.Provider == NodeProviderType.Worker && n.Worker is not null))
            {
                var wc = workerNode.Worker!;
                builder.Services
                    .AddHttpClient($"worker-{workerNode.NodeId}", client =>
                    {
                        client.BaseAddress = new Uri(wc.BaseUrl);
                        client.Timeout = TimeSpan.FromSeconds(wc.TimeoutSeconds * 2);
                    })
                    // TSK-0007: Worker nodes now have the same resilience as Ollama nodes
                    .AddResilienceHandler($"resilience-worker-{workerNode.NodeId}", pipeline =>
                    {
                        pipeline.AddRetry(new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 2,
                            Delay = TimeSpan.FromSeconds(1),
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
                            SamplingDuration = TimeSpan.FromSeconds(60),
                            MinimumThroughput = 5,
                            BreakDuration = TimeSpan.FromSeconds(30),
                            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                                .HandleResult(r => !r.IsSuccessStatusCode)
                                .Handle<HttpRequestException>()
                                .Handle<TimeoutRejectedException>()
                        });
                        pipeline.AddTimeout(TimeSpan.FromSeconds(wc.TimeoutSeconds));
                    });

                var capturedNode = workerNode;
                builder.Services.AddKeyedSingleton<WorkerInferenceNode>(capturedNode.NodeId, (sp, _) =>
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

            // LlamaCpp nodes — keyed singleton per topology entry (TSK-0001)
            foreach (var llamaCppNode in topologyConfig.Nodes.Where(n => n.Provider == NodeProviderType.LlamaCpp && n.LlamaCpp is not null))
            {
                var lc = llamaCppNode.LlamaCpp!;
                var capturedLlamaCpp = llamaCppNode;

                builder.Services
                    .AddHttpClient($"llamacpp-{llamaCppNode.NodeId}", client =>
                    {
                        client.BaseAddress = new Uri(lc.BaseUrl);
                        client.Timeout = TimeSpan.FromSeconds(lc.TimeoutSeconds * 2);
                    })
                    .AddResilienceHandler($"resilience-llamacpp-{llamaCppNode.NodeId}", pipeline =>
                    {
                        pipeline.AddRetry(new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 2, Delay = TimeSpan.FromSeconds(1),
                            BackoffType = DelayBackoffType.Exponential, UseJitter = true,
                            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                                .HandleResult(r => !r.IsSuccessStatusCode)
                                .Handle<HttpRequestException>().Handle<TimeoutRejectedException>()
                        });
                        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                        {
                            FailureRatio = 0.8, SamplingDuration = TimeSpan.FromSeconds(60),
                            MinimumThroughput = 5, BreakDuration = TimeSpan.FromSeconds(30),
                            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                                .HandleResult(r => !r.IsSuccessStatusCode)
                                .Handle<HttpRequestException>().Handle<TimeoutRejectedException>()
                        });
                        pipeline.AddTimeout(TimeSpan.FromSeconds(lc.TimeoutSeconds));
                    });

                builder.Services.AddKeyedSingleton<LlamaCppInferenceNode>(capturedLlamaCpp.NodeId, (sp, _) =>
                {
                    var nodeOpts = Options.Create(new LlamaCppClientOptions
                    {
                        BaseUrl = lc.BaseUrl, TimeoutSeconds = lc.TimeoutSeconds,
                        ModelLabel = lc.ModelLabel, NodeId = capturedLlamaCpp.NodeId,
                        VramMb = (int)lc.GpuVramTotalMB
                    });
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpFactory.CreateClient($"llamacpp-{capturedLlamaCpp.NodeId}");
                    var client = new LlamaCppClient(httpClient, nodeOpts, sp.GetRequiredService<ILogger<LlamaCppClient>>());
                    var logger = sp.GetRequiredService<ILogger<LlamaCppInferenceNode>>();
                    return new LlamaCppInferenceNode(client, nodeOpts, logger);
                });
            }

            // Keyed OllamaInferenceNode per topology node (config-driven).
            // NodeA/B legacy singletons are still registered below for RoutingService compat.
            foreach (var ollamaNode in topologyConfig.Nodes.Where(n => n.Provider == NodeProviderType.Ollama && n.Ollama is not null))
            {
                var capturedOllama = ollamaNode;
                builder.Services.AddKeyedSingleton<OllamaInferenceNode>(capturedOllama.NodeId, (sp, _) =>
                {
                    var oConfig = capturedOllama.Ollama!;
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpFactory.CreateClient($"ollama-{capturedOllama.NodeId}");
                    var ollamaOptions = Options.Create(new OllamaClientOptions
                    {
                        BaseUrl = oConfig.BaseUrl,
                        TimeoutSeconds = oConfig.TimeoutSeconds
                    });
                    var client = new OllamaClient(httpClient, ollamaOptions);
                    var logger = sp.GetRequiredService<ILogger<OllamaInferenceNode>>();
                    return new OllamaInferenceNode(capturedOllama.NodeId, oConfig, client, logger);
                });
            }

            builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();

            // Node C (GitHub Copilot) — singleton for CopilotSdk factory dispatch.
            builder.Services.AddSingleton(sp =>
            {
                var copilotOptions = sp.GetRequiredService<IOptions<CopilotClientOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<NodeCInferenceNode>>();
                return NodeCInferenceNode.CreateAsync(copilotOptions, logger).GetAwaiter().GetResult();
            });

            builder.Services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
            builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
            builder.Services.AddSingleton<IPromptHistory, PromptHistoryService>();
            builder.Services.AddSingleton<IRoutingService>(sp => new RoutingService(
                registry: sp.GetRequiredService<INodeRegistry>(),
                queueFactory: nodeId =>
                {
                    // TSK-0006: Use MaxConcurrentRequests from topology instead of hardcoded "A"==64 else 32
                    var cap = topologyConfig.Nodes
                        .FirstOrDefault(n => n.NodeId == nodeId)?.MaxConcurrentRequests ?? 32;
                    return new NodeQueue(capacity: Math.Max(4, cap));
                },
                logger: sp.GetRequiredService<ILogger<RoutingService>>(),
                healthCache: sp.GetRequiredService<INodeHealthCache>(),
                metrics: sp.GetRequiredService<IMetricsCollector>(),
                history: sp.GetRequiredService<IPromptHistory>(),
                routingOptions: sp.GetRequiredService<IOptions<RoutingOptions>>()));
            builder.Services.AddSingleton<IKernelPlannerService, KernelPlannerService>();
            builder.Services.AddSingleton<IModelRegistry>(sp =>
            {
                var registry = new InMemoryModelRegistry();
                var config = sp.GetRequiredService<IConfiguration>();
                var models = config.GetSection("SplitBrain:Models").Get<List<ModelDefinition>>() ?? [];
                foreach (var m in models)
                    registry.RegisterModel(m);
                return registry;
            });

            builder.Services.AddSingleton<IInferenceNodeFactory, Orchestrator.Infrastructure.Registry.InferenceNodeFactory>();
            builder.Services.AddSingleton<Func<NodeConfiguration, IInferenceNode>>(sp => config =>
                config.Provider switch
                {
                    NodeProviderType.Worker =>
                        sp.GetRequiredKeyedService<WorkerInferenceNode>(config.NodeId),
                    NodeProviderType.Ollama =>
                        sp.GetRequiredKeyedService<OllamaInferenceNode>(config.NodeId),
                    NodeProviderType.CopilotSdk =>
                        sp.GetRequiredService<NodeCInferenceNode>(),
                    // TSK-0001: LlamaCpp dispatch added
                    NodeProviderType.LlamaCpp =>
                        sp.GetRequiredKeyedService<LlamaCppInferenceNode>(config.NodeId),
                    _ => throw new InvalidOperationException(
                        $"No IInferenceNode registered for NodeId '{config.NodeId}' provider '{config.Provider}'.")
                });
            builder.Services.AddSingleton<INodeRegistry, NodeRegistry>();
            builder.Services.AddHostedService<NodeHealthCheckService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.MapHub<DashboardHub>("/hubs/dashboard");

            app.Run();
        }
    }
}
