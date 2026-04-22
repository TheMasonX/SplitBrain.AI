using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using NodeClient.Copilot;
using NodeClient.Ollama;
using Orchestrator.Agents;
using Orchestrator.Agents.Sandbox;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.History;
using Orchestrator.Infrastructure.Metrics;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Routing;
using Orchestrator.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Signal Windows SCM that this process is a Windows Service (no-op when run interactively)
builder.Host.UseWindowsService();

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

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();

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

var app = builder.Build();

app.MapMcp("/mcp");

await app.RunAsync();

