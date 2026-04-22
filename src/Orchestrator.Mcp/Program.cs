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
using NodeClient.Ollama;
using Orchestrator.Agents;
using Orchestrator.Agents.Sandbox;
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
// Logs are also written to a rolling file for inspection: %TEMP%\splitbrain-mcp-.log
LoggerConfiguration loggerBuilder = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .WriteTo.File(
        path: Path.Combine(Path.GetTempPath(), "splitbrain-mcp-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);

// Only add EventLog sink on Windows, and only for Information level and above to avoid excessive noise.
// This allows us to monitor the service via Windows Event Viewer without overwhelming it with debug logs.
if (OperatingSystem.IsWindows())
{
    loggerBuilder = loggerBuilder.WriteTo.EventLog("SplitBrain MCP", manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Information);
}

Log.Logger = loggerBuilder.CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

// Node A
builder.Services.Configure<OllamaClientOptions>(
    builder.Configuration.GetSection(OllamaClientOptions.Section));

// Node B — remote Ollama (LAN IP from OllamaNodeB config section)
builder.Services.Configure<OllamaClientOptions>("NodeB",
    builder.Configuration.GetSection("OllamaNodeB"));

// Node C — GitHub Copilot API
builder.Services.Configure<CopilotClientOptions>(
    builder.Configuration.GetSection(CopilotClientOptions.Section));

// Routing fallback chains
builder.Services.Configure<RoutingOptions>(
    builder.Configuration.GetSection(RoutingOptions.Section));

// Dynamic node topology (hot-reloaded from nodes.json)
builder.Services.Configure<NodeTopologyConfig>(
    builder.Configuration.GetSection("NodeTopology"));

// File logging for input/output capture
builder.Services.Configure<FileLoggingOptions>(
    builder.Configuration.GetSection(FileLoggingOptions.Section));

// Base typed client for simple use-cases (NodeA default)
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();

// Register a named, resilient HttpClient per Ollama node defined in NodeTopology config.
// CRITICAL: OllamaApiClient (OllamaSharp) MUST receive this injected HttpClient.
// Creating new OllamaApiClient(uri) creates its own internal HttpClient, bypassing Polly entirely.
var topologyConfig = builder.Configuration
    .GetSection("NodeTopology")
    .Get<NodeTopologyConfig>() ?? new NodeTopologyConfig();

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
            // 1. Retry: exponential backoff + jitter, 3 attempts
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
            // 2. Circuit breaker: 80% failure ratio, 10-sample window, 30s break
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
            // 3. Per-node timeout
            pipeline.AddTimeout(TimeSpan.FromSeconds(ollamaConfig.TimeoutSeconds));
        });
}

// Node A inference node (uses default IOptions<OllamaClientOptions>)
builder.Services.AddSingleton<NodeAInferenceNode>();
builder.Services.AddSingleton<IInferenceNode>(sp => sp.GetRequiredService<NodeAInferenceNode>());

// Node B inference node (uses a separate OllamaClient bound to OllamaNodeB config)
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

// Node C inference node — GitHub Copilot API (optional: only registered when configured)
// API token is resolved securely from Azure Key Vault (preferred) or COPILOT_API_KEY env var.
// No raw key is ever stored in config files.
builder.Services.AddSingleton(sp =>
{
    var copilotOptions = sp.GetRequiredService<IOptions<CopilotClientOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<NodeCInferenceNode>>();
    return NodeCInferenceNode.CreateAsync(copilotOptions, logger).GetAwaiter().GetResult();
});

// Queues — Node A high priority (64), Node B normal (32), Node C normal (32)
builder.Services.AddKeyedSingleton<IInferenceQueue>("nodeA", (_, _) => new NodeQueue(capacity: 64));
builder.Services.AddKeyedSingleton<IInferenceQueue>("nodeB", (_, _) => new NodeQueue(capacity: 32));
builder.Services.AddKeyedSingleton<IInferenceQueue>("nodeC", (_, _) => new NodeQueue(capacity: 32));

builder.Services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddSingleton<IPromptHistory, PromptHistoryService>();
builder.Services.AddSingleton<ILoggingService, FileLoggingService>();
// No SignalR dashboard in MCP host — use no-op publishers
builder.Services.AddSingleton<INodeHealthPublisher, NullNodeHealthPublisher>();
builder.Services.AddSingleton<ILogEntryPublisher, NullLogEntryPublisher>();

// Node registry — dynamic topology management with hot-reload
builder.Services.AddSingleton<IInferenceNodeFactory, InferenceNodeFactory>();

// Dispatch factory: maps NodeConfiguration → concrete singleton by NodeId.
// This lets NodeRegistry.RebuildTopology reuse existing nodes on hot-reload
// without constructing new instances each time.
builder.Services.AddSingleton<Func<NodeConfiguration, IInferenceNode>>(sp => config =>
    config.NodeId switch
    {
        "A" => sp.GetRequiredService<NodeAInferenceNode>(),
        "B" => sp.GetRequiredService<NodeBInferenceNode>(),
        "C" => sp.GetRequiredService<NodeCInferenceNode>(),
        _ => throw new InvalidOperationException($"No IInferenceNode registered for NodeId '{config.NodeId}'.")
    });

builder.Services.AddSingleton<INodeRegistry, NodeRegistry>();
builder.Services.AddHostedService<NodeHealthCheckService>();

// MCP idempotency cache (TTL-based deduplication, in-process)
builder.Services.AddSingleton<IIdempotencyCache, InMemoryIdempotencyCache>();

// Model registry — seeded from appsettings SplitBrain:Models section
builder.Services.AddSingleton<IModelRegistry>(sp =>
{
    var registry = new InMemoryModelRegistry();
    var config = sp.GetRequiredService<IConfiguration>();
    var models = config.GetSection("SplitBrain:Models").Get<List<ModelDefinition>>() ?? [];
    foreach (var m in models)
        registry.RegisterModel(m);
    return registry;
});

builder.Services.AddSingleton<IRoutingService>(sp => new RoutingService(
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

// Phase 3 — Agent system
builder.Services.AddSingleton<ICodeSandbox, ProcessCodeSandbox>();
builder.Services.AddSingleton<IAgentEventLog>(_ => new LiteDbAgentEventLog());
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

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

// OpenTelemetry — traces + metrics exported via OTLP (Jaeger / Grafana / etc.)
// Set OTEL_EXPORTER_OTLP_ENDPOINT env-var to your collector; defaults to http://localhost:4317
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

