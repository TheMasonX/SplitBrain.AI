using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Polly;
using Polly.Timeout;
using Serilog;
using Serilog.Events;
using NodeClient.Copilot;
using NodeClient.LlamaCpp;
using NodeClient.Ollama;
using NodeClient.Worker;
using Orchestrator.Agents;
using Orchestrator.Agents.Sandbox;
using Orchestrator.Agents.SemanticKernel;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.AgentLog;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.History;
using Orchestrator.Infrastructure.Logging;
using Orchestrator.Infrastructure.Metrics;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Registry;
using Orchestrator.Infrastructure.Routing;
using Orchestrator.Mcp.Idempotency;
using Orchestrator.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Signal Windows SCM that this process is a Windows Service (no-op when run interactively)
builder.Host.UseWindowsService();

// Load nodes.json beside the executable for hot-reloadable topology config
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "nodes.json"),
    optional: true,
    reloadOnChange: true);
// Also load from the project directory during development
builder.Configuration.AddJsonFile("nodes.json", optional: true, reloadOnChange: true);

// Redirect all logging to stderr so stdout carries only MCP JSON-RPC messages
LoggerConfiguration loggerBuilder = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .WriteTo.File(
        path: Path.Combine(Path.GetTempPath(), "splitbrain-mcp-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);

if (OperatingSystem.IsWindows())
{
    loggerBuilder = loggerBuilder.WriteTo.EventLog("SplitBrain MCP", manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Information);
}

Log.Logger = loggerBuilder.CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

builder.Services.Configure<OllamaClientOptions>(
    builder.Configuration.GetSection(OllamaClientOptions.Section));

builder.Services.Configure<OllamaClientOptions>("NodeB",
    builder.Configuration.GetSection("OllamaNodeB"));

builder.Services.Configure<CopilotClientOptions>(
    builder.Configuration.GetSection(CopilotClientOptions.Section));

builder.Services.Configure<RoutingOptions>(
    builder.Configuration.GetSection(RoutingOptions.Section));

builder.Services.Configure<NodeTopologyConfig>(
    builder.Configuration.GetSection("NodeTopology"));

builder.Services.Configure<FileLoggingOptions>(
    builder.Configuration.GetSection(FileLoggingOptions.Section));

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();

var topologyConfig = builder.Configuration
    .GetSection("NodeTopology")
    .Get<NodeTopologyConfig>() ?? new NodeTopologyConfig();

// Register resilient HttpClients + keyed OllamaInferenceNode per topology entry
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

// Worker nodes
foreach (var workerNode in topologyConfig.Nodes.Where(n => n.Provider == NodeProviderType.Worker && n.Worker is not null))
{
    var wc = workerNode.Worker!;
    builder.Services
        .AddHttpClient($"worker-{workerNode.NodeId}", client =>
        {
            client.BaseAddress = new Uri(wc.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(wc.TimeoutSeconds * 2);
        })
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

// LlamaCpp nodes (TSK-0001: wire LlamaCppInferenceNode into dispatch)
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
            pipeline.AddTimeout(TimeSpan.FromSeconds(lc.TimeoutSeconds));
        });

    builder.Services.AddKeyedSingleton<LlamaCppInferenceNode>(capturedLlamaCpp.NodeId, (sp, _) =>
    {
        var nodeOpts = Options.Create(new LlamaCppClientOptions
        {
            BaseUrl        = lc.BaseUrl,
            TimeoutSeconds = lc.TimeoutSeconds,
            ModelLabel     = lc.ModelLabel,
            NodeId         = capturedLlamaCpp.NodeId,
            VramMb         = (int)lc.GpuVramTotalMB
        });
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient  = httpFactory.CreateClient($"llamacpp-{capturedLlamaCpp.NodeId}");
        var client      = new LlamaCppClient(httpClient, nodeOpts, sp.GetRequiredService<ILogger<LlamaCppClient>>());
        var logger      = sp.GetRequiredService<ILogger<LlamaCppInferenceNode>>();
        return new LlamaCppInferenceNode(client, nodeOpts, logger);
    });
}

builder.Services.AddSingleton(sp =>
{
    var copilotOptions = sp.GetRequiredService<IOptions<CopilotClientOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<NodeCInferenceNode>>();
    return NodeCInferenceNode.CreateAsync(copilotOptions, logger).GetAwaiter().GetResult();
});

builder.Services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddSingleton<IPromptHistory, PromptHistoryService>();
builder.Services.AddSingleton<ILoggingService, FileLoggingService>();
builder.Services.AddSingleton<INodeHealthPublisher, NullNodeHealthPublisher>();
builder.Services.AddSingleton<ILogEntryPublisher, NullLogEntryPublisher>();

builder.Services.AddSingleton<IInferenceNodeFactory, Orchestrator.Infrastructure.Registry.InferenceNodeFactory>();

// Dispatch factory — maps NodeConfiguration → concrete keyed singleton by NodeId.
// TSK-0001: LlamaCpp arm added.
builder.Services.AddSingleton<Func<NodeConfiguration, IInferenceNode>>(sp => config =>
    config.Provider switch
    {
        NodeProviderType.Worker =>
            sp.GetRequiredKeyedService<WorkerInferenceNode>(config.NodeId),
        NodeProviderType.Ollama =>
            sp.GetRequiredKeyedService<OllamaInferenceNode>(config.NodeId),
        NodeProviderType.CopilotSdk =>
            sp.GetRequiredService<NodeCInferenceNode>(),
        NodeProviderType.LlamaCpp =>
            sp.GetRequiredKeyedService<LlamaCppInferenceNode>(config.NodeId),
        _ => throw new InvalidOperationException(
            $"No IInferenceNode registered for NodeId '{config.NodeId}' provider '{config.Provider}'.")
    });

builder.Services.AddSingleton<INodeRegistry, NodeRegistry>();
builder.Services.AddHostedService<NodeHealthCheckService>();

builder.Services.AddSingleton<IIdempotencyCache, InMemoryIdempotencyCache>();

builder.Services.AddSingleton<IModelRegistry>(sp =>
{
    var registry = new InMemoryModelRegistry();
    var config = sp.GetRequiredService<IConfiguration>();
    var models = config.GetSection("SplitBrain:Models").Get<List<ModelDefinition>>() ?? [];
    foreach (var m in models)
        registry.RegisterModel(m);
    return registry;
});

// TSK-0006: Queue capacity now reads from topology MaxConcurrentRequests instead of
// hardcoded nodeId == "A" ? 64 : 32.
builder.Services.AddSingleton<IRoutingService>(sp => new RoutingService(
    registry: sp.GetRequiredService<INodeRegistry>(),
    queueFactory: nodeId =>
    {
        var cap = topologyConfig.Nodes
            .FirstOrDefault(n => n.NodeId == nodeId)?.MaxConcurrentRequests ?? 32;
        return new NodeQueue(capacity: Math.Max(4, cap));
    },
    logger: sp.GetRequiredService<ILogger<RoutingService>>(),
    healthCache: sp.GetRequiredService<INodeHealthCache>(),
    metrics: sp.GetRequiredService<IMetricsCollector>(),
    history: sp.GetRequiredService<IPromptHistory>(),
    routingOptions: sp.GetRequiredService<IOptions<RoutingOptions>>()));

builder.Services.AddSingleton<ICodeSandbox, ProcessCodeSandbox>();
builder.Services.AddSingleton<IAgentEventLog>(_ => new LiteDbAgentEventLog());
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
builder.Services.AddSingleton<IKernelPlannerService, KernelPlannerService>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<ReviewCodeTool>()
    .WithTools<RefactorCodeTool>()
    .WithTools<GenerateTestsTool>()
    .WithTools<SearchCodebaseTool>()
    .WithTools<ApplyPatchTool>()
    .WithTools<RunTestsTool>()
    .WithTools<AgentTaskTool>();

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("SplitBrain.Mcp", serviceVersion: "3.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

var app = builder.Build();

app.MapMcp("/mcp");

await app.RunAsync();
