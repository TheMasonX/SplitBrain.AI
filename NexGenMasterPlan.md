SplitBrain.AI
Architectural Implementation Plan — Distributed AI Orchestration Platform

Author: Lucas  |  Version: 1.0  |  Date: April 22, 2026

Target Runtime: .NET 8+  |  Classification: Technical Architecture Document

1. Executive Summary
This document defines the complete architectural blueprint for SplitBrain.AI, a distributed AI orchestration platform built on .NET 8+. The system coordinates multiple Ollama inference nodes, routes tasks intelligently based on real-time resource telemetry, and exposes capabilities through the Model Context Protocol (MCP).

Greenfield .NET 8+ solution with a modular 9-project structure enforcing clean separation of concerns across networking, routing, model management, agents, MCP integration, observability, and dashboard layers.
Configurable multi-node networking replacing all hardcoded dual-node topology with a JSON-driven node registry supporting hot-reload, role-based classification, capability tagging, and dynamic discovery.
Blazor Server dashboard with real-time logging and cluster monitoring via SignalR, providing live VRAM telemetry, task status, structured log streaming, and runtime configuration management.
Resilience-first model management using Polly v8 resilience pipelines with per-node retry, circuit breaker, timeout, and fallback chains — ensuring graceful degradation when nodes or models become unavailable.
Full observability stack combining Serilog structured logging, OpenTelemetry distributed tracing and metrics, and a custom SignalR sink for real-time dashboard integration.
2. Solution Structure & Project Layout
The solution follows a clean modular architecture. Each project owns a single bounded context with explicit dependency boundaries enforced by project references.

SplitBrain.AI/
├── src/
│   ├── SplitBrain.Core/              — Domain models, interfaces, enums, constants
│   ├── SplitBrain.Networking/        — Node discovery, health checks, Ollama clients
│   ├── SplitBrain.Routing/           — VRAM-aware scoring, task routing engine
│   ├── SplitBrain.Models/            — Model definitions, registry, fallback chains
│   ├── SplitBrain.Agents/            — Bounded agent state machine, iteration limits
│   ├── SplitBrain.MCP/               — MCP server implementation (tools, resources)
│   ├── SplitBrain.Observability/     — Logging, metrics, tracing infrastructure
│   ├── SplitBrain.Dashboard/         — Blazor Server app, SignalR hubs, UI components
│   └── SplitBrain.Host/              — ASP.NET Core host, DI composition root
├── tests/
│   ├── SplitBrain.Core.Tests/
│   ├── SplitBrain.Networking.Tests/
│   ├── SplitBrain.Routing.Tests/
│   └── SplitBrain.Integration.Tests/
├── config/
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── nodes.json                    — Node topology configuration
└── docs/
Project Responsibilities
Project	Responsibility	Key Dependencies
SplitBrain.Core	Domain models, interfaces, enums (NodeRole, ModelCapability), shared constants, and DTOs. Zero external dependencies — this is the innermost layer.	None
SplitBrain.Networking	Node configuration loading, INodeRegistry implementation, NodeHealthCheckService background worker, IOllamaNode / IOllamaNodeFactory abstractions wrapping OllamaSharp.	Core, OllamaSharp
SplitBrain.Routing	VRAM-aware scoring engine, IRoutingEngine implementation, pluggable IRoutingPolicy strategies, routing decision audit trail.	Core, Networking
SplitBrain.Models	Model definitions, IModelRegistry with live availability cross-referencing, IFallbackChainProvider for cascading model/node selection.	Core, Networking
SplitBrain.Agents	Bounded agent state machine with configurable iteration and token limits, agent lifecycle management, tool execution orchestration.	Core, Routing, Models
SplitBrain.MCP	MCP server implementation exposing tools (CodeReview, Refactor, GenerateTests, RunAgent) and resources (node health, model registry, task history). Streamable HTTP + stdio transports.	Core, Agents, ModelContextProtocol SDK
SplitBrain.Observability	Serilog configuration and custom sinks, OpenTelemetry ActivitySource and meters, structured log enrichers (NodeId, TaskId, CorrelationId).	Core, Serilog, OpenTelemetry
SplitBrain.Dashboard	Blazor Server application, DashboardHub SignalR hub, strongly-typed IDashboardClient, UI components for cluster monitoring, log streaming, and settings.	Core, Observability
SplitBrain.Host	ASP.NET Core host and DI composition root. Wires all services, configures middleware pipeline, maps SignalR hubs, registers MCP server endpoints. This is the only executable project.	All projects
Design Principle

SplitBrain.Core has zero NuGet dependencies. Every other project references Core. The Host project is the only one that references all projects — it serves as the composition root. This enforces a strict dependency direction: Core ← Infrastructure ← Application ← Host.

3. Configurable Networking Layer
The networking layer replaces all hardcoded node references with a fully configurable, hot-reloadable topology. Nodes are defined in configuration, discovered at startup, continuously health-checked, and exposed through a strongly-typed registry.

3.1 Node Configuration Model
namespace SplitBrain.Core.Configuration;

public enum NodeRole
{
    Fast,       // Optimized for low-latency responses (e.g., smaller quantized models)
    Deep,       // Optimized for quality/reasoning (e.g., larger models, more VRAM)
    Hybrid,     // Can serve both roles depending on load
    Standby     // Passive — only activated on failover
}

public sealed class NodeConfiguration
{
    public required string NodeId { get; init; }
    public required string Name { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = 11434;
    public NodeRole Role { get; init; } = NodeRole.Hybrid;
    public int Priority { get; init; } = 100;
    public int MaxConcurrentRequests { get; init; } = 4;
    public List<string> Tags { get; init; } = [];
    public int HealthCheckIntervalSeconds { get; init; } = 30;
    public bool Enabled { get; init; } = true;

    public Uri BaseUri => new($"http://{Host}:{Port}");
}

public sealed class NodesConfiguration
{
    public const string SectionName = "SplitBrain:Nodes";
    public List<NodeConfiguration> Nodes { get; init; } = [];
}
Sample nodes.json
{
  "SplitBrain": {
    "Nodes": [
      {
        "NodeId": "node-rtx5060",
        "Name": "Fast Node (RTX 5060)",
        "Host": "192.168.1.100",
        "Port": 11434,
        "Role": "Fast",
        "Priority": 10,
        "MaxConcurrentRequests": 6,
        "Tags": ["code", "chat", "fast-inference"],
        "HealthCheckIntervalSeconds": 15,
        "Enabled": true
      },
      {
        "NodeId": "node-gtx1080",
        "Name": "Deep Node (GTX 1080)",
        "Host": "192.168.1.101",
        "Port": 11434,
        "Role": "Deep",
        "Priority": 20,
        "MaxConcurrentRequests": 2,
        "Tags": ["reasoning", "code-review", "embedding"],
        "HealthCheckIntervalSeconds": 30,
        "Enabled": true
      },
      {
        "NodeId": "node-cloud-fallback",
        "Name": "Cloud Fallback",
        "Host": "ollama.remote.example.com",
        "Port": 443,
        "Role": "Standby",
        "Priority": 999,
        "MaxConcurrentRequests": 8,
        "Tags": ["code", "reasoning", "chat", "embedding"],
        "HealthCheckIntervalSeconds": 60,
        "Enabled": true
      }
    ]
  }
}
3.2 Node Registry & Discovery
namespace SplitBrain.Core.Abstractions;

public interface INodeRegistry
{
    IReadOnlyList<NodeConfiguration> GetAllNodes();
    IReadOnlyList<NodeConfiguration> GetHealthyNodes();
    IReadOnlyList<NodeConfiguration> GetNodesByRole(NodeRole role);
    IReadOnlyList<NodeConfiguration> GetNodesByTag(string tag);
    NodeConfiguration? GetNode(string nodeId);

    void RegisterNode(NodeConfiguration config);
    void DeregisterNode(string nodeId);
    void UpdateNodeHealth(string nodeId, NodeHealthStatus status);

    NodeHealthStatus? GetNodeHealth(string nodeId);

    event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;
}

public sealed class NodeStateChangedEventArgs(
    string nodeId,
    NodeHealthStatus? previous,
    NodeHealthStatus current) : EventArgs
{
    public string NodeId { get; } = nodeId;
    public NodeHealthStatus? Previous { get; } = previous;
    public NodeHealthStatus Current { get; } = current;
}
Implementation: NodeRegistry
namespace SplitBrain.Networking.Services;

public sealed class NodeRegistry : INodeRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, NodeConfiguration> _nodes = new();
    private readonly ConcurrentDictionary<string, NodeHealthStatus> _healthStates = new();
    private readonly IOptionsMonitor<NodesConfiguration> _optionsMonitor;
    private readonly IDisposable? _changeToken;
    private readonly ILogger<NodeRegistry> _logger;

    public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;

    public NodeRegistry(
        IOptionsMonitor<NodesConfiguration> optionsMonitor,
        ILogger<NodeRegistry> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        // Initial load
        LoadNodes(optionsMonitor.CurrentValue);

        // Hot-reload on config change
        _changeToken = _optionsMonitor.OnChange(config =>
        {
            _logger.LogInformation("Node configuration changed — reloading {Count} nodes",
                config.Nodes.Count);
            LoadNodes(config);
        });
    }

    public IReadOnlyList<NodeConfiguration> GetAllNodes() =>
        _nodes.Values
            .Where(n => n.Enabled)
            .OrderBy(n => n.Priority)
            .ToList();

    public IReadOnlyList<NodeConfiguration> GetHealthyNodes() =>
        _nodes.Values
            .Where(n => n.Enabled
                && _healthStates.TryGetValue(n.NodeId, out var h)
                && h.IsHealthy)
            .OrderBy(n => n.Priority)
            .ToList();

    public IReadOnlyList<NodeConfiguration> GetNodesByRole(NodeRole role) =>
        GetHealthyNodes().Where(n => n.Role == role).ToList();

    public IReadOnlyList<NodeConfiguration> GetNodesByTag(string tag) =>
        GetHealthyNodes()
            .Where(n => n.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public void UpdateNodeHealth(string nodeId, NodeHealthStatus status)
    {
        var previous = _healthStates.TryGetValue(nodeId, out var p) ? p : null;
        _healthStates[nodeId] = status;

        if (previous?.IsHealthy != status.IsHealthy)
        {
            NodeStateChanged?.Invoke(this,
                new NodeStateChangedEventArgs(nodeId, previous, status));
        }
    }

    private void LoadNodes(NodesConfiguration config)
    {
        _nodes.Clear();
        foreach (var node in config.Nodes)
            _nodes[node.NodeId] = node;
    }

    public void Dispose() => _changeToken?.Dispose();

    // ... remaining members omitted for brevity
}
Hot-Reload

IOptionsMonitor<NodesConfiguration> detects changes to nodes.json at runtime without restarting the host. Combined with reloadOnChange: true in ConfigurationBuilder, you can add, remove, or reconfigure nodes by editing the file on disk.

3.3 Health Check Service
NodeHealthStatus Model
namespace SplitBrain.Core.Models;

public sealed record NodeHealthStatus
{
    public required bool IsHealthy { get; init; }
    public required DateTimeOffset LastChecked { get; init; }
    public double LatencyMs { get; init; }
    public List<string> AvailableModels { get; init; } = [];
    public List<RunningModelInfo> RunningModels { get; init; } = [];
    public long VramUsedMB { get; init; }
    public long VramTotalMB { get; init; }
    public string? ErrorMessage { get; init; }

    public double VramUtilizationPercent =>
        VramTotalMB > 0 ? (double)VramUsedMB / VramTotalMB * 100 : 0;

    public long VramAvailableMB => VramTotalMB - VramUsedMB;
}

public sealed record RunningModelInfo
{
    public required string ModelName { get; init; }
    public long SizeBytes { get; init; }
    public long VramBytes { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
Background Health Check Service
namespace SplitBrain.Networking.Services;

public sealed class NodeHealthCheckService(
    INodeRegistry nodeRegistry,
    IOllamaNodeFactory nodeFactory,
    IHubContext<DashboardHub, IDashboardClient> dashboardHub,
    ILogger<NodeHealthCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Node health check service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nodes = nodeRegistry.GetAllNodes();

            var healthTasks = nodes.Select(node =>
                CheckNodeHealthAsync(node, stoppingToken));

            await Task.WhenAll(healthTasks);

            // Use the shortest configured interval across all nodes
            var minInterval = nodes.Min(n => n.HealthCheckIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(minInterval), stoppingToken);
        }
    }

    private async Task CheckNodeHealthAsync(
        NodeConfiguration node, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var client = nodeFactory.Create(node);

            // Poll /api/tags for available models
            var models = await client.ListModelsAsync(ct);

            // Poll /api/ps for running models and VRAM
            var running = await client.GetRunningModelsAsync(ct);

            sw.Stop();

            var status = new NodeHealthStatus
            {
                IsHealthy = true,
                LastChecked = DateTimeOffset.UtcNow,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                AvailableModels = models.Select(m => m.Name).ToList(),
                RunningModels = running.Models,
                VramUsedMB = running.VramUsedMB,
                VramTotalMB = running.VramTotalMB
            };

            nodeRegistry.UpdateNodeHealth(node.NodeId, status);

            // Push to dashboard
            await dashboardHub.Clients.All.ReceiveNodeHealthUpdate(
                new NodeHealthSnapshot(node.NodeId, node.Name, status));
        }
        catch (Exception ex)
        {
            sw.Stop();
            var status = new NodeHealthStatus
            {
                IsHealthy = false,
                LastChecked = DateTimeOffset.UtcNow,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message
            };

            nodeRegistry.UpdateNodeHealth(node.NodeId, status);
            logger.LogWarning(ex, "Health check failed for node {NodeId}", node.NodeId);
        }
    }
}
3.4 Ollama Client Abstraction
namespace SplitBrain.Core.Abstractions;

public interface IOllamaNode
{
    string NodeId { get; }
    NodeConfiguration Configuration { get; }

    Task<IAsyncEnumerable<string>> ChatAsync(
        ChatRequest request, CancellationToken ct = default);

    Task<string> GenerateAsync(
        GenerateRequest request, CancellationToken ct = default);

    Task<float[]> EmbedAsync(
        string model, string input, CancellationToken ct = default);

    Task<IReadOnlyList<OllamaModel>> ListModelsAsync(
        CancellationToken ct = default);

    Task<RunningModelsResponse> GetRunningModelsAsync(
        CancellationToken ct = default);
}

public interface IOllamaNodeFactory
{
    IOllamaNode Create(NodeConfiguration config);
}
namespace SplitBrain.Networking.Clients;

public sealed class OllamaNode : IOllamaNode
{
    private readonly OllamaApiClient _client;

    public string NodeId { get; }
    public NodeConfiguration Configuration { get; }

    public OllamaNode(NodeConfiguration config)
    {
        NodeId = config.NodeId;
        Configuration = config;
        _client = new OllamaApiClient(config.BaseUri);
    }

    public async Task<IReadOnlyList<OllamaModel>> ListModelsAsync(
        CancellationToken ct = default)
    {
        var models = await _client.ListLocalModelsAsync(ct);
        return models.ToList();
    }

    // ... remaining endpoint wrappers
}

public sealed class OllamaNodeFactory(
    ILogger<OllamaNodeFactory> logger) : IOllamaNodeFactory
{
    private readonly ConcurrentDictionary<string, IOllamaNode> _cache = new();

    public IOllamaNode Create(NodeConfiguration config) =>
        _cache.GetOrAdd(config.NodeId, _ =>
        {
            logger.LogDebug("Creating Ollama client for node {NodeId} at {Uri}",
                config.NodeId, config.BaseUri);
            return new OllamaNode(config);
        });
}
4. Model Definitions & Registry
4.1 Model Definition
namespace SplitBrain.Core.Models;

public enum ModelFamily
{
    Qwen, DeepSeek, Nomic, Llama, Mistral, Phi, Gemma, Custom
}

[Flags]
public enum ModelCapability
{
    None         = 0,
    Chat         = 1 << 0,
    CodeGeneration = 1 << 1,
    Reasoning    = 1 << 2,
    Embedding    = 1 << 3,
    Vision       = 1 << 4
}

public sealed record ModelDefinition
{
    public required string ModelId { get; init; }        // e.g. "qwen2.5-coder:7b-q4_K_M"
    public required string DisplayName { get; init; }
    public ModelFamily Family { get; init; }
    public ModelCapability Capabilities { get; init; }
    public string QuantizationLevel { get; init; } = "Q4_K_M";
    public int ContextWindow { get; init; } = 8192;
    public int VramRequirementMB { get; init; }
    public List<string> PreferredNodes { get; init; } = [];
    public bool IsDefault { get; init; }
}
Sample Model Configuration
Model ID	Family	Capabilities	Quant	VRAM (MB)	Preferred Node	Default
qwen2.5-coder:7b-q5_K_M	Qwen	Chat, CodeGeneration	Q5_K_M	5800	node-rtx5060	Yes
qwen2.5-coder:7b-q4_K_M	Qwen	Chat, CodeGeneration	Q4_K_M	4700	node-rtx5060	No
deepseek-r1:7b	DeepSeek	Chat, Reasoning, CodeGeneration	Q4_K_M	5200	node-gtx1080	No
nomic-embed-text:latest	Nomic	Embedding	FP16	550	node-gtx1080	No
4.2 Model Registry
namespace SplitBrain.Core.Abstractions;

public interface IModelRegistry
{
    IReadOnlyList<ModelDefinition> GetAllModels();

    IReadOnlyList<ModelDefinition> GetAvailableModels();

    IReadOnlyList<ModelDefinition> GetModelsByCapability(ModelCapability capability);

    ModelDefinition? GetModelForTask(TaskRequirements requirements);

    /// <summary>
    /// Cross-references configured models against live node health data
    /// to determine which models are actually loaded/loadable right now.
    /// </summary>
    void RefreshAvailability();
}

public sealed record TaskRequirements
{
    public required ModelCapability RequiredCapabilities { get; init; }
    public int MinContextWindow { get; init; } = 4096;
    public string? PreferredModelFamily { get; init; }
    public string? PreferredNodeId { get; init; }
}
The ModelRegistry implementation loads ModelDefinition records from IConfiguration and cross-references them against live data from INodeRegistry. When RefreshAvailability() is called (triggered by health check updates), it reconciles configured models against each node's AvailableModels list to determine actual availability.

4.3 Fallback Chain Pattern
namespace SplitBrain.Core.Models;

public sealed record FallbackChainLink
{
    public required string ModelId { get; init; }
    public string? PreferredNodeId { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record FallbackChainConfiguration
{
    public required string TaskType { get; init; }   // "CodeReview", "TestGen", etc.
    public required List<FallbackChainLink> Chain { get; init; }
}

public interface IFallbackChainProvider
{
    FallbackChainConfiguration? GetChain(string taskType);
    IReadOnlyList<FallbackChainLink> GetAvailableChain(string taskType);
}
Sample Fallback Chain Configuration
{
  "SplitBrain": {
    "FallbackChains": [
      {
        "TaskType": "CodeReview",
        "Chain": [
          { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 120 },
          { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 120 },
          { "ModelId": "deepseek-r1:7b",           "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 180 },
          { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeId": "node-cloud-fallback", "TimeoutSeconds": 90 }
        ]
      },
      {
        "TaskType": "Embedding",
        "Chain": [
          { "ModelId": "nomic-embed-text:latest", "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 30 },
          { "ModelId": "nomic-embed-text:latest", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 30 }
        ]
      },
      {
        "TaskType": "Chat",
        "Chain": [
          { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 90 },
          { "ModelId": "deepseek-r1:7b",           "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 120 },
          { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeId": "node-cloud-fallback", "TimeoutSeconds": 60 }
        ]
      }
    ]
  }
}
Fallback Cascade Flow

CodeReview example: Qwen 2.5 Coder Q5 (RTX 5060) → Qwen 2.5 Coder Q4 (RTX 5060) → DeepSeek R1 (GTX 1080) → Qwen 2.5 Coder Q4 (Cloud) → Error with alert. Each link is attempted in order. A link is skipped if the target node is unhealthy or the model is unavailable.

5. Intelligent Routing Engine
5.1 Task Scoring Algorithm
The routing engine scores every viable (node, model) pair for a given task request and selects the highest-scoring combination. Each dimension is scored 0–100 independently, then combined using configurable weights.

namespace SplitBrain.Core.Models;

public sealed record RoutingScore
{
    public required string NodeId { get; init; }
    public required string ModelId { get; init; }

    public int VramScore { get; init; }           // 0-100: enough VRAM to load?
    public int QueueDepthScore { get; init; }     // 0-100: how busy is the node?
    public int LatencyScore { get; init; }        // 0-100: recent response time
    public int CapabilityScore { get; init; }     // 0-100: model fits task?
    public int AffinityScore { get; init; }       // 0-100: model already loaded?

    public double WeightedTotal { get; init; }
    public string Reasoning { get; init; } = "";
}

public sealed record RoutingDecision
{
    public required string SelectedNodeId { get; init; }
    public required string SelectedModelId { get; init; }
    public required RoutingScore WinningScore { get; init; }
    public IReadOnlyList<RoutingScore> AllScores { get; init; } = [];
    public required RoutingPolicy AppliedPolicy { get; init; }
    public DateTimeOffset DecidedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan DecisionDuration { get; init; }
}
Weighted Scoring Formula
Dimension	Weight (default)	Scoring Logic
VRAM Availability	0.25	100 if model loaded; max(0, (available - required) / available × 100) if loadable; 0 if insufficient
Queue Depth	0.20	max(0, 100 - (pendingRequests / maxConcurrent × 100))
Latency	0.20	max(0, 100 - (latencyMs / 500 × 100)) — 500ms baseline
Capability Match	0.15	100 if all required capabilities present; 0 if any missing (hard filter)
Affinity	0.20	100 if model already loaded on node; 0 otherwise (avoids cold-load latency)
WeightedTotal = (VramScore × 0.25) + (QueueDepthScore × 0.20)
              + (LatencyScore × 0.20) + (CapabilityScore × 0.15)
              + (AffinityScore × 0.20)
Routing Engine Interface
namespace SplitBrain.Core.Abstractions;

public interface IRoutingEngine
{
    Task<RoutingDecision> RouteAsync(
        TaskRequirements requirements,
        RoutingPolicy? policyOverride = null,
        CancellationToken ct = default);

    IReadOnlyList<RoutingScore> ScoreAll(TaskRequirements requirements);
}
5.2 Routing Policies
Policy	Behavior	Weight Override	Use Case
LatencyFirst	Maximize speed of response	Latency: 0.40, Affinity: 0.30, Queue: 0.20, VRAM: 0.10	Interactive chat, IDE autocomplete
QualityFirst	Maximize model capability	Capability: 0.35, VRAM: 0.30, Latency: 0.15, Queue: 0.10, Affinity: 0.10	Deep code review, reasoning tasks
BalancedLoad	Distribute evenly across nodes	Queue: 0.35, VRAM: 0.25, Latency: 0.20, Capability: 0.10, Affinity: 0.10	Batch processing, sustained load
Pinned	Force specific node+model	N/A — bypasses scoring, direct selection	Debugging, A/B testing
namespace SplitBrain.Core.Abstractions;

public enum RoutingPolicy
{
    LatencyFirst,
    QualityFirst,
    BalancedLoad,
    Pinned
}

public interface IRoutingPolicy
{
    RoutingPolicy PolicyType { get; }
    RoutingWeights GetWeights();
}

public sealed record RoutingWeights
{
    public double Vram { get; init; }
    public double QueueDepth { get; init; }
    public double Latency { get; init; }
    public double Capability { get; init; }
    public double Affinity { get; init; }
}
6. Resilience Patterns with Polly v8
6.1 Per-Node Resilience Pipeline
Each Ollama node connection gets a dedicated Polly v8 resilience pipeline registered via Microsoft.Extensions.Http.Resilience. The pipeline layers four strategies in order: Timeout → Retry → Circuit Breaker → Fallback.

namespace SplitBrain.Networking.Resilience;

public static class ResilienceExtensions
{
    public static IHttpClientBuilder AddOllamaResiliencePipeline(
        this IHttpClientBuilder builder,
        NodeConfiguration nodeConfig)
    {
        builder.AddResilienceHandler($"ollama-{nodeConfig.NodeId}", (pipelineBuilder, context) =>
        {
            // 1. Outer timeout — absolute maximum for the entire operation
            pipelineBuilder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(120),
                Name = $"{nodeConfig.NodeId}-outer-timeout"
            });

            // 2. Retry with exponential backoff + jitter
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode is
                        HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.ServiceUnavailable
                    || args.Outcome.Exception is HttpRequestException
                         or TimeoutRejectedException),
                OnRetry = static args =>
                {
                    // Log retry attempt for observability
                    return ValueTask.CompletedTask;
                }
            });

            // 3. Circuit breaker — open after consecutive failures
            pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.8,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Result?.IsSuccessStatusCode == false
                    || args.Outcome.Exception is not null),
                Name = $"{nodeConfig.NodeId}-circuit-breaker"
            });

            // 4. Inner timeout per attempt
            pipelineBuilder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                Name = $"{nodeConfig.NodeId}-attempt-timeout"
            });
        });

        return builder;
    }
}
Timeout Tiers by Operation Type
Operation	Attempt Timeout	Outer Timeout	Rationale
Health Check (/api/tags, /api/ps)	10s	15s	Fast probe — unresponsive node should be marked unhealthy quickly
Embedding (/api/embed)	30s	45s	Embeddings are fast; long delays indicate a problem
Chat / Generate (/api/chat, /api/generate)	60s	120s	LLM generation can be slow with large contexts
Agent Step (multi-turn)	90s	180s	Agent iterations involve tool calls + inference
6.2 Graceful Degradation
Failure Scenario	System Response	User Impact
Single node goes down	Circuit breaker opens. Routing engine excludes node. All traffic redirected to remaining healthy nodes via fallback chain.	Possible latency increase. Transparent to user if fallback models are available.
All nodes down	Requests queued with configurable TTL (default 60s). Dashboard alert raised. SystemAlert pushed via SignalR.	Requests timeout with clear error message. No silent failures.
Specific model unavailable	Fallback chain cascades to next model. ModelRegistry marks model as unavailable until next health check confirms availability.	Slightly different model used. Decision logged in routing audit trail.
VRAM exhaustion on node	Routing engine detects low VRAM score. Prefers smaller quantized variants. Non-critical tasks (embeddings, background agents) are deferred.	Quality model may be swapped for quantized variant during peak load.
Circuit Breaker State Visibility

Circuit breaker state transitions (Closed → Open → Half-Open → Closed) are logged as structured events with NodeId and pushed to the dashboard in real time. This is critical for diagnosing intermittent node failures.

7. Blazor Dashboard & Real-Time Logging
7.1 Dashboard Architecture
Blazor Server is chosen over Blazor WASM because the dashboard runs on the orchestrator host and requires direct access to DI-registered services (INodeRegistry, IModelRegistry, etc.) without serialization overhead. SignalR provides the real-time push channel.

Strongly-Typed Hub Interface
namespace SplitBrain.Core.Abstractions;

public interface IDashboardClient
{
    Task ReceiveNodeHealthUpdate(NodeHealthSnapshot snapshot);
    Task ReceiveLogEntry(StructuredLogEntry entry);
    Task ReceiveTaskUpdate(TaskStatusUpdate update);
    Task ReceiveMetricUpdate(MetricSnapshot snapshot);
    Task ReceiveAlert(SystemAlert alert);
}

public sealed record NodeHealthSnapshot(
    string NodeId,
    string NodeName,
    NodeHealthStatus Health);

public sealed record TaskStatusUpdate(
    string TaskId,
    string TaskType,
    string Status,          // "Routing", "Inferring", "Complete", "Failed"
    string? NodeId,
    string? ModelId,
    TimeSpan? Duration,
    RoutingDecision? Decision);

public sealed record MetricSnapshot(
    string MetricName,
    double Value,
    DateTimeOffset Timestamp,
    Dictionary<string, string> Tags);

public sealed record SystemAlert(
    string AlertId,
    string Severity,        // "Info", "Warning", "Critical"
    string Title,
    string Message,
    DateTimeOffset Timestamp);
SignalR Hub
namespace SplitBrain.Dashboard.Hubs;

public sealed class DashboardHub : Hub<IDashboardClient>
{
    private readonly INodeRegistry _nodeRegistry;

    public DashboardHub(INodeRegistry nodeRegistry)
        => _nodeRegistry = nodeRegistry;

    public override async Task OnConnectedAsync()
    {
        // Send current cluster state to newly connected client
        foreach (var node in _nodeRegistry.GetAllNodes())
        {
            var health = _nodeRegistry.GetNodeHealth(node.NodeId);
            if (health is not null)
            {
                await Clients.Caller.ReceiveNodeHealthUpdate(
                    new NodeHealthSnapshot(node.NodeId, node.Name, health));
            }
        }

        await base.OnConnectedAsync();
    }
}
7.2 Dashboard Pages
Page	Route	Purpose	Key Components
Overview	/	Cluster health at a glance	Node status cards (green/yellow/red), VRAM usage gauges per node, aggregate request counter, active model list, system alert banner
Nodes	/nodes	Detailed per-node view	Per-node model list with loaded/available status, queue depth indicator, latency history (tabular), VRAM breakdown, circuit breaker state, health check log
Tasks	/tasks	Active and historical tasks	Active task list with real-time status, completed task history (filterable by type, model, node), routing decision detail panel showing all scores and reasoning trail
Logs	/logs	Real-time structured log viewer	Streaming log entries via SignalR, level filter (Debug through Fatal), search by message/NodeId/TaskId/CorrelationId, auto-scroll with pause, JSON property expansion
Settings	/settings	Runtime configuration	Node enable/disable toggle, routing policy selector, log level adjustment, fallback chain editor, model priority override, health check interval tuning
7.3 Logging Architecture
The observability stack is organized into three layers, each serving a distinct purpose in the telemetry pipeline.

Layer 1: Serilog — Structured Logging Framework
Serilog serves as the primary logging framework with structured logging enrichers that automatically attach contextual properties to every log event.

Enricher	Property	Source
NodeIdEnricher	NodeId	AsyncLocal from routing context
TaskIdEnricher	TaskId	AsyncLocal from task execution scope
ModelIdEnricher	ModelId	Set during inference calls
CorrelationIdEnricher	CorrelationId	Propagated from MCP request or HTTP header
Sinks Configuration:

Console — Development only, colored output with structured properties
File — Rolling daily files with 7-day retention, JSON format
Seq — Optional centralized log server for advanced querying
Custom SignalR Sink — Pushes to dashboard in real time (Layer 2)
Layer 2: Custom SignalR Sink
namespace SplitBrain.Observability.Sinks;

public sealed class SignalRLogSink : ILogEventSink, IDisposable
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hubContext;
    private readonly LogEventLevel _minimumLevel;
    private readonly Channel<StructuredLogEntry> _channel;
    private readonly Task _processingTask;

    public SignalRLogSink(
        IHubContext<DashboardHub, IDashboardClient> hubContext,
        LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        _hubContext = hubContext;
        _minimumLevel = minimumLevel;
        _channel = Channel.CreateBounded<StructuredLogEntry>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

        _processingTask = Task.Run(ProcessBatchAsync);
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < _minimumLevel) return;

        var entry = new StructuredLogEntry
        {
            Timestamp = logEvent.Timestamp,
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessage(),
            NodeId = GetProperty(logEvent, "NodeId"),
            TaskId = GetProperty(logEvent, "TaskId"),
            ModelId = GetProperty(logEvent, "ModelId"),
            CorrelationId = GetProperty(logEvent, "CorrelationId"),
            Properties = logEvent.Properties
                .ToDictionary(p => p.Key, p => (object?)p.Value.ToString()),
            ExceptionDetails = logEvent.Exception?.ToString()
        };

        _channel.Writer.TryWrite(entry);
    }

    private async Task ProcessBatchAsync()
    {
        var batch = new List<StructuredLogEntry>(50);

        await foreach (var entry in _channel.Reader.ReadAllAsync())
        {
            batch.Add(entry);

            // Drain up to 50 entries per batch
            while (batch.Count < 50
                && _channel.Reader.TryRead(out var additional))
            {
                batch.Add(additional);
            }

            foreach (var item in batch)
            {
                await _hubContext.Clients.All.ReceiveLogEntry(item);
            }

            batch.Clear();
            await Task.Delay(100); // Throttle to avoid overwhelming clients
        }
    }

    private static string? GetProperty(LogEvent evt, string name) =>
        evt.Properties.TryGetValue(name, out var val)
            ? val.ToString().Trim('"')
            : null;

    public void Dispose() => _channel.Writer.Complete();
}
Layer 3: OpenTelemetry — Distributed Tracing & Metrics
namespace SplitBrain.Observability;

public static class Telemetry
{
    public static readonly ActivitySource Source =
        new("SplitBrain.AI", "1.0.0");

    public static readonly Meter Meter =
        new("SplitBrain.AI", "1.0.0");

    // Counters
    public static readonly Counter<long> RequestsTotal =
        Meter.CreateCounter<long>("splitbrain.requests.total");
    public static readonly Counter<long> RoutingDecisions =
        Meter.CreateCounter<long>("splitbrain.routing.decisions");
    public static readonly Counter<long> FallbacksTriggered =
        Meter.CreateCounter<long>("splitbrain.fallbacks.triggered");

    // Histograms
    public static readonly Histogram<double> InferenceLatency =
        Meter.CreateHistogram<double>("splitbrain.inference.latency_ms");
    public static readonly Histogram<double> RoutingLatency =
        Meter.CreateHistogram<double>("splitbrain.routing.latency_ms");

    // Gauges (via ObservableGauge with callbacks wired in DI)
}
Trace spans are created for each major operation:

Span Name	Parent	Key Attributes
splitbrain.mcp.tool_call	Root	tool.name, correlation.id
splitbrain.routing.decide	tool_call	policy, candidates.count, selected.node, selected.model
splitbrain.inference.execute	routing	node.id, model.id, tokens.in, tokens.out
splitbrain.agent.step	tool_call	agent.iteration, agent.state, tools.called
Exporters: Console (development), OTLP (production, compatible with Jaeger/Grafana Tempo), Prometheus (optional scrape endpoint for metrics).

7.4 Log Entry Model for Dashboard
namespace SplitBrain.Core.Models;

public sealed record StructuredLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public required string Level { get; init; }         // Information, Warning, Error, etc.
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? TaskId { get; init; }
    public string? ModelId { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, object?> Properties { get; init; } = new();
    public string? ExceptionDetails { get; init; }
}
8. MCP Server Integration
The Model Context Protocol layer exposes SplitBrain.AI's capabilities as MCP tools and resources, enabling integration with any MCP-compatible client (IDEs, agents, CLI tools).

Aspect	Implementation
SDK	ModelContextProtocol NuGet package (v1.2.0) — official C# SDK maintained in collaboration with Microsoft
Transport	Streamable HTTP (default, via ModelContextProtocol.AspNetCore) + stdio for local IDE integration
Tools Exposed	CodeReview, Refactor, GenerateTests, RunAgent, Embed, Chat
Resources Exposed	nodes://health — live node health; models://registry — available models; tasks://history — recent task log
Routing Integration	Every MCP tool call flows through IRoutingEngine. The tool invocation creates a TaskRequirements record, obtains a RoutingDecision, and executes inference on the selected node+model.
// Registration in SplitBrain.Host/Program.cs
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new("SplitBrain.AI", "1.0.0");
})
.WithStreamableHttpTransport()
.WithToolsFromAssembly(typeof(CodeReviewTool).Assembly)
.WithResourcesFromAssembly(typeof(NodeHealthResource).Assembly);
// Example MCP Tool — CodeReview
namespace SplitBrain.MCP.Tools;

[McpServerTool(Name = "CodeReview")]
public sealed class CodeReviewTool(
    IRoutingEngine router,
    IFallbackChainProvider fallbackChains)
{
    [McpServerTool, Description("Review code for issues, patterns, and improvements")]
    public async Task<string> ExecuteAsync(
        [Description("The source code to review")] string code,
        [Description("Programming language")] string language = "csharp",
        CancellationToken ct = default)
    {
        var requirements = new TaskRequirements
        {
            RequiredCapabilities = ModelCapability.Chat | ModelCapability.CodeGeneration
        };

        var decision = await router.RouteAsync(requirements, ct: ct);

        // Execute inference on selected node+model via fallback chain
        // ... implementation
    }
}
9. Implementation Phases
Phase 1: Foundation
Goal: Establish the solution structure, configurable networking, and node health monitoring.

Attribute	Detail
Deliverables	SplitBrain.Core (all domain models, interfaces, enums), SplitBrain.Networking (NodeConfiguration, NodeRegistry, OllamaNodeFactory, NodeHealthCheckService), SplitBrain.Host (minimal host with DI), config/nodes.json, SplitBrain.Core.Tests, SplitBrain.Networking.Tests
Dependencies	None — greenfield start
Complexity	Medium
Success Criteria	Host starts, loads node config from nodes.json, discovers and health-checks configured Ollama nodes, logs node status to console. Hot-reload works: editing nodes.json updates the registry without restart.
Phase 2: Model Management & Routing
Goal: Build the intelligent routing engine with fallback chains and resilience pipelines.

Attribute	Detail
Deliverables	SplitBrain.Models (ModelDefinition, ModelRegistry, FallbackChainProvider), SplitBrain.Routing (RoutingEngine, scoring algorithm, routing policies), Polly resilience pipelines in Networking, SplitBrain.Routing.Tests
Dependencies	Phase 1 (Core, Networking)
Complexity	High
Success Criteria	Can submit a test prompt, routing engine scores all candidates, selects best node+model, executes inference. If primary fails, Polly retries and fallback chain cascades to next option. Routing decision logged with full score breakdown.
Phase 3: Observability & Dashboard
Goal: Full observability stack and a live monitoring dashboard.

Attribute	Detail
Deliverables	SplitBrain.Observability (Serilog config, enrichers, SignalRLogSink, OpenTelemetry ActivitySource and Meters), SplitBrain.Dashboard (Blazor Server app, DashboardHub, Overview page, Nodes page, Logs page)
Dependencies	Phase 1 (Networking for health data), Phase 2 (Routing for decision data)
Complexity	High
Success Criteria	Dashboard loads in browser at /, shows live node health cards with VRAM gauges. Logs page streams structured log entries in real time via SignalR. OpenTelemetry traces appear in console exporter for routing and inference spans.
Phase 4: Agent Engine & MCP
Goal: Bounded agent execution and MCP server exposing all capabilities.

Attribute	Detail
Deliverables	SplitBrain.Agents (agent state machine with configurable iteration and token limits, tool execution orchestration), SplitBrain.MCP (MCP server with tool/resource registration, Streamable HTTP + stdio transports), SplitBrain.Integration.Tests
Dependencies	Phase 2 (Routing for task execution), Phase 3 (Observability for tracing agent steps)
Complexity	High
Success Criteria	Can execute a bounded agent task (e.g., CodeReview) via MCP tool call from an external client. Agent runs with iteration limits, routes through engine, returns structured result. Trace shows full span tree: MCP → Routing → Inference → Agent Steps.
Phase 5: Polish & Production Hardening
Goal: Production readiness with persistence, alerting, and deployment automation.

Attribute	Detail
Deliverables	Dashboard Settings page (runtime config), Tasks page with history, task persistence via LiteDB, alerting rules engine (VRAM thresholds, node failure alerts), comprehensive integration test suite, docker-compose.yml for multi-node dev setup, documentation
Dependencies	All previous phases
Complexity	Medium
Success Criteria	Full end-to-end workflow: MCP client sends CodeReview tool call → routing engine selects best node+model → inference executes with Polly resilience → result returned → task persisted to LiteDB → dashboard shows task in history with full routing decision audit trail → logs streamed in real time.
Phase Summary Timeline
Phase	Name	Complexity	Dependencies	Key Milestone
1	Foundation	Medium	—	Nodes discovered and health-checked
2	Model Management & Routing	High	Phase 1	Intelligent routing with fallback
3	Observability & Dashboard	High	Phase 1, 2	Live dashboard with log streaming
4	Agent Engine & MCP	High	Phase 2, 3	MCP tool calls executing bounded agents
5	Polish & Production	Medium	All	Full end-to-end with persistence and alerting
10. Key NuGet Packages
Package	Purpose	Version	Used In
OllamaSharp	Ollama API client — chat, generate, embed, model management	5.4.x	Networking
ModelContextProtocol	Official MCP C# SDK — server, tools, resources	1.2.x	MCP
ModelContextProtocol.AspNetCore	Streamable HTTP transport for MCP server	1.2.x	Host
Polly.Core	Resilience pipeline builder (v8 API)	8.x	Networking
Microsoft.Extensions.Http.Resilience	HttpClient + Polly v8 integration	8.x / 9.x	Networking
Microsoft.Extensions.Http.Polly	Legacy Polly HttpClient integration (if needed)	8.x / 9.x	Networking
Serilog.AspNetCore	Serilog integration with ASP.NET Core hosting	8.x+	Host, Observability
Serilog.Sinks.File	Rolling file sink with retention policies	Latest stable	Observability
Serilog.Sinks.Seq	Seq structured log server sink (optional)	Latest stable	Observability
Serilog.Sinks.Console	Colored console output for development	Latest stable	Observability
OpenTelemetry	Distributed tracing and metrics SDK	Latest stable	Observability
OpenTelemetry.Exporter.Console	Dev tracing/metrics output to console	Latest stable	Observability
OpenTelemetry.Exporter.OpenTelemetryProtocol	OTLP exporter (Jaeger, Grafana Tempo, etc.)	Latest stable	Observability
OpenTelemetry.Extensions.Hosting	Host integration for OTel providers	Latest stable	Host
OpenTelemetry.Instrumentation.AspNetCore	Auto-instrument ASP.NET Core requests	Latest stable	Host
OpenTelemetry.Instrumentation.Http	Auto-instrument outbound HttpClient calls	Latest stable	Networking
Microsoft.AspNetCore.SignalR	Real-time push to Blazor dashboard	Built into ASP.NET Core	Dashboard
LiteDB	Embedded NoSQL database for task history persistence	5.x	Host / Dashboard
11. Configuration Examples
Complete appsettings.json
{
  "SplitBrain": {
    "Nodes": [
      {
        "NodeId": "node-rtx5060",
        "Name": "Fast Node (RTX 5060)",
        "Host": "192.168.1.100",
        "Port": 11434,
        "Role": "Fast",
        "Priority": 10,
        "MaxConcurrentRequests": 6,
        "Tags": ["code", "chat", "fast-inference"],
        "HealthCheckIntervalSeconds": 15,
        "Enabled": true
      },
      {
        "NodeId": "node-gtx1080",
        "Name": "Deep Node (GTX 1080)",
        "Host": "192.168.1.101",
        "Port": 11434,
        "Role": "Deep",
        "Priority": 20,
        "MaxConcurrentRequests": 2,
        "Tags": ["reasoning", "code-review", "embedding"],
        "HealthCheckIntervalSeconds": 30,
        "Enabled": true
      },
      {
        "NodeId": "node-cloud-fallback",
        "Name": "Cloud Fallback",
        "Host": "ollama.remote.example.com",
        "Port": 443,
        "Role": "Standby",
        "Priority": 999,
        "MaxConcurrentRequests": 8,
        "Tags": ["code", "reasoning", "chat", "embedding"],
        "HealthCheckIntervalSeconds": 60,
        "Enabled": true
      }
    ],

    "Models": [
      {
        "ModelId": "qwen2.5-coder:7b-q5_K_M",
        "DisplayName": "Qwen 2.5 Coder 7B (Q5)",
        "Family": "Qwen",
        "Capabilities": "Chat, CodeGeneration",
        "QuantizationLevel": "Q5_K_M",
        "ContextWindow": 32768,
        "VramRequirementMB": 5800,
        "PreferredNodes": ["node-rtx5060"],
        "IsDefault": true
      },
      {
        "ModelId": "qwen2.5-coder:7b-q4_K_M",
        "DisplayName": "Qwen 2.5 Coder 7B (Q4)",
        "Family": "Qwen",
        "Capabilities": "Chat, CodeGeneration",
        "QuantizationLevel": "Q4_K_M",
        "ContextWindow": 32768,
        "VramRequirementMB": 4700,
        "PreferredNodes": ["node-rtx5060"],
        "IsDefault": false
      },
      {
        "ModelId": "deepseek-r1:7b",
        "DisplayName": "DeepSeek R1 7B",
        "Family": "DeepSeek",
        "Capabilities": "Chat, Reasoning, CodeGeneration",
        "QuantizationLevel": "Q4_K_M",
        "ContextWindow": 65536,
        "VramRequirementMB": 5200,
        "PreferredNodes": ["node-gtx1080"],
        "IsDefault": false
      },
      {
        "ModelId": "nomic-embed-text:latest",
        "DisplayName": "Nomic Embed Text",
        "Family": "Nomic",
        "Capabilities": "Embedding",
        "QuantizationLevel": "FP16",
        "ContextWindow": 8192,
        "VramRequirementMB": 550,
        "PreferredNodes": ["node-gtx1080"],
        "IsDefault": false
      }
    ],

    "FallbackChains": [
      {
        "TaskType": "CodeReview",
        "Chain": [
          { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 120 },
          { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 120 },
          { "ModelId": "deepseek-r1:7b",           "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 180 },
          { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeId": "node-cloud-fallback", "TimeoutSeconds": 90 }
        ]
      },
      {
        "TaskType": "TestGeneration",
        "Chain": [
          { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 120 },
          { "ModelId": "deepseek-r1:7b",           "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 150 }
        ]
      },
      {
        "TaskType": "Embedding",
        "Chain": [
          { "ModelId": "nomic-embed-text:latest", "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 30 },
          { "ModelId": "nomic-embed-text:latest", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 30 }
        ]
      },
      {
        "TaskType": "Chat",
        "Chain": [
          { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeId": "node-rtx5060", "TimeoutSeconds": 90 },
          { "ModelId": "deepseek-r1:7b",           "PreferredNodeId": "node-gtx1080", "TimeoutSeconds": 120 },
          { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeId": "node-cloud-fallback", "TimeoutSeconds": 60 }
        ]
      }
    ],

    "Routing": {
      "DefaultPolicy": "BalancedLoad",
      "Weights": {
        "Vram": 0.25,
        "QueueDepth": 0.20,
        "Latency": 0.20,
        "Capability": 0.15,
        "Affinity": 0.20
      }
    },

    "Dashboard": {
      "SignalR": {
        "MaximumReceiveMessageSize": 131072,
        "KeepAliveIntervalSeconds": 15,
        "ClientTimeoutSeconds": 60
      },
      "LogStreaming": {
        "MinimumLevel": "Information",
        "MaxBufferSize": 1000,
        "BatchIntervalMs": 100
      }
    },

    "Agents": {
      "MaxIterationsPerTask": 10,
      "MaxTokensPerTask": 50000,
      "DefaultTimeoutSeconds": 300
    }
  },

  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Sinks.File",
      "Serilog.Sinks.Seq"
    ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "System.Net.Http": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/splitbrain-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
        }
      },
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  },

  "OpenTelemetry": {
    "ServiceName": "SplitBrain.AI",
    "Tracing": {
      "Exporter": "Console",
      "OtlpEndpoint": "http://localhost:4317"
    },
    "Metrics": {
      "Exporter": "Console",
      "PrometheusEndpoint": "/metrics"
    }
  }
}
Configuration Layering

The host loads configuration in this order: appsettings.json → appsettings.{Environment}.json → nodes.json → environment variables → command-line args. Node topology can be split into nodes.json for independent management, or inlined into appsettings.json. Environment variables use the SplitBrain__Nodes__0__Host double-underscore format for hierarchical keys.

12. Appendix: Key Interfaces Reference
Interface	Project	Purpose
INodeRegistry	Core (defined) / Networking (impl)	Node state management, discovery, health state tracking, hot-reload support, node state change events
IOllamaNode	Core (defined) / Networking (impl)	Single Ollama instance connection — wraps OllamaSharp for chat, generate, embed, model listing
IOllamaNodeFactory	Core (defined) / Networking (impl)	Creates and caches IOllamaNode instances from NodeConfiguration
INodeHealthService	Core (defined) / Networking (impl)	Background health monitoring — polls Ollama endpoints, tracks VRAM, latency, model availability
IModelRegistry	Core (defined) / Models (impl)	Model definition management, live availability cross-referencing with node health data
IFallbackChainProvider	Core (defined) / Models (impl)	Fallback chain resolution — returns ordered list of model+node pairs filtered by current availability
IRoutingEngine	Core (defined) / Routing (impl)	Task-to-node+model routing with multi-dimensional scoring and configurable policy selection
IRoutingPolicy	Core (defined) / Routing (impl)	Pluggable routing strategy — provides weight overrides for scoring dimensions
IDashboardClient	Core	SignalR strongly-typed client — defines all real-time push methods for the Blazor dashboard
IAgentEngine	Core (defined) / Agents (impl)	Bounded agent execution — manages state machine with iteration/token limits, tool orchestration
SplitBrain.AI — Architectural Implementation Plan v1.0 — April 2026
This document is a living artifact. Update as implementation decisions evolve.