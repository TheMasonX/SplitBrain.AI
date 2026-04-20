using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

var builder = Host.CreateApplicationBuilder(args);

// Node A — local Ollama (localhost:11434)
builder.Services.Configure<OllamaClientOptions>(
    builder.Configuration.GetSection(OllamaClientOptions.Section));

// Node B — remote Ollama (LAN IP from OllamaNodeB config section)
builder.Services.Configure<OllamaClientOptions>("NodeB",
    builder.Configuration.GetSection("OllamaNodeB"));

// Node C — GitHub Copilot API
builder.Services.Configure<CopilotClientOptions>(
    builder.Configuration.GetSection(CopilotClientOptions.Section));

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
    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NodeBInferenceNode>>();
    return new NodeBInferenceNode(ollamaClient, logger);
});

// Node C inference node — GitHub Copilot API (optional: only registered when configured)
// API token is resolved securely from Azure Key Vault (preferred) or COPILOT_API_KEY env var.
// No raw key is ever stored in config files.
builder.Services.AddSingleton<NodeCInferenceNode>(sp =>
{
    var copilotOptions = sp.GetRequiredService<IOptions<CopilotClientOptions>>().Value;
    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NodeCInferenceNode>>();
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
    logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RoutingService>>(),
    nodeB: sp.GetRequiredService<NodeBInferenceNode>(),
    nodeBQueue: sp.GetRequiredKeyedService<IInferenceQueue>("nodeB"),
    healthCache: sp.GetRequiredService<INodeHealthCache>(),
    metrics: sp.GetRequiredService<IMetricsCollector>(),
    history: sp.GetRequiredService<IPromptHistory>(),
    nodeC: sp.GetRequiredService<NodeCInferenceNode>(),
    nodeCQueue: sp.GetRequiredKeyedService<IInferenceQueue>("nodeC")));

// Phase 3 — Agent system (§9 + §13)
builder.Services.AddSingleton<ICodeSandbox, ProcessCodeSandbox>();
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ReviewCodeTool>()
    .WithTools<RefactorCodeTool>()
    .WithTools<GenerateTestsTool>()
    .WithTools<SearchCodebaseTool>()
    .WithTools<ApplyPatchTool>()
    .WithTools<RunTestsTool>()
    .WithTools<AgentTaskTool>();

var host = builder.Build();
await host.RunAsync();

