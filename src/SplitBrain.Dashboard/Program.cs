using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeClient.Copilot;
using NodeClient.Ollama;
using NodeClient.Worker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
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

            // Dashboard services
            builder.Services.AddSingleton<DashboardState>();
            builder.Services.AddSingleton<INodeHealthPublisher, SignalRNodeHealthPublisher>();
            builder.Services.AddSingleton<ILogEntryPublisher, SignalRLogEntryPublisher>();

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

            builder.Services.Configure<NodeTopologyConfig>(
                builder.Configuration.GetSection("NodeTopology"));
            builder.Services.Configure<OllamaClientOptions>(
                builder.Configuration.GetSection(OllamaClientOptions.Section));
            builder.Services.Configure<CopilotClientOptions>(
                builder.Configuration.GetSection(CopilotClientOptions.Section));
            builder.Services.Configure<RoutingOptions>(
                builder.Configuration.GetSection(RoutingOptions.Section));

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

            builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();
            builder.Services.AddSingleton<NodeAInferenceNode>();
            builder.Services.AddSingleton<IInferenceNode>(sp => sp.GetRequiredService<NodeAInferenceNode>());
            builder.Services.AddSingleton<NodeBInferenceNode>(sp =>
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
            builder.Services.AddSingleton(sp =>
            {
                var copilotOptions = sp.GetRequiredService<IOptions<CopilotClientOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<NodeCInferenceNode>>();
                return NodeCInferenceNode.CreateAsync(copilotOptions, logger).GetAwaiter().GetResult();
            });

            builder.Services.AddKeyedSingleton<IInferenceQueue>("nodeA", (_, _) => new NodeQueue(capacity: 64));
            builder.Services.AddKeyedSingleton<IInferenceQueue>("nodeB", (_, _) => new NodeQueue(capacity: 32));
            builder.Services.AddKeyedSingleton<IInferenceQueue>("nodeC", (_, _) => new NodeQueue(capacity: 32));

            builder.Services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
            builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
            builder.Services.AddSingleton<IModelRegistry>(sp =>
            {
                var registry = new InMemoryModelRegistry();
                var config = sp.GetRequiredService<IConfiguration>();
                var models = config.GetSection("SplitBrain:Models").Get<List<ModelDefinition>>() ?? [];
                foreach (var m in models)
                    registry.RegisterModel(m);
                return registry;
            });

            builder.Services.AddSingleton<IInferenceNodeFactory, InferenceNodeFactory>();
            builder.Services.AddSingleton<Func<NodeConfiguration, IInferenceNode>>(sp => config =>
                config.NodeId switch
                {
                    "A" => sp.GetRequiredService<NodeAInferenceNode>(),
                    "B" => sp.GetRequiredService<NodeBInferenceNode>(),
                    "C" => sp.GetRequiredService<NodeCInferenceNode>(),
                    _ when config.Provider == NodeProviderType.Worker =>
                        sp.GetRequiredKeyedService<WorkerInferenceNode>(config.NodeId),
                    _ => throw new InvalidOperationException($"No IInferenceNode registered for NodeId '{config.NodeId}'.")
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
