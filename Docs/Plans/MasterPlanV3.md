# SplitBrain.AI - Unified Architecture Plan v3

Distributed Home AI Orchestrator on .NET 10

Author: Lucas & Copilot | Version: 2.0 | Date: April 22, 2026  
Repository: github.com/TheMasonX/SplitBrain.AI | Status: Production Blueprint

# 1\. Executive Summary

- **.NET 10 centralized orchestrator with dynamic, provider-agnostic node topology.** The system supports any number of inference nodes - local GPU machines running Ollama, cloud endpoints via GitHub Copilot SDK, or future providers - all defined in a single JSON configuration file with zero code changes required to add, remove, or reconfigure nodes.
- **JSON-driven node configuration (Ollama + GitHub Copilot SDK + extensible providers).** A dedicated nodes.json file is the source of truth for the entire compute topology. Each node declares its provider type, role, priority, concurrency limits, and provider-specific settings. The system hot-reloads on file change.
- **Blazor Server dashboard for real-time monitoring AND node/model/fallback management.** The dashboard is not a read-only monitoring tool - it is the management plane. Operators add, edit, and remove nodes through a form UI that writes directly to nodes.json. Fallback chains, routing weights, and model assignments are all editable from the browser.
- **Semantic Kernel agents with bounded state machine and deterministic event log.** Agent execution follows a strict state flow (INIT → PLAN → IMPLEMENT → REVIEW → TEST → DONE | FAIL) with hard limits of 4 iterations and 12,000 tokens per loop. Every agent decision is captured in an append-only event log backed by LiteDB for post-mortem replay.
- **Full observability: Serilog + OpenTelemetry + agent step replay + token cost tracking.** Structured logs flow through Serilog with a custom SignalR sink for live dashboard streaming. OpenTelemetry provides distributed tracing and metrics. Token consumption is tracked per-request with divide-by-zero-guarded throughput calculations and estimated cost for cloud nodes.

**Design Principles (Invariants)**

**1.** Stability > raw intelligence. **2.** Latency-sensitive tasks are isolated. **3.** All capabilities exposed through MCP (single system boundary). **4.** Models treated as constrained compute resources. **5.** Agent autonomy is bounded and observable.

# 2\. Solution Structure

The solution contains **two deployable services** and **eight shared libraries**. The Orchestrator is the brain; Workers are lightweight proxies that run on remote inference hardware.

## 2.1 Repository Layout

SplitBrain.AI/ ├── src/ │ ├── SplitBrain.Core/ # Domain models, interfaces, enums, DTOs, constants │ ├── SplitBrain.Networking/ # Node registry, health checks, provider adapters │ ├── SplitBrain.Routing/ # Scoring engine, routing policies, fallback chains │ ├── SplitBrain.Models/ # Model definitions, model registry, capability mapping │ ├── SplitBrain.Agents/ # Semantic Kernel agent orchestration, state machine │ ├── SplitBrain.Validation/ # Output validation pipeline, built-in validators │ ├── SplitBrain.Observability/ # Serilog sinks, OpenTelemetry, metrics, SignalR sink │ ├── SplitBrain.MCP/ # MCP server, tool registration, idempotency │ ├── SplitBrain.Dashboard/ # Blazor Server components, SignalR hub, pages │ ├── SplitBrain.Orchestrator/ ← ASP.NET Core host (primary node) │ └── SplitBrain.Worker/ ← Worker service (remote inference nodes) ├── tests/ │ ├── SplitBrain.Core.Tests/ │ ├── SplitBrain.Networking.Tests/ │ ├── SplitBrain.Routing.Tests/ │ └── SplitBrain.Integration.Tests/ ├── config/ │ ├── appsettings.json # Models, fallback chains, routing, agents, MCP │ ├── appsettings.Development.json # Dev overrides │ └── nodes.json ← Dynamic node topology (source of truth) ├── deploy/ │ ├── setup-orchestrator.ps1 │ ├── setup-worker.ps1 │ └── publish/ │ ├── Orchestrator.pubxml │ └── Worker.pubxml ├── docs/ │ └── architecture-v2.md ├── SplitBrain.AI.sln ├── .gitignore ├── LICENSE └── README.md

## 2.2 Project Responsibility Matrix

| **Project**                  | **Responsibility**                                                                                                                                                                                  | **Dependencies**                                 |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| **SplitBrain.Core**          | Domain models (NodeConfiguration, InferenceRequest, InferenceResult), interfaces (IInferenceNode, INodeRegistry), enums (NodeProviderType, NodeRole, TaskType, HealthState), DTOs, shared constants | None (leaf dependency)                           |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Networking**    | IInferenceNodeFactory, OllamaInferenceNode, CopilotInferenceNode, NodeRegistry, NodeHealthCheckService, provider adapter implementations                                                            | Core, OllamaSharp, GitHub.Copilot.SDK, Polly     |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Routing**       | IRoutingEngine, scoring algorithm, hard routing rules, IRequestQueue with per-node SemaphoreSlim, backpressure detection, fallback chain resolution                                                 | Core, Models                                     |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Models**        | ModelDefinition, IModelRegistry, IFallbackChainProvider, model-to-node affinity, capability mapping                                                                                                 | Core                                             |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Agents**        | Semantic Kernel integration, ChatCompletionAgent wrappers, bounded state machine (INIT→DONE\|FAIL), IAgentEventLog, step replay                                                                     | Core, Models, Routing, Microsoft.SemanticKernel  |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Validation**    | IOutputValidator pipeline, StructuredOutputValidator, CodeSyntaxValidator, LengthBoundsValidator, RefusalDetector                                                                                   | Core                                             |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Observability** | Serilog configuration, SignalRLogSink, OpenTelemetry ActivitySource + metrics, TokenUsageRecord tracking                                                                                            | Core, Serilog, OpenTelemetry                     |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.MCP**           | MCP server via official SDK, tool registration (review_code, refactor_code, etc.), IIdempotencyCache, streamable HTTP + stdio transport                                                             | Core, Routing, Agents, ModelContextProtocol      |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Dashboard**     | Blazor Server Razor components, DashboardHub (SignalR), strongly-typed IDashboardClient, 6 pages: Overview, Nodes, Models, Tasks, Logs, Settings                                                    | Core, Networking, Routing, Models, Observability |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Orchestrator**  | ASP.NET Core host. DI composition root. Hosts MCP server, routing engine, Blazor dashboard, agent engine. Runs on the primary node.                                                                 | All libraries                                    |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |
| **SplitBrain.Worker**        | Lightweight worker service for remote inference nodes. Exposes gRPC/minimal API for orchestrator communication. Manages local Ollama lifecycle, health reporting.                                   | Core, Networking, Observability                  |
| ---                          | ---                                                                                                                                                                                                 | ---                                              |

# 3\. Dynamic Node Configuration

This is the core architectural innovation. Nodes are **never hardcoded**. The entire compute topology is defined in nodes.json, hot-reloaded on change, and fully manageable from the Blazor dashboard. Adding a fourth, fifth, or tenth node is a JSON edit - no code changes, no recompilation, no redeployment.

## 3.1 Node Configuration Model

namespace SplitBrain.Core.Configuration; /// &lt;summary&gt; /// Provider type determines which adapter creates the IInferenceNode. /// Extend this enum when adding new provider integrations. /// &lt;/summary&gt; public enum NodeProviderType { Ollama, // Local Ollama instance (any machine on the network) CopilotSdk, // GitHub Copilot SDK (cloud) // Future: OpenAI, Anthropic, AzureOpenAI, LocalLlama, vLLM, etc. } /// &lt;summary&gt; /// Role governs hard routing rules. Fast nodes handle latency-sensitive work. /// Deep nodes handle complex reasoning. Hybrid can do both. Standby is reserve. /// &lt;/summary&gt; public enum NodeRole { Fast, // Low-latency interactive tasks (autocomplete, chat) Deep, // Complex reasoning, review, validation, test generation Hybrid, // Can handle both - scored by the routing engine Standby // Available but not actively routed to unless all others fail } /// &lt;summary&gt; /// Complete configuration for a single inference node. /// Serialized to/from nodes.json. Immutable record for thread safety. /// &lt;/summary&gt; public record NodeConfiguration { public required string NodeId { get; init; } public required string DisplayName { get; init; } public required NodeProviderType Provider { get; init; } public required NodeRole Role { get; init; } public int Priority { get; init; } = 100; // Lower = preferred public int MaxConcurrentRequests { get; init; } = 2; public List&lt;string&gt; Tags { get; init; } = \[\]; // "code", "reasoning", "embedding" public bool Enabled { get; init; } = true; public int HealthCheckIntervalMs { get; init; } = 2000; // Provider-specific configuration (polymorphic by convention) public OllamaProviderConfig? Ollama { get; init; } public CopilotProviderConfig? Copilot { get; init; } }

### 3.1.1 Ollama Provider Configuration

/// &lt;summary&gt; /// Ollama-specific settings per node. Maps directly to Ollama environment /// variables and API behavior. /// &lt;/summary&gt; public record OllamaProviderConfig { public required string Host { get; init; } // "localhost" or "192.168.1.100" public int Port { get; init; } = 11434; public string BaseUrl => \$"http://{Host}:{Port}"; // Ollama environment variable equivalents (set on the remote machine) public int NumParallel { get; init; } = 2; // OLLAMA_NUM_PARALLEL public int MaxLoadedModels { get; init; } = 2; // OLLAMA_MAX_LOADED_MODELS public bool FlashAttention { get; init; } = true; // OLLAMA_FLASH_ATTENTION // Per-node timeout (from spec: Node A=5s, Node B=20s) public int TimeoutSeconds { get; init; } = 10; // Static VRAM capacity - Ollama /api/ps only reports per-model size_vram, // NOT total GPU memory. This field enables VRAM utilization percentage // calculations in the dashboard and routing engine. public long GpuVramTotalMB { get; init; } }

**Why GpuVramTotalMB is static config**

The Ollama API (/api/ps) reports size_vram per loaded model, but does **not** expose total GPU memory. To calculate VRAM utilization percentage (critical for routing and dashboard gauges), total VRAM must come from static configuration. This is set once per node and rarely changes.

### 3.1.2 Copilot SDK Provider Configuration

/// &lt;summary&gt; /// GitHub Copilot SDK settings. Supports both stdio and TCP transport. /// Auth resolution: Azure Key Vault → env var COPILOT_API_KEY → GitHub CLI. /// &lt;/summary&gt; public record CopilotProviderConfig { public string? CliPath { get; init; } // Path to gh copilot binary public string? CliUrl { get; init; } // URL of pre-running server public bool UseStdio { get; init; } = true; // stdio vs TCP transport public string DefaultModel { get; init; } = "gpt-4o"; // Auth resolution order: KeyVault → env var → GitHub CLI public string? KeyVaultUri { get; init; } public string? KeyVaultSecretName { get; init; } public int TimeoutSeconds { get; init; } = 30; }

## 3.2 Complete nodes.json

This file defines Lucas's current 3-node topology. Adding a fourth node is a matter of appending another object to the Nodes array.

{ "Nodes": \[ { "NodeId": "home-laptop", "DisplayName": "RTX 5060 Laptop", "Provider": "Ollama", "Role": "Fast", "Priority": 10, "MaxConcurrentRequests": 2, "Tags": \["code", "chat", "orchestration"\], "Enabled": true, "HealthCheckIntervalMs": 2000, "Ollama": { "Host": "localhost", "Port": 11434, "NumParallel": 2, "MaxLoadedModels": 2, "FlashAttention": true, "TimeoutSeconds": 5, "GpuVramTotalMB": 8192 } }, { "NodeId": "tower-gpu", "DisplayName": "GTX 1080 Tower", "Provider": "Ollama", "Role": "Deep", "Priority": 20, "MaxConcurrentRequests": 2, "Tags": \["review", "testing", "reasoning"\], "Enabled": true, "HealthCheckIntervalMs": 2000, "Ollama": { "Host": "192.168.1.100", "Port": 11434, "NumParallel": 1, "MaxLoadedModels": 2, "FlashAttention": false, "TimeoutSeconds": 20, "GpuVramTotalMB": 8192 } }, { "NodeId": "copilot-cloud", "DisplayName": "GitHub Copilot (Enterprise)", "Provider": "CopilotSdk", "Role": "Deep", "Priority": 50, "MaxConcurrentRequests": 5, "Tags": \["code", "review", "reasoning", "refactor"\], "Enabled": true, "HealthCheckIntervalMs": 10000, "Copilot": { "UseStdio": true, "DefaultModel": "gpt-4o", "KeyVaultUri": "<https://splitbrain-vault.vault.azure.net>", "KeyVaultSecretName": "CopilotApiKey", "TimeoutSeconds": 30 } } \] }

## 3.3 IInferenceNode - The Provider Abstraction

This is the single interface that decouples the entire system from any specific AI provider. Every provider - Ollama, Copilot SDK, a future OpenAI adapter - implements this contract. The routing engine, agent system, and MCP server never know or care which provider handles a request.

namespace SplitBrain.Core.Abstractions; public interface IInferenceNode : IAsyncDisposable { /// &lt;summary&gt;Unique identifier matching NodeConfiguration.NodeId.&lt;/summary&gt; string NodeId { get; } /// &lt;summary&gt;Provider type for routing decisions.&lt;/summary&gt; NodeProviderType Provider { get; } /// &lt;summary&gt;Current health snapshot. Updated by NodeHealthCheckService.&lt;/summary&gt; NodeHealthStatus Health { get; } /// &lt;summary&gt;Execute a single inference request and return the complete result.&lt;/summary&gt; Task&lt;InferenceResult&gt; ExecuteAsync( InferenceRequest request, CancellationToken ct = default); /// &lt;summary&gt;Stream inference chunks as they are generated.&lt;/summary&gt; IAsyncEnumerable&lt;InferenceChunk&gt; StreamAsync( InferenceRequest request, \[EnumeratorCancellation\] CancellationToken ct = default); /// &lt;summary&gt;Probe node health. Called by the background health service.&lt;/summary&gt; Task&lt;NodeHealthStatus&gt; GetHealthAsync(CancellationToken ct = default); /// &lt;summary&gt;List models available on this node.&lt;/summary&gt; Task&lt;IReadOnlyList<ModelInfo&gt;> ListModelsAsync(CancellationToken ct = default); }

### 3.3.1 Request and Result Records

public record InferenceRequest { public required string ModelId { get; init; } public required string Prompt { get; init; } public string? SystemPrompt { get; init; } public ChatHistory? History { get; init; } public int? MaxTokens { get; init; } public float? Temperature { get; init; } public string? IdempotencyKey { get; init; } // For MCP deduplication } public record InferenceResult { public required string Content { get; init; } public required int PromptTokens { get; init; } public required int CompletionTokens { get; init; } public int TotalTokens => PromptTokens + CompletionTokens; public required TimeSpan Duration { get; init; } public required string NodeId { get; init; } public required string ModelId { get; init; } public string? FinishReason { get; init; } } public record InferenceChunk { public required string Content { get; init; } public int? TokensGenerated { get; init; } public bool IsFinal { get; init; } public InferenceResult? FinalResult { get; init; } // Populated on last chunk }

### 3.3.2 Node Health Status (3-State)

public enum HealthState { Healthy, // Responding, latency within bounds, VRAM available Degraded, // Responding but slow, high VRAM, or high queue depth Unavailable // Unreachable, timed out, or circuit breaker open } public record NodeHealthStatus { public required HealthState State { get; init; } public required DateTimeOffset LastChecked { get; init; } public double LatencyMs { get; init; } public IReadOnlyList&lt;string&gt; AvailableModels { get; init; } = \[\]; public IReadOnlyList&lt;RunningModelInfo&gt; RunningModels { get; init; } = \[\]; public long? VramLoadedMB { get; init; } // Sum of size_vram from /api/ps public long? VramTotalMB { get; init; } // From OllamaProviderConfig.GpuVramTotalMB public string? ErrorMessage { get; init; } public int ActiveRequests { get; init; } } public record RunningModelInfo { public required string ModelId { get; init; } public required long SizeVramBytes { get; init; } public DateTimeOffset? ExpiresAt { get; init; } }

## 3.4 Provider Adapters

### 3.4.1 OllamaInferenceNode

namespace SplitBrain.Networking.Providers; /// &lt;summary&gt; /// Ollama provider adapter. CRITICAL: The HttpClient from IHttpClientFactory /// (with Polly resilience pipeline attached) MUST be injected into the /// OllamaApiClient(httpClient) constructor. Creating new OllamaApiClient(uri) /// bypasses all resilience pipelines - retry, circuit breaker, timeout - none fire. /// &lt;/summary&gt; public sealed class OllamaInferenceNode : IInferenceNode { private readonly OllamaApiClient \_client; private readonly OllamaProviderConfig \_config; private NodeHealthStatus \_health; public string NodeId { get; } public NodeProviderType Provider => NodeProviderType.Ollama; public NodeHealthStatus Health => \_health; public OllamaInferenceNode( string nodeId, OllamaProviderConfig config, HttpClient httpClient) // ← From IHttpClientFactory, Polly attached { NodeId = nodeId; \_config = config; // CORRECT: Inject managed HttpClient so Polly resilience fires httpClient.BaseAddress = new Uri(config.BaseUrl); \_client = new OllamaApiClient(httpClient); // WRONG (DO NOT DO THIS): // \_client = new OllamaApiClient(new Uri(config.BaseUrl)); // ↑ Creates internal HttpClient, bypasses all Polly pipelines \_health = new NodeHealthStatus { State = HealthState.Unavailable, LastChecked = DateTimeOffset.MinValue }; } public async Task&lt;InferenceResult&gt; ExecuteAsync( InferenceRequest request, CancellationToken ct = default) { var sw = Stopwatch.StartNew(); var chatRequest = new ChatRequest { Model = request.ModelId, Messages = BuildMessages(request), Stream = false, Options = new RequestOptions { Temperature = request.Temperature } }; var response = await \_client.ChatAsync(chatRequest, ct); sw.Stop(); return new InferenceResult { Content = response.Message.Content, PromptTokens = response.PromptEvalCount ?? 0, CompletionTokens = response.EvalCount ?? 0, Duration = sw.Elapsed, NodeId = NodeId, ModelId = request.ModelId, FinishReason = response.DoneReason }; } public async IAsyncEnumerable&lt;InferenceChunk&gt; StreamAsync( InferenceRequest request, \[EnumeratorCancellation\] CancellationToken ct = default) { var sw = Stopwatch.StartNew(); var chatRequest = new ChatRequest { Model = request.ModelId, Messages = BuildMessages(request), Stream = true }; await foreach (var chunk in \_client.ChatAsync(chatRequest, ct)) { var isFinal = chunk.Done; yield return new InferenceChunk { Content = chunk.Message.Content ?? "", TokensGenerated = isFinal ? chunk.EvalCount : null, IsFinal = isFinal, FinalResult = isFinal ? new InferenceResult { Content = chunk.Message.Content ?? "", PromptTokens = chunk.PromptEvalCount ?? 0, CompletionTokens = chunk.EvalCount ?? 0, Duration = sw.Elapsed, NodeId = NodeId, ModelId = request.ModelId, FinishReason = chunk.DoneReason } : null }; } } public async Task&lt;NodeHealthStatus&gt; GetHealthAsync(CancellationToken ct = default) { try { var sw = Stopwatch.StartNew(); // ListRunningModelsAsync maps to /api/ps // NOTE: The correct OllamaSharp method is ListRunningModelsAsync, // NOT GetRunningModelsAsync (common mistake) var running = await \_client.ListRunningModelsAsync(ct); sw.Stop(); var runningList = running.Select(m => new RunningModelInfo { ModelId = m.Name, SizeVramBytes = m.SizeVram, ExpiresAt = m.ExpiresAt }).ToList(); var vramLoadedMB = runningList.Sum(r => r.SizeVramBytes) / (1024 \* 1024); // ListLocalModelsAsync maps to /api/tags var available = await \_client.ListLocalModelsAsync(ct); var state = sw.Elapsed.TotalMilliseconds > \_config.TimeoutSeconds \* 500 ? HealthState.Degraded // >50% of timeout = degraded : HealthState.Healthy; \_health = new NodeHealthStatus { State = state, LastChecked = DateTimeOffset.UtcNow, LatencyMs = sw.Elapsed.TotalMilliseconds, AvailableModels = available.Select(m => m.Name).ToList(), RunningModels = runningList, VramLoadedMB = vramLoadedMB, VramTotalMB = \_config.GpuVramTotalMB, ActiveRequests = 0 // Updated externally by request queue }; } catch (Exception ex) { \_health = new NodeHealthStatus { State = HealthState.Unavailable, LastChecked = DateTimeOffset.UtcNow, ErrorMessage = ex.Message }; } return \_health; } public async Task&lt;IReadOnlyList<ModelInfo&gt;> ListModelsAsync(CancellationToken ct = default) { var models = await \_client.ListLocalModelsAsync(ct); return models.Select(m => new ModelInfo { ModelId = m.Name, SizeBytes = m.Size, ModifiedAt = m.ModifiedAt }).ToList(); } public ValueTask DisposeAsync() => ValueTask.CompletedTask; // HttpClient lifetime managed by IHttpClientFactory, not us private static List&lt;Message&gt; BuildMessages(InferenceRequest request) { var messages = new List&lt;Message&gt;(); if (request.SystemPrompt is not null) messages.Add(new Message(ChatRole.System, request.SystemPrompt)); // Append chat history if present if (request.History is not null) { foreach (var msg in request.History) messages.Add(new Message(msg.Role, msg.Content)); } messages.Add(new Message(ChatRole.User, request.Prompt)); return messages; } }

### 3.4.2 CopilotInferenceNode

namespace SplitBrain.Networking.Providers; /// &lt;summary&gt; /// GitHub Copilot SDK provider adapter. Each ExecuteAsync opens a fresh /// single-turn session (from spec: infinite sessions disabled). /// CopilotClient is IAsyncDisposable - StopAsync() + DisposeAsync() on shutdown. /// &lt;/summary&gt; public sealed class CopilotInferenceNode : IInferenceNode { private readonly CopilotClient \_copilotClient; private readonly CopilotProviderConfig \_config; private NodeHealthStatus \_health; public string NodeId { get; } public NodeProviderType Provider => NodeProviderType.CopilotSdk; public NodeHealthStatus Health => \_health; public CopilotInferenceNode( string nodeId, CopilotProviderConfig config, CopilotClient copilotClient) { NodeId = nodeId; \_config = config; \_copilotClient = copilotClient; \_health = new NodeHealthStatus { State = HealthState.Unavailable, LastChecked = DateTimeOffset.MinValue }; } public async Task&lt;InferenceResult&gt; ExecuteAsync( InferenceRequest request, CancellationToken ct = default) { var sw = Stopwatch.StartNew(); // Fresh single-turn session per request (no infinite sessions) await using var session = await \_copilotClient.CreateSessionAsync(ct); var response = await session.SendAsync(request.Prompt, ct); sw.Stop(); // Extract content from AssistantMessageEvent return new InferenceResult { Content = response.Content, PromptTokens = response.Usage?.PromptTokens ?? 0, CompletionTokens = response.Usage?.CompletionTokens ?? 0, Duration = sw.Elapsed, NodeId = NodeId, ModelId = \_config.DefaultModel, FinishReason = "stop" }; } public async IAsyncEnumerable&lt;InferenceChunk&gt; StreamAsync( InferenceRequest request, \[EnumeratorCancellation\] CancellationToken ct = default) { var sw = Stopwatch.StartNew(); await using var session = await \_copilotClient.CreateSessionAsync(ct); // Streaming via AssistantMessageDeltaEvent await foreach (var delta in session.StreamAsync(request.Prompt, ct)) { yield return new InferenceChunk { Content = delta.Content ?? "", IsFinal = delta.IsFinal, FinalResult = delta.IsFinal ? new InferenceResult { Content = delta.Content ?? "", PromptTokens = delta.Usage?.PromptTokens ?? 0, CompletionTokens = delta.Usage?.CompletionTokens ?? 0, Duration = sw.Elapsed, NodeId = NodeId, ModelId = \_config.DefaultModel, FinishReason = "stop" } : null }; } } public async Task&lt;NodeHealthStatus&gt; GetHealthAsync(CancellationToken ct = default) { try { var sw = Stopwatch.StartNew(); await \_copilotClient.PingAsync(ct); sw.Stop(); \_health = new NodeHealthStatus { State = HealthState.Healthy, LastChecked = DateTimeOffset.UtcNow, LatencyMs = sw.Elapsed.TotalMilliseconds, AvailableModels = \[\_config.DefaultModel\] }; } catch (Exception ex) { \_health = new NodeHealthStatus { State = HealthState.Unavailable, LastChecked = DateTimeOffset.UtcNow, ErrorMessage = ex.Message }; } return \_health; } public async Task&lt;IReadOnlyList<ModelInfo&gt;> ListModelsAsync(CancellationToken ct = default) { return \[new ModelInfo { ModelId = \_config.DefaultModel }\]; } public async ValueTask DisposeAsync() { await \_copilotClient.StopAsync(); await \_copilotClient.DisposeAsync(); } }

### 3.4.3 Inference Node Factory

namespace SplitBrain.Networking; public interface IInferenceNodeFactory { IInferenceNode Create(NodeConfiguration config); } public sealed class InferenceNodeFactory : IInferenceNodeFactory { private readonly IHttpClientFactory \_httpClientFactory; private readonly IServiceProvider \_services; public InferenceNodeFactory( IHttpClientFactory httpClientFactory, IServiceProvider services) { \_httpClientFactory = httpClientFactory; \_services = services; } public IInferenceNode Create(NodeConfiguration config) { return config.Provider switch { NodeProviderType.Ollama => CreateOllamaNode(config), NodeProviderType.CopilotSdk => CreateCopilotNode(config), \_=> throw new NotSupportedException( \$"Provider '{config.Provider}' is not registered. " + \$"Implement IInferenceNode and add a case to InferenceNodeFactory.") }; } private OllamaInferenceNode CreateOllamaNode(NodeConfiguration config) { var ollamaConfig = config.Ollama ?? throw new InvalidOperationException( \$"Node '{config.NodeId}' has Provider=Ollama but no Ollama config section."); // Named HttpClient with Polly pipeline attached var httpClient = \_httpClientFactory.CreateClient(\$"ollama-{config.NodeId}"); return new OllamaInferenceNode(config.NodeId, ollamaConfig, httpClient); } private CopilotInferenceNode CreateCopilotNode(NodeConfiguration config) { var copilotConfig = config.Copilot ?? throw new InvalidOperationException( \$"Node '{config.NodeId}' has Provider=CopilotSdk but no Copilot config section."); // Resolve API key: Key Vault → env var → GitHub CLI var apiKey = ResolveApiKey(copilotConfig); var copilotClient = new CopilotClient(apiKey, copilotConfig.UseStdio); return new CopilotInferenceNode(config.NodeId, copilotConfig, copilotClient); } private string ResolveApiKey(CopilotProviderConfig config) { // 1. Azure Key Vault if (config.KeyVaultUri is not null && config.KeyVaultSecretName is not null) { var secretClient = new SecretClient( new Uri(config.KeyVaultUri), new DefaultAzureCredential()); var secret = secretClient.GetSecret(config.KeyVaultSecretName); return secret.Value.Value; } // 2. Environment variable var envKey = Environment.GetEnvironmentVariable("COPILOT_API_KEY"); if (!string.IsNullOrEmpty(envKey)) return envKey; // 3. GitHub CLI fallback (returns empty - SDK handles auth internally) return string.Empty; } }

## 3.5 Node Registry

namespace SplitBrain.Networking; public interface INodeRegistry { IReadOnlyList&lt;NodeRegistration&gt; GetAllNodes(); IReadOnlyList&lt;NodeRegistration&gt; GetHealthyNodes(); IReadOnlyList&lt;NodeRegistration&gt; GetNodesByRole(NodeRole role); IReadOnlyList&lt;NodeRegistration&gt; GetNodesByTag(string tag); NodeRegistration? GetNode(string nodeId); void RegisterNode(NodeConfiguration config); void DeregisterNode(string nodeId); void UpdateNodeHealth(string nodeId, NodeHealthStatus status); /// &lt;summary&gt; /// Persists the current topology back to nodes.json. /// Called by the dashboard Settings page after edits. /// &lt;/summary&gt; Task SaveTopologyAsync(CancellationToken ct = default); } public record NodeRegistration { public required NodeConfiguration Config { get; init; } public required IInferenceNode Node { get; init; } public NodeHealthStatus? LastHealth { get; set; } }

**Critical: Immutable Swap on Config Reload**

When nodes.json changes (via dashboard edit or manual file edit), the registry must rebuild the node dictionary atomically. **Never** use Clear() followed by re-add - this creates a race window where queries return empty results. Instead, build a new ConcurrentDictionary and swap it in one atomic operation.

public sealed class NodeRegistry : INodeRegistry, IDisposable { private ConcurrentDictionary&lt;string, NodeRegistration&gt; \_nodes = new(); private readonly IInferenceNodeFactory \_factory; private readonly IOptionsMonitor&lt;NodeTopologyConfig&gt; \_optionsMonitor; private readonly IDisposable? \_changeListener; private readonly string \_configFilePath; public NodeRegistry( IInferenceNodeFactory factory, IOptionsMonitor&lt;NodeTopologyConfig&gt; optionsMonitor, IConfiguration configuration) { \_factory = factory; \_optionsMonitor = optionsMonitor; \_configFilePath = Path.Combine( AppContext.BaseDirectory, "..", "config", "nodes.json"); // Initial load RebuildTopology(optionsMonitor.CurrentValue); // Hot-reload on file change (reloadOnChange: true in config builder) \_changeListener = optionsMonitor.OnChange(newConfig => { RebuildTopology(newConfig); }); } private void RebuildTopology(NodeTopologyConfig config) { var newDict = new ConcurrentDictionary&lt;string, NodeRegistration&gt;(); foreach (var nodeConfig in config.Nodes.Where(n => n.Enabled)) { // Reuse existing node instance if config hasn't changed if (\_nodes.TryGetValue(nodeConfig.NodeId, out var existing) && existing.Config == nodeConfig) { newDict\[nodeConfig.NodeId\] = existing; } else { // Dispose old node if it existed if (\_nodes.TryGetValue(nodeConfig.NodeId, out var old)) _= old.Node.DisposeAsync(); var node = \_factory.Create(nodeConfig); newDict\[nodeConfig.NodeId\] = new NodeRegistration { Config = nodeConfig, Node = node }; } } // Dispose nodes that were removed from config var oldDict = Interlocked.Exchange(ref \_nodes, newDict); foreach (var (id, reg) in oldDict) { if (!newDict.ContainsKey(id))_ = reg.Node.DisposeAsync(); } } public async Task SaveTopologyAsync(CancellationToken ct = default) { var config = new NodeTopologyConfig { Nodes = \_nodes.Values.Select(r => r.Config).ToList() }; var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }); await File.WriteAllTextAsync(\_configFilePath, json, ct); // IOptionsMonitor picks up the change via reloadOnChange: true } // ... GetAllNodes, GetHealthyNodes, etc. delegate to \_nodes public IReadOnlyList&lt;NodeRegistration&gt; GetAllNodes() => \_nodes.Values.ToList(); public IReadOnlyList&lt;NodeRegistration&gt; GetHealthyNodes() => \_nodes.Values.Where(n => n.LastHealth?.State == HealthState.Healthy).ToList(); public IReadOnlyList&lt;NodeRegistration&gt; GetNodesByRole(NodeRole role) => \_nodes.Values.Where(n => n.Config.Role == role).ToList(); public IReadOnlyList&lt;NodeRegistration&gt; GetNodesByTag(string tag) => \_nodes.Values.Where(n => n.Config.Tags.Contains(tag)).ToList(); // ... }

## 3.6 Health Check Service

namespace SplitBrain.Networking; /// &lt;summary&gt; /// Background service that polls each enabled node at its configured interval. /// Uses per-node SemaphoreSlim to prevent concurrent health checks on the same node. /// Publishes results to SignalR hub for dashboard consumption. /// &lt;/summary&gt; public sealed class NodeHealthCheckService : BackgroundService { private readonly INodeRegistry \_registry; private readonly IHubContext&lt;DashboardHub, IDashboardClient&gt; \_hub; private readonly ILogger&lt;NodeHealthCheckService&gt; \_logger; private readonly ConcurrentDictionary&lt;string, SemaphoreSlim&gt; \_semaphores = new(); public NodeHealthCheckService( INodeRegistry registry, IHubContext&lt;DashboardHub, IDashboardClient&gt; hub, ILogger&lt;NodeHealthCheckService&gt; logger) { \_registry = registry; \_hub = hub; \_logger = logger; } protected override async Task ExecuteAsync(CancellationToken stoppingToken) { while (!stoppingToken.IsCancellationRequested) { var nodes = \_registry.GetAllNodes(); var tasks = nodes.Select(n => CheckNodeAsync(n, stoppingToken)); await Task.WhenAll(tasks); // Sleep for the minimum interval across all nodes var minInterval = nodes .Select(n => n.Config.HealthCheckIntervalMs) .DefaultIfEmpty(2000) // Guard: empty collection .Min(); await Task.Delay(minInterval, stoppingToken); } } private async Task CheckNodeAsync( NodeRegistration registration, CancellationToken ct) { var sem = \_semaphores.GetOrAdd( registration.Config.NodeId, \_ => new SemaphoreSlim(1, 1)); if (!await sem.WaitAsync(0, ct)) // Non-blocking: skip if already checking return; try { var health = await registration.Node.GetHealthAsync(ct); \_registry.UpdateNodeHealth(registration.Config.NodeId, health); // Push to dashboard via SignalR await \_hub.Clients.All.ReceiveNodeHealthUpdate(new NodeHealthSnapshot { NodeId = registration.Config.NodeId, DisplayName = registration.Config.DisplayName, Health = health, Timestamp = DateTimeOffset.UtcNow }); } catch (Exception ex) { \_logger.LogWarning(ex, "Health check failed for node {NodeId}", registration.Config.NodeId); } finally { sem.Release(); } } }

# 4\. Model Definitions & Registry

## 4.1 ModelDefinition

namespace SplitBrain.Core.Models; public enum ModelFamily { Qwen, DeepSeek, Nomic, Copilot, // Future: Llama, Mistral, Phi, etc. } /// &lt;summary&gt; /// Task types from the spec. These are the routing primitives - /// each request is classified as one TaskType, which determines /// its fallback chain and hard routing rules. /// &lt;/summary&gt; public enum TaskType { Autocomplete, // Latency-critical IDE completions Chat, // Interactive conversation Review, // Code review, analysis Refactor, // Code transformation TestGeneration, // Unit/integration test creation AgentStep, // Bounded agent sub-task Embedding // Vector embedding generation } public record ModelDefinition { public required string ModelId { get; init; } public required string DisplayName { get; init; } public required ModelFamily Family { get; init; } public required TaskType PrimaryCapability { get; init; } public List&lt;TaskType&gt; SecondaryCapabilities { get; init; } = \[\]; public string? QuantizationLevel { get; init; } public int ContextWindow { get; init; } = 8192; public int EstimatedVramMB { get; init; } public List&lt;string&gt; PreferredNodeIds { get; init; } = \[\]; }

### 4.1.1 Locked Model Matrix

These models are the spec's locked selections - chosen for stability over raw capability, matching the available VRAM budgets.

| **Model ID**               | **Display Name**         | **Family** | **Quantization** | **Context** | **VRAM (est.)** | **Preferred Node** |
| -------------------------- | ------------------------ | ---------- | ---------------- | ----------- | --------------- | ------------------ |
| qwen2.5-coder:7b-q4_K_M    | Qwen 2.5 Coder 7B (Q4)   | Qwen       | Q4_K_M           | 8192        | ~4,500 MB       | home-laptop        |
| ---                        | ---                      | ---        | ---              | ---         | ---             | ---                |
| qwen2.5-coder:7b-q5_K_M    | Qwen 2.5 Coder 7B (Q5)   | Qwen       | Q5_K_M           | 8192        | ~5,200 MB       | tower-gpu          |
| ---                        | ---                      | ---        | ---              | ---         | ---             | ---                |
| deepseek-coder:6.7b-q4_K_M | DeepSeek Coder 6.7B (Q4) | DeepSeek   | Q4_K_M           | 8192        | ~4,200 MB       | tower-gpu          |
| ---                        | ---                      | ---        | ---              | ---         | ---             | ---                |
| nomic-embed-text           | Nomic Embed Text v1.5    | Nomic      | F16              | 8192        | ~270 MB         | any                |
| ---                        | ---                      | ---        | ---              | ---         | ---             | ---                |
| gpt-4o                     | GPT-4o (via Copilot SDK) | Copilot    | N/A              | 128000      | 0 (cloud)       | copilot-cloud      |
| ---                        | ---                      | ---        | ---              | ---         | ---             | ---                |

## 4.2 Fallback Chains - Configurable Per TaskType

public record FallbackChainConfig { public required TaskType TaskType { get; init; } public required List&lt;FallbackStep&gt; Steps { get; init; } } public record FallbackStep { public required string ModelId { get; init; } public List&lt;string&gt;? PreferredNodeIds { get; init; } // null = any healthy node public int TimeoutOverrideMs { get; init; } = 0; // 0 = use node default }

**Graceful Degradation**

Fallback chains reference node IDs and model IDs, both of which are dynamic. If a referenced node or model does not exist at runtime, the step is **silently skipped** and the chain continues to the next step. This means adding or removing a node never breaks existing fallback chains - they simply adapt.

### 4.2.1 Complete Fallback Chain Configuration

// In appsettings.json under "SplitBrain:FallbackChains" \[ { "TaskType": "Autocomplete", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] } // NEVER fall back to Deep or Cloud nodes - latency requirement from spec. // If home-laptop is unavailable, autocomplete fails fast. \] }, { "TaskType": "Chat", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "Review", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "deepseek-coder:6.7b-q4_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "Refactor", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "TestGeneration", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "deepseek-coder:6.7b-q4_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "AgentStep", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "Embedding", "Steps": \[ { "ModelId": "nomic-embed-text", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "nomic-embed-text", "PreferredNodeIds": \["tower-gpu"\] } \] } \]

# 5\. Intelligent Routing Engine

## 5.1 Routing Score Weights

The routing engine computes a composite score for each eligible (node, model) pair. Weights are from the spec and are configurable via appsettings.json.

namespace SplitBrain.Routing; public record RoutingWeights { public double Vram { get; init; } = 0.35; // Available VRAM headroom public double QueueDepth { get; init; } = 0.25; // Current queue length public double ModelFit { get; init; } = 0.20; // Model capability match public double Latency { get; init; } = 0.10; // Recent average latency public double ContextFit { get; init; } = 0.10; // Context window sufficiency }

## 5.2 Hard Routing Rules

Hard rules **override** the soft scoring system. They are non-negotiable constraints from the spec.

| **Rule**                     | **Condition**                                          | **Action**                                                                                     | **Rationale**                                                                                       |
| ---------------------------- | ------------------------------------------------------ | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| **Autocomplete Isolation**   | TaskType == Autocomplete                               | Force to NodeRole.Fast nodes ONLY. Never route to Deep or Cloud.                               | Latency requirement: autocomplete must be <200ms. Deep and cloud nodes add 100ms+ network overhead. |
| ---                          | ---                                                    | ---                                                                                            | ---                                                                                                 |
| **Large Context Preference** | Input tokens > 5,000                                   | Prefer NodeRole.Deep or NodeRole.Hybrid nodes. Exclude nodes with context window < input size. | Large prompts need more VRAM and longer processing; fast nodes risk timeout.                        |
| ---                          | ---                                                    | ---                                                                                            | ---                                                                                                 |
| **Cloud Failover**           | All local nodes HealthState.Unavailable                | Route to cloud node regardless of task type.                                                   | System must remain functional even if all local hardware is down.                                   |
| ---                          | ---                                                    | ---                                                                                            | ---                                                                                                 |
| **Standby Activation**       | All Fast/Deep/Hybrid nodes unavailable or at max queue | Activate NodeRole.Standby nodes.                                                               | Standby is the last line of defense before cloud failover.                                          |
| ---                          | ---                                                    | ---                                                                                            | ---                                                                                                 |

## 5.3 Routing Decision Record

public record RoutingDecision { public required string TaskId { get; init; } public required TaskType TaskType { get; init; } public required string SelectedNodeId { get; init; } public required string SelectedModelId { get; init; } public required double CompositeScore { get; init; } public required TimeSpan DecisionDuration { get; init; } // Reasoning trail for observability and debugging public required List&lt;RoutingCandidate&gt; CandidatesEvaluated { get; init; } public required List&lt;string&gt; HardRulesApplied { get; init; } public int FallbackStepIndex { get; init; } = 0; // 0 = primary choice public string? FallbackReason { get; init; } // Why we fell back } public record RoutingCandidate { public required string NodeId { get; init; } public required string ModelId { get; init; } public required double Score { get; init; } public required Dictionary&lt;string, double&gt; ScoreBreakdown { get; init; } public bool Excluded { get; init; } public string? ExclusionReason { get; init; } }

## 5.4 Request Queue

namespace SplitBrain.Routing; public interface IRequestQueue { /// &lt;summary&gt;Enqueue a request to a specific node. Blocks if at capacity.&lt;/summary&gt; Task&lt;IAsyncDisposable&gt; EnqueueAsync(string nodeId, CancellationToken ct = default); /// &lt;summary&gt;Current queue depth for a node.&lt;/summary&gt; int GetQueueDepth(string nodeId); /// &lt;summary&gt;True if node's queue exceeds backpressure threshold.&lt;/summary&gt; bool IsBackpressured(string nodeId); } public sealed class NodeRequestQueue : IRequestQueue { private readonly ConcurrentDictionary&lt;string, SemaphoreSlim&gt; \_semaphores = new(); private readonly ConcurrentDictionary&lt;string, int&gt; \_queueDepths = new(); private readonly INodeRegistry \_registry; public NodeRequestQueue(INodeRegistry registry) { \_registry = registry; } public async Task&lt;IAsyncDisposable&gt; EnqueueAsync( string nodeId, CancellationToken ct = default) { var node = \_registry.GetNode(nodeId) ?? throw new InvalidOperationException(\$"Node '{nodeId}' not found."); var sem = \_semaphores.GetOrAdd( nodeId, \_ => new SemaphoreSlim( node.Config.MaxConcurrentRequests, node.Config.MaxConcurrentRequests)); \_queueDepths.AddOrUpdate(nodeId, 1, (\_, v) => v + 1); await sem.WaitAsync(ct); return new QueueSlot(nodeId, sem, \_queueDepths); } public int GetQueueDepth(string nodeId) => \_queueDepths.GetValueOrDefault(nodeId, 0); public bool IsBackpressured(string nodeId) { var node = \_registry.GetNode(nodeId); if (node is null) return true; return GetQueueDepth(nodeId) > node.Config.MaxConcurrentRequests \* 2; } private sealed class QueueSlot : IAsyncDisposable { private readonly string \_nodeId; private readonly SemaphoreSlim \_semaphore; private readonly ConcurrentDictionary&lt;string, int&gt; \_depths; public QueueSlot(string nodeId, SemaphoreSlim sem, ConcurrentDictionary&lt;string, int&gt; depths) { \_nodeId = nodeId; \_semaphore = sem; \_depths = depths; } public ValueTask DisposeAsync() { \_semaphore.Release(); \_depths.AddOrUpdate(\_nodeId, 0, (\_, v) => Math.Max(0, v - 1)); return ValueTask.CompletedTask; } } }

# 6\. Resilience with Polly v8

Each Ollama node gets a dedicated **named HttpClient** with a Polly resilience pipeline attached via Microsoft.Extensions.Http.Resilience. The pipeline includes retry, circuit breaker, and timeout - in that order.

**Critical Implementation Constraint**

The HttpClient from IHttpClientFactory (with Polly attached) **MUST** be injected into new OllamaApiClient(httpClient). If you create new OllamaApiClient(uri), the OllamaSharp library creates its own internal HttpClient, and **all Polly pipelines are bypassed** - retry, circuit breaker, timeout - none fire. This is the single most common integration mistake.

// In SplitBrain.Orchestrator/Program.cs - DI composition root // Register a named, resilient HttpClient per Ollama node foreach (var node in nodeConfig.Nodes.Where(n => n.Provider == NodeProviderType.Ollama)) { var ollamaConfig = node.Ollama!; builder.Services .AddHttpClient(\$"ollama-{node.NodeId}", client => { client.BaseAddress = new Uri(ollamaConfig.BaseUrl); client.Timeout = TimeSpan.FromSeconds(ollamaConfig.TimeoutSeconds \* 2); }) .AddResilienceHandler(\$"resilience-{node.NodeId}", pipeline => { // 1. Retry: exponential backoff + jitter, 3 attempts pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.FromMilliseconds(500), BackoffType = DelayBackoffType.Exponential, UseJitter = true, ShouldHandle = new PredicateBuilder&lt;HttpResponseMessage&gt;() .HandleResult(r => !r.IsSuccessStatusCode) .Handle&lt;HttpRequestException&gt;() .Handle&lt;TimeoutRejectedException&gt;() }); // 2. Circuit breaker: 80% failure ratio, 10-sample window, 30s break pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions { FailureRatio = 0.8, SamplingDuration = TimeSpan.FromSeconds(30), MinimumThroughput = 10, BreakDuration = TimeSpan.FromSeconds(30), ShouldHandle = new PredicateBuilder&lt;HttpResponseMessage&gt;() .HandleResult(r => !r.IsSuccessStatusCode) .Handle&lt;HttpRequestException&gt;() .Handle&lt;TimeoutRejectedException&gt;() }); // 3. Timeout: per-node from config pipeline.AddTimeout(TimeSpan.FromSeconds(ollamaConfig.TimeoutSeconds)); }); }

### 6.1 Edge Case Guards

| **Edge Case**                      | **Risk**                                    | **Guard**                                                            |
| ---------------------------------- | ------------------------------------------- | -------------------------------------------------------------------- |
| nodes.Min() on empty collection    | InvalidOperationException                   | Use .DefaultIfEmpty(fallback).Min() or early-return guard            |
| ---                                | ---                                         | ---                                                                  |
| TokensPerSecond divide by zero     | DivideByZeroException / NaN                 | InferenceDuration.TotalSeconds > 0 ? CompletionTokens / Duration : 0 |
| ---                                | ---                                         | ---                                                                  |
| Config reload race condition       | Empty registry during rebuild               | Interlocked.Exchange(ref \_nodes, newDict) - never Clear() + re-add  |
| ---                                | ---                                         | ---                                                                  |
| OllamaSharp HttpClient bypass      | All Polly resilience disabled               | Always use new OllamaApiClient(httpClient) constructor               |
| ---                                | ---                                         | ---                                                                  |
| Concurrent health checks same node | Redundant network calls, stale interleaving | Per-node SemaphoreSlim(1,1) with non-blocking WaitAsync(0)           |
| ---                                | ---                                         | ---                                                                  |

# 7\. Semantic Kernel Agent Engine

Agents use **Microsoft.SemanticKernel**. The state machine is not custom - it uses SK's ChatCompletionAgent with bounded execution enforced by the orchestrator.

## 7.1 Agent State Flow

INIT → PLAN → IMPLEMENT → REVIEW → TEST → DONE | FAIL │ │ └── On max iterations (4) or max ─────────┘ tokens (12,000) → FAIL

## 7.2 Agent Configuration Bounds

| **Parameter**           | **Value**         | **Rationale**                                                             |
| ----------------------- | ----------------- | ------------------------------------------------------------------------- |
| Max iterations per loop | **4**             | From spec. Prevents runaway loops. Most tasks complete in 2-3 iterations. |
| ---                     | ---               | ---                                                                       |
| Max tokens per loop     | **12,000**        | From spec. Fits within 8K context with room for system prompt + history.  |
| ---                     | ---               | ---                                                                       |
| Default timeout         | **300s**          | 5-minute hard cutoff for any agent task.                                  |
| ---                     | ---               | ---                                                                       |
| Architect, Coder roles  | → Fast-role nodes | Interactive latency for code generation.                                  |
| ---                     | ---               | ---                                                                       |
| Reviewer, Tester roles  | → Deep-role nodes | Complex reasoning benefits from higher-quality quantization.              |
| ---                     | ---               | ---                                                                       |

## 7.3 Semantic Kernel Integration

Each IInferenceNode is bridged into Semantic Kernel via IChatCompletionService. This is the glue that lets SK's ChatCompletionAgent work with any provider through a uniform interface.

namespace SplitBrain.Agents; /// &lt;summary&gt; /// Wraps an IInferenceNode as an IChatCompletionService for Semantic Kernel. /// This bridge enables SK agents to target any provider transparently. /// &lt;/summary&gt; public sealed class InferenceNodeChatCompletionService : IChatCompletionService { private readonly IInferenceNode \_node; private readonly string \_modelId; public IReadOnlyDictionary&lt;string, object?&gt; Attributes { get; } public InferenceNodeChatCompletionService( IInferenceNode node, string modelId) { \_node = node; \_modelId = modelId; Attributes = new Dictionary&lt;string, object?&gt; { \["ModelId"\] = modelId, \["NodeId"\] = node.NodeId, \["Provider"\] = node.Provider.ToString() }; } public async Task&lt;IReadOnlyList<ChatMessageContent&gt;> GetChatMessageContentsAsync( ChatHistory chatHistory, PromptExecutionSettings? settings = null, Kernel? kernel = null, CancellationToken ct = default) { var request = new InferenceRequest { ModelId = \_modelId, Prompt = chatHistory.Last().Content ?? "", History = chatHistory, Temperature = (settings as OpenAIPromptExecutionSettings) ?.Temperature is float t ? t : null, MaxTokens = (settings as OpenAIPromptExecutionSettings) ?.MaxTokens }; var result = await \_node.ExecuteAsync(request, ct); return \[new ChatMessageContent( AuthorRole.Assistant, result.Content, modelId: result.ModelId, metadata: new Dictionary&lt;string, object?&gt; { \["NodeId"\] = result.NodeId, \["PromptTokens"\] = result.PromptTokens, \["CompletionTokens"\] = result.CompletionTokens, \["Duration"\] = result.Duration })\]; } public async IAsyncEnumerable&lt;StreamingChatMessageContent&gt; GetStreamingChatMessageContentsAsync( ChatHistory chatHistory, PromptExecutionSettings? settings = null, Kernel? kernel = null, \[EnumeratorCancellation\] CancellationToken ct = default) { var request = new InferenceRequest { ModelId = \_modelId, Prompt = chatHistory.Last().Content ?? "", History = chatHistory }; await foreach (var chunk in \_node.StreamAsync(request, ct)) { yield return new StreamingChatMessageContent( AuthorRole.Assistant, chunk.Content, modelId: \_modelId); } } }

### 7.3.1 Agent Registration Pattern

// Building a Semantic Kernel agent that targets a specific node + model public ChatCompletionAgent BuildAgent( string agentName, string instructions, IInferenceNode node, string modelId) { // Bridge IInferenceNode → IChatCompletionService var chatService = new InferenceNodeChatCompletionService(node, modelId); var kernel = Kernel.CreateBuilder() .AddService&lt;IChatCompletionService&gt;(chatService) .Build(); return new ChatCompletionAgent { Name = agentName, Instructions = instructions, Kernel = kernel }; } // Example: Build a Reviewer agent targeting the tower-gpu node var towerNode = registry.GetNode("tower-gpu")!.Node; var reviewer = BuildAgent( "Reviewer", "You are a senior code reviewer. Analyze the code for bugs, " + "performance issues, and style violations. Be specific and actionable.", towerNode, "qwen2.5-coder:7b-q5_K_M" );

## 7.4 Agent Step Event Log

namespace SplitBrain.Agents; public enum AgentStepType { Init, Plan, Implement, Review, Test, Done, Fail, FallbackTriggered, ValidationFailed } /// &lt;summary&gt; /// Immutable, append-only audit record for every agent decision. /// Backed by LiteDB. Distinct from OpenTelemetry (which captures timing) - /// this captures the SEMANTIC decision chain for post-mortem debugging. /// &lt;/summary&gt; public record AgentStepEvent { public required string TaskId { get; init; } public required int StepIndex { get; init; } public required DateTimeOffset Timestamp { get; init; } public required AgentStepType StepType { get; init; } public required string Summary { get; init; } public string? ModelId { get; init; } public string? NodeId { get; init; } public int? TokensConsumed { get; init; } public double? LatencyMs { get; init; } public Dictionary&lt;string, object?&gt;? Metadata { get; init; } } public interface IAgentEventLog { /// &lt;summary&gt;Append a step event. Never updates or deletes.&lt;/summary&gt; Task AppendAsync(AgentStepEvent step, CancellationToken ct = default); /// &lt;summary&gt; /// Replay all events for a task in strict StepIndex order. /// Used for post-mortem debugging and dashboard task detail view. /// &lt;/summary&gt; IAsyncEnumerable&lt;AgentStepEvent&gt; ReplayAsync( string taskId, \[EnumeratorCancellation\] CancellationToken ct = default); /// &lt;summary&gt;Get total tokens consumed for a task across all steps.&lt;/summary&gt; Task&lt;int&gt; GetTotalTokensAsync(string taskId, CancellationToken ct = default); }

# 8\. Blazor Dashboard - Monitoring AND Management

The dashboard is the user's primary interface. It is **Blazor Server** (not WASM) - this gives direct DI access to all orchestrator services without needing a separate API layer. Real-time updates flow through a SignalR DashboardHub with a strongly-typed client interface.

## 8.1 Dashboard Pages

| **Page**     | **Route** | **Purpose**                     | **Key Features**                                                                                                                                                        |
| ------------ | --------- | ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Overview** | /         | Cluster health at a glance      | Node cards with LED status indicators (green/yellow/red for Healthy/Degraded/Unavailable), VRAM utilization gauges, active request counters, aggregate token throughput |
| ---          | ---       | ---                             | ---                                                                                                                                                                     |
| **Nodes**    | /nodes    | Per-node detail and control     | Model list with VRAM per model, queue depth, latency history, health timeline, enable/disable toggle, real-time /api/ps data                                            |
| ---          | ---       | ---                             | ---                                                                                                                                                                     |
| **Models**   | /models   | Model registry management       | Available models per node, capability tags, VRAM requirements, preferred node assignment, pull/remove model actions                                                     |
| ---          | ---       | ---                             | ---                                                                                                                                                                     |
| **Tasks**    | /tasks    | Active and historical tasks     | Task list with routing decision details, agent step timeline visualization, token usage breakdown, validation results, step replay                                      |
| ---          | ---       | ---                             | ---                                                                                                                                                                     |
| **Logs**     | /logs     | Real-time structured log viewer | Filterable log stream (level, node, task, model), full-text search, log level badges, auto-scroll with pause                                                            |
| ---          | ---       | ---                             | ---                                                                                                                                                                     |
| **Settings** | /settings | System-wide configuration       | Node topology editor, fallback chain editor, routing weight sliders, routing policy selector                                                                            |
| ---          | ---       | ---                             | ---                                                                                                                                                                     |

## 8.2 Settings Page - Node Topology Editor

This is the key management feature. The Settings page "Nodes" tab provides full CRUD operations on the node topology:

- **Node card grid** - Lists all configured nodes from nodes.json. Each card shows NodeId, DisplayName, provider type icon, role badge, priority, enabled state, and current health indicator.
- **"Add Node" button** - Opens a form with fields for all NodeConfiguration properties. The **Provider** dropdown (Ollama / CopilotSdk) dynamically shows/hides provider-specific fields (Ollama shows Host, Port, FlashAttention, etc.; CopilotSdk shows UseStdio, DefaultModel, KeyVault settings).
- **Edit** - Click any existing node card to edit its configuration in the same form.
- **"Save"** - Serializes the updated topology to nodes.json via INodeRegistry.SaveTopologyAsync(). IOptionsMonitor picks up the file change and the registry hot-reloads - creating/disposing IInferenceNode instances as nodes are added or removed.
- **"Delete"** - Removes a node from the topology with a confirmation dialog. Saves immediately.

## 8.3 Fallback Chain Editor

Also on the Settings page, a visual editor for fallback chains:

- Dropdown to select TaskType (Autocomplete, Chat, Review, Refactor, TestGeneration, AgentStep, Embedding)
- Ordered list of FallbackStep entries, each with a model selector dropdown + optional node filter checkboxes
- Drag-and-drop reordering of steps (or up/down arrow buttons for accessibility)
- "Add Step" and "Remove Step" buttons per chain
- "Save" writes to the SplitBrain:FallbackChains section of appsettings.json → IOptionsMonitor hot-reload

## 8.4 IDashboardClient Interface

namespace SplitBrain.Dashboard; /// &lt;summary&gt; /// Strongly-typed SignalR client interface. All dashboard real-time /// updates flow through these methods. Blazor components subscribe /// to these events via the DashboardHub. /// &lt;/summary&gt; public interface IDashboardClient { Task ReceiveNodeHealthUpdate(NodeHealthSnapshot snapshot); Task ReceiveLogEntry(StructuredLogEntry entry); Task ReceiveTaskUpdate(TaskStatusUpdate update); Task ReceiveMetricUpdate(MetricSnapshot snapshot); Task ReceiveAlert(SystemAlert alert); Task ReceiveAgentStepEvent(AgentStepEvent step); Task ReceiveTokenUsageUpdate(TokenUsageRecord record); } public record NodeHealthSnapshot { public required string NodeId { get; init; } public required string DisplayName { get; init; } public required NodeHealthStatus Health { get; init; } public required DateTimeOffset Timestamp { get; init; } } public record StructuredLogEntry { public required DateTimeOffset Timestamp { get; init; } public required string Level { get; init; } // "Information", "Warning", "Error" public required string Message { get; init; } public string? NodeId { get; init; } public string? TaskId { get; init; } public string? ModelId { get; init; } public string? CorrelationId { get; init; } public string? Exception { get; init; } } public record TaskStatusUpdate { public required string TaskId { get; init; } public required TaskType TaskType { get; init; } public required string Status { get; init; } // "Queued", "Routing", "Executing", "Done", "Failed" public RoutingDecision? Routing { get; init; } public int TokensConsumed { get; init; } public double ElapsedMs { get; init; } } public record SystemAlert { public required string AlertId { get; init; } public required string Severity { get; init; } // "Info", "Warning", "Critical" public required string Message { get; init; } public required DateTimeOffset Timestamp { get; init; } public string? NodeId { get; init; } }

# 9\. Observability Stack

## 9.1 Serilog

Structured logging with enrichers for NodeId, TaskId, ModelId, and CorrelationId. Four sinks:

| **Sink**             | **Environment** | **Purpose**                                     |
| -------------------- | --------------- | ----------------------------------------------- |
| **Console**          | Development     | Immediate feedback during development           |
| ---                  | ---             | ---                                             |
| **File (Rolling)**   | All             | 7-day rolling retention, structured JSON format |
| ---                  | ---             | ---                                             |
| **Seq**              | Optional        | Full-text search and analysis via Seq server    |
| ---                  | ---             | ---                                             |
| **SignalR (Custom)** | All             | Live streaming to Blazor dashboard Logs page    |
| ---                  | ---             | ---                                             |

### 9.1.1 Custom SignalR Log Sink

namespace SplitBrain.Observability; /// &lt;summary&gt; /// Custom Serilog sink that pushes structured log entries to the /// DashboardHub via SignalR. Includes configurable minimum level /// and batching to avoid flooding the dashboard. /// &lt;/summary&gt; public sealed class SignalRLogSink : ILogEventSink { private readonly IHubContext&lt;DashboardHub, IDashboardClient&gt; \_hub; private readonly LogEventLevel \_minimumLevel; private readonly Channel&lt;StructuredLogEntry&gt; \_channel; private readonly Task \_processingTask; public SignalRLogSink( IHubContext&lt;DashboardHub, IDashboardClient&gt; hub, LogEventLevel minimumLevel = LogEventLevel.Information, int batchSize = 20) { \_hub = hub; \_minimumLevel = minimumLevel; \_channel = Channel.CreateBounded&lt;StructuredLogEntry&gt;( new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest }); \_processingTask = Task.Run(() => ProcessBatchesAsync(batchSize)); } public void Emit(LogEvent logEvent) { if (logEvent.Level &lt; \_minimumLevel) return; var entry = new StructuredLogEntry { Timestamp = logEvent.Timestamp, Level = logEvent.Level.ToString(), Message = logEvent.RenderMessage(), NodeId = logEvent.Properties.GetValueOrDefault("NodeId")?.ToString(), TaskId = logEvent.Properties.GetValueOrDefault("TaskId")?.ToString(), ModelId = logEvent.Properties.GetValueOrDefault("ModelId")?.ToString(), CorrelationId = logEvent.Properties .GetValueOrDefault("CorrelationId")?.ToString(), Exception = logEvent.Exception?.ToString() }; \_channel.Writer.TryWrite(entry); } private async Task ProcessBatchesAsync(int batchSize) { var batch = new List<StructuredLogEntry&gt;(batchSize); await foreach (var entry in \_channel.Reader.ReadAllAsync()) { batch.Add(entry); if (batch.Count >= batchSize || !\_channel.Reader.TryPeek(out \_)) { foreach (var item in batch) await \_hub.Clients.All.ReceiveLogEntry(item); batch.Clear(); } } } }

## 9.2 OpenTelemetry

Distributed tracing and metrics via a custom ActivitySource.

namespace SplitBrain.Observability; public static class Telemetry { public static readonly ActivitySource Source = new("SplitBrain.AI", "2.0.0"); // Meters public static readonly Meter Meter = new("SplitBrain.AI", "2.0.0"); // Token metrics public static readonly Histogram&lt;double&gt; TokensPerSecond = Meter.CreateHistogram&lt;double&gt;( "splitbrain.tokens.per_second", "tokens/s", "Inference throughput in tokens per second"); public static readonly Counter&lt;long&gt; PromptTokens = Meter.CreateCounter&lt;long&gt;( "splitbrain.tokens.prompt", "tokens", "Total prompt tokens consumed"); public static readonly Counter&lt;long&gt; CompletionTokens = Meter.CreateCounter&lt;long&gt;( "splitbrain.tokens.completion", "tokens", "Total completion tokens generated"); public static readonly Counter&lt;double&gt; EstimatedCost = Meter.CreateCounter&lt;double&gt;( "splitbrain.cost.estimated", "USD", "Estimated cost for cloud inference"); // Operational metrics public static readonly Histogram&lt;double&gt; RoutingLatency = Meter.CreateHistogram&lt;double&gt;( "splitbrain.routing.latency_ms", "ms", "Time to compute a routing decision"); public static readonly UpDownCounter&lt;int&gt; QueueDepth = Meter.CreateUpDownCounter&lt;int&gt;( "splitbrain.queue.depth", "requests", "Current queue depth across all nodes"); }

## 9.3 Token Cost Tracking

public record TokenUsageRecord { public required string TaskId { get; init; } public required string ModelId { get; init; } public required string NodeId { get; init; } public required int PromptTokens { get; init; } public required int CompletionTokens { get; init; } public int TotalTokens => PromptTokens + CompletionTokens; public required DateTimeOffset Timestamp { get; init; } public required TimeSpan InferenceDuration { get; init; } // Divide-by-zero guarded throughput calculation public double TokensPerSecond => InferenceDuration.TotalSeconds > 0 ? CompletionTokens / InferenceDuration.TotalSeconds : 0; // Estimated cost (0 for local models, real rates for cloud) public decimal EstimatedCostUSD { get; init; } }

# 10\. MCP Server

The MCP server is the single system boundary. All external consumers (IDEs, CLI tools, other agents) interact with SplitBrain.AI exclusively through MCP tools. Built on the official ModelContextProtocol C# SDK NuGet.

## 10.1 Transport Configuration

| **Transport**       | **Use Case**                              | **Configuration**                       |
| ------------------- | ----------------------------------------- | --------------------------------------- |
| **Streamable HTTP** | Default. Network-accessible MCP endpoint. | <http://localhost:5100/mcp>             |
| ---                 | ---                                       | ---                                     |
| **stdio**           | Local IDE integration (VS Code, VS 2026)  | Launched as subprocess by IDE extension |
| ---                 | ---                                       | ---                                     |

## 10.2 Tool Registry

| **Tool Name**   | **TaskType**   | **Description**                                      |
| --------------- | -------------- | ---------------------------------------------------- |
| review_code     | Review         | Analyze code for bugs, performance, style violations |
| ---             | ---            | ---                                                  |
| refactor_code   | Refactor       | Transform code structure while preserving behavior   |
| ---             | ---            | ---                                                  |
| generate_tests  | TestGeneration | Create unit/integration tests for given code         |
| ---             | ---            | ---                                                  |
| run_tests       | AgentStep      | Execute test suite and report results                |
| ---             | ---            | ---                                                  |
| query_ui        | Chat           | Natural language query interface                     |
| ---             | ---            | ---                                                  |
| apply_patch     | Refactor       | Apply a code patch to the target codebase            |
| ---             | ---            | ---                                                  |
| search_codebase | Embedding      | Semantic search over the indexed codebase            |
| ---             | ---            | ---                                                  |

## 10.3 Idempotency Middleware

namespace SplitBrain.MCP; public enum IdempotencyState { Processing, Completed, Failed } public record IdempotencyEntry { public required string Key { get; init; } public required DateTimeOffset CreatedAt { get; init; } public required TimeSpan Ttl { get; init; } public IdempotencyState State { get; init; } public object? Result { get; init; } } /// &lt;summary&gt; /// In-memory idempotency cache. Single-orchestrator system - no need for /// distributed cache (Redis, etc.). TTL expiration via periodic cleanup. /// /// Behavior: /// - Key exists + Completed → return cached result (no re-execution) /// - Key exists + Processing → return 409 Conflict (prevent duplicate work) /// - Key not found → mark as Processing, execute, mark as Completed/Failed /// &lt;/summary&gt; public interface IIdempotencyCache { Task&lt;IdempotencyEntry?&gt; GetAsync(string key, CancellationToken ct = default); Task SetAsync(IdempotencyEntry entry, CancellationToken ct = default); Task RemoveExpiredAsync(CancellationToken ct = default); } public sealed class InMemoryIdempotencyCache : IIdempotencyCache { private readonly ConcurrentDictionary&lt;string, IdempotencyEntry&gt; \_cache = new(); public Task&lt;IdempotencyEntry?&gt; GetAsync(string key, CancellationToken ct = default) { if (\_cache.TryGetValue(key, out var entry)) { if (DateTimeOffset.UtcNow - entry.CreatedAt > entry.Ttl) { \_cache.TryRemove(key, out \_); return Task.FromResult&lt;IdempotencyEntry?&gt;(null); } return Task.FromResult&lt;IdempotencyEntry?&gt;(entry); } return Task.FromResult&lt;IdempotencyEntry?&gt;(null); } public Task SetAsync(IdempotencyEntry entry, CancellationToken ct = default) { \_cache\[entry.Key\] = entry; return Task.CompletedTask; } public Task RemoveExpiredAsync(CancellationToken ct = default) { var now = DateTimeOffset.UtcNow; foreach (var (key, entry) in \_cache) { if (now - entry.CreatedAt > entry.Ttl) \_cache.TryRemove(key, out \_); } return Task.CompletedTask; } }

# 11\. Output Validation Pipeline

namespace SplitBrain.Validation; public enum ValidationSeverity { Pass, Warning, Error } public record ValidationResult { public required ValidationSeverity Severity { get; init; } public required string ValidatorName { get; init; } public required string Message { get; init; } } /// &lt;summary&gt; /// Validators run after every inference response. On Error severity, /// the orchestrator triggers fallback to the next model in the chain. /// &lt;/summary&gt; public interface IOutputValidator { Task&lt;ValidationResult&gt; ValidateAsync( string output, TaskContext context, CancellationToken ct = default); }

### 11.1 Built-in Validators

| **Validator**                 | **Applies To**                         | **What It Checks**                                                                              | **On Failure**                       |
| ----------------------------- | -------------------------------------- | ----------------------------------------------------------------------------------------------- | ------------------------------------ |
| **StructuredOutputValidator** | All tasks                              | JSON/XML well-formedness when structured output is expected                                     | Error → fallback                     |
| ---                           | ---                                    | ---                                                                                             | ---                                  |
| **CodeSyntaxValidator**       | Code tasks (Review, Refactor, TestGen) | Bracket matching, unclosed strings, unbalanced delimiters. Lightweight - no Roslyn compilation. | Error → fallback                     |
| ---                           | ---                                    | ---                                                                                             | ---                                  |
| **LengthBoundsValidator**     | All tasks                              | Not empty, not truncated (abrupt ending), not exceeding max length                              | Error if empty; Warning if truncated |
| ---                           | ---                                    | ---                                                                                             | ---                                  |
| **RefusalDetector**           | All tasks                              | Model refusal patterns ("I cannot", "As an AI", "I'm unable to")                                | Error → fallback                     |
| ---                           | ---                                    | ---                                                                                             | ---                                  |

// Validation pipeline execution public sealed class ValidationPipeline { private readonly IReadOnlyList&lt;IOutputValidator&gt; \_validators; public ValidationPipeline(IEnumerable&lt;IOutputValidator&gt; validators) { \_validators = validators.ToList(); } public async Task&lt;(bool Passed, List<ValidationResult&gt; Results)> ValidateAsync(string output, TaskContext context, CancellationToken ct) { var results = new List&lt;ValidationResult&gt;(); foreach (var validator in \_validators) { var result = await validator.ValidateAsync(output, context, ct); results.Add(result); } var passed = results.All(r => r.Severity != ValidationSeverity.Error); return (passed, results); } }

# 12\. Implementation Phases

## Phase 1: Foundation

**Scope:** Core + Networking + Config

| **Deliverable**                | **Details**                                                                                                         |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------- |
| Solution structure             | All 11 project shells created with correct dependencies. .sln file with solution folders for src/, tests/, config/. |
| ---                            | ---                                                                                                                 |
| NodeConfiguration + nodes.json | Full configuration model, provider configs, JSON serialization with JsonStringEnumConverter.                        |
| ---                            | ---                                                                                                                 |
| IInferenceNode interface       | Complete abstraction with Execute, Stream, GetHealth, ListModels.                                                   |
| ---                            | ---                                                                                                                 |
| OllamaInferenceNode            | Full implementation with correct HttpClient injection (not bypassed). OllamaSharp integration.                      |
| ---                            | ---                                                                                                                 |
| NodeRegistry                   | Immutable swap on reload via Interlocked.Exchange. IOptionsMonitor hot-reload.                                      |
| ---                            | ---                                                                                                                 |
| NodeHealthCheckService         | Background service with per-node SemaphoreSlim, 3-state health.                                                     |
| ---                            | ---                                                                                                                 |
| DI composition root            | SplitBrain.Orchestrator/Program.cs with all service registrations, named HttpClients.                               |
| ---                            | ---                                                                                                                 |

**Success Criteria:** Can discover and monitor Ollama nodes from JSON config. Adding/removing a node in nodes.json causes hot-reload with no code changes.

## Phase 2: Routing + Models

**Scope:** Routing + Models + Resilience

| **Deliverable**                 | **Details**                                                                                           |
| ------------------------------- | ----------------------------------------------------------------------------------------------------- |
| ModelDefinition + ModelRegistry | Model definitions in appsettings.json. Registry tracks availability per node.                         |
| ---                             | ---                                                                                                   |
| Fallback chains                 | Per-TaskType chains with silent skip for missing nodes/models.                                        |
| ---                             | ---                                                                                                   |
| RoutingEngine                   | Weighted scoring (VRAM 0.35, Queue 0.25, ModelFit 0.20, Latency 0.10, Context 0.10) + hard rules.     |
| ---                             | ---                                                                                                   |
| Request queue                   | Per-node FIFO with SemaphoreSlim backpressure. Reroute on queue overflow.                             |
| ---                             | ---                                                                                                   |
| Polly v8 pipelines              | Named HttpClients with retry (3x exponential+jitter), circuit breaker (80%/10/30s), per-node timeout. |
| ---                             | ---                                                                                                   |
| CopilotInferenceNode            | GitHub Copilot SDK integration. Single-turn sessions. Key Vault auth chain.                           |
| ---                             | ---                                                                                                   |

**Success Criteria:** Can route a prompt to the best (node, model) pair, fall back on failure, cloud failover works. Autocomplete never routes to Deep nodes.

## Phase 3: Dashboard + Observability

**Scope:** Dashboard + Observability

| **Deliverable**        | **Details**                                                                       |
| ---------------------- | --------------------------------------------------------------------------------- |
| Blazor Server app      | 6 pages: Overview, Nodes, Models, Tasks, Logs, Settings. SignalR DashboardHub.    |
| ---                    | ---                                                                               |
| Node topology editor   | Settings page with Add/Edit/Remove node forms → saves to nodes.json → hot-reload. |
| ---                    | ---                                                                               |
| Fallback chain editor  | Visual chain builder with drag-and-drop reordering.                               |
| ---                    | ---                                                                               |
| Serilog with all sinks | Console, File (rolling 7-day), Seq (optional), custom SignalRLogSink.             |
| ---                    | ---                                                                               |
| OpenTelemetry          | Custom ActivitySource, token metrics, routing latency histograms.                 |
| ---                    | ---                                                                               |

**Success Criteria:** Can manage entire node topology from browser. Live health updates, log streaming, and token metrics visible in dashboard.

## Phase 4: Agents + MCP

**Scope:** Agents + MCP + Validation

| **Deliverable**             | **Details**                                                                       |
| --------------------------- | --------------------------------------------------------------------------------- |
| Semantic Kernel integration | InferenceNodeChatCompletionService bridge. ChatCompletionAgent creation per role. |
| ---                         | ---                                                                               |
| Agent state flow            | INIT→PLAN→IMPLEMENT→REVIEW→TEST→DONE\|FAIL. Max 4 iterations, 12K tokens.         |
| ---                         | ---                                                                               |
| Agent event log             | Append-only LiteDB store. Replay in StepIndex order. Dashboard task detail view.  |
| ---                         | ---                                                                               |
| MCP server                  | 7 tools registered. Streamable HTTP + stdio transport. Idempotency middleware.    |
| ---                         | ---                                                                               |
| Validation pipeline         | 4 built-in validators. Error severity triggers fallback.                          |
| ---                         | ---                                                                               |

**Success Criteria:** Can execute a bounded agent task via MCP, replay agent decision chain, validation errors trigger automatic fallback.

## Phase 5: Polish + Hardening

**Scope:** Worker service + Deployment + Testing

| **Deliverable**   | **Details**                                                                                 |
| ----------------- | ------------------------------------------------------------------------------------------- |
| SplitBrain.Worker | Lightweight worker service for remote nodes. gRPC/minimal API. Local Ollama management.     |
| ---               | ---                                                                                         |
| Deploy scripts    | setup-orchestrator.ps1, setup-worker.ps1. Self-contained, single-file, ReadyToRun, win-x64. |
| ---               | ---                                                                                         |
| Task history      | LiteDB persistence for completed tasks with full routing + agent trail.                     |
| ---               | ---                                                                                         |
| Alerting rules    | Configurable thresholds (VRAM >90%, latency >2x baseline, circuit breaker open).            |
| ---               | ---                                                                                         |
| Integration tests | End-to-end routing, fallback chain, health check, config reload tests.                      |
| ---               | ---                                                                                         |

**Success Criteria:** End-to-end deployment on two Windows machines from PowerShell scripts. All integration tests pass.

# 13\. NuGet Packages

| **Package**                                  | **Purpose**                                             | **Notes**                                                                             |
| -------------------------------------------- | ------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| Microsoft.SemanticKernel                     | Agent orchestration, IChatCompletionService abstraction | Core agent framework. Provides ChatCompletionAgent, Kernel, chat history.             |
| ---                                          | ---                                                     | ---                                                                                   |
| Microsoft.SemanticKernel.Connectors.Ollama   | Ollama ↔ SK bridge                                      | Currently alpha. Provides OllamaChatCompletionService. May use custom bridge instead. |
| ---                                          | ---                                                     | ---                                                                                   |
| GitHub.Copilot.SDK                           | Copilot CLI agent runtime for cloud node                | Public preview, v0.2.x. Provides CopilotClient, CopilotSession.                       |
| ---                                          | ---                                                     | ---                                                                                   |
| ModelContextProtocol                         | Official MCP C# SDK                                     | MCP server with tool registration, streamable HTTP + stdio transport.                 |
| ---                                          | ---                                                     | ---                                                                                   |
| OllamaSharp                                  | Low-level Ollama API client                             | Used inside OllamaInferenceNode. MUST inject HttpClient for Polly.                    |
| ---                                          | ---                                                     | ---                                                                                   |
| Polly                                        | Resilience pipelines                                    | v8+. Retry, circuit breaker, timeout strategies.                                      |
| ---                                          | ---                                                     | ---                                                                                   |
| Microsoft.Extensions.Http.Resilience         | HttpClient + Polly integration                          | Requires IHttpClientFactory. AddResilienceHandler extension method.                   |
| ---                                          | ---                                                     | ---                                                                                   |
| Serilog.AspNetCore                           | Structured logging                                      | Host integration, request logging, enrichment.                                        |
| ---                                          | ---                                                     | ---                                                                                   |
| Serilog.Sinks.File                           | Rolling file sink                                       | 7-day retention, JSON format.                                                         |
| ---                                          | ---                                                     | ---                                                                                   |
| Serilog.Sinks.Seq                            | Seq server sink (optional)                              | Full-text search and dashboarding.                                                    |
| ---                                          | ---                                                     | ---                                                                                   |
| OpenTelemetry                                | Distributed tracing + metrics                           | Custom ActivitySource and Meter.                                                      |
| ---                                          | ---                                                     | ---                                                                                   |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | OTLP export                                             | For production observability backends (Jaeger, Grafana, etc.).                        |
| ---                                          | ---                                                     | ---                                                                                   |
| LiteDB                                       | Embedded NoSQL database                                 | v5.x. Agent event log + task history. Single-file, zero-config.                       |
| ---                                          | ---                                                     | ---                                                                                   |
| Azure.Identity                               | DefaultAzureCredential                                  | For Key Vault auth in Copilot SDK provider.                                           |
| ---                                          | ---                                                     | ---                                                                                   |
| Azure.Security.KeyVault.Secrets              | Key Vault secret retrieval                              | Copilot API key resolution (first in auth chain).                                     |
| ---                                          | ---                                                     | ---                                                                                   |

# 14\. Key Interfaces Reference

| **Interface**          | **Project** | **Purpose**                                                                   |
| ---------------------- | ----------- | ----------------------------------------------------------------------------- |
| IInferenceNode         | Core        | Provider-agnostic inference execution (Execute, Stream, Health, ListModels)   |
| ---                    | ---         | ---                                                                           |
| IInferenceNodeFactory  | Networking  | Creates IInferenceNode from NodeConfiguration based on provider type          |
| ---                    | ---         | ---                                                                           |
| INodeRegistry          | Networking  | Node state management, topology queries, SaveTopologyAsync for dashboard      |
| ---                    | ---         | ---                                                                           |
| INodeHealthService     | Networking  | Background health monitoring with per-node intervals and SignalR publishing   |
| ---                    | ---         | ---                                                                           |
| IModelRegistry         | Models      | Model availability tracking, model-to-node affinity, capability queries       |
| ---                    | ---         | ---                                                                           |
| IFallbackChainProvider | Models      | Fallback chain resolution per TaskType with silent skip for missing nodes     |
| ---                    | ---         | ---                                                                           |
| IRoutingEngine         | Routing     | Task-to-(node, model) routing with weighted scoring and hard rules            |
| ---                    | ---         | ---                                                                           |
| IRoutingPolicy         | Routing     | Pluggable routing strategy (WeightedScore, RoundRobin, LeastConnections)      |
| ---                    | ---         | ---                                                                           |
| IRequestQueue          | Routing     | Per-node request queue with SemaphoreSlim concurrency and backpressure        |
| ---                    | ---         | ---                                                                           |
| IAgentEventLog         | Agents      | Append-only agent step history. Replay in StepIndex order for post-mortem.    |
| ---                    | ---         | ---                                                                           |
| IOutputValidator       | Validation  | Output quality gate. Error severity triggers fallback to next model.          |
| ---                    | ---         | ---                                                                           |
| IIdempotencyCache      | MCP         | MCP tool call deduplication. In-memory with TTL. 409 on duplicate processing. |
| ---                    | ---         | ---                                                                           |
| IDashboardClient       | Dashboard   | Strongly-typed SignalR client for real-time dashboard updates                 |
| ---                    | ---         | ---                                                                           |

# 15\. Configuration Reference

**Configuration Split**

Node topology lives in nodes.json (separate file) - see Section 3.2. Everything else lives in appsettings.json below. Both files support reloadOnChange: true for hot-reload.

{ "SplitBrain": { "Models": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "DisplayName": "Qwen 2.5 Coder 7B (Q4)", "Family": "Qwen", "PrimaryCapability": "Chat", "SecondaryCapabilities": \["Autocomplete", "AgentStep", "Refactor"\], "QuantizationLevel": "Q4_K_M", "ContextWindow": 8192, "EstimatedVramMB": 4500, "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "qwen2.5-coder:7b-q5_K_M", "DisplayName": "Qwen 2.5 Coder 7B (Q5)", "Family": "Qwen", "PrimaryCapability": "Review", "SecondaryCapabilities": \["Refactor", "TestGeneration", "AgentStep", "Chat"\], "QuantizationLevel": "Q5_K_M", "ContextWindow": 8192, "EstimatedVramMB": 5200, "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "deepseek-coder:6.7b-q4_K_M", "DisplayName": "DeepSeek Coder 6.7B (Q4)", "Family": "DeepSeek", "PrimaryCapability": "Review", "SecondaryCapabilities": \["TestGeneration", "Refactor"\], "QuantizationLevel": "Q4_K_M", "ContextWindow": 8192, "EstimatedVramMB": 4200, "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "nomic-embed-text", "DisplayName": "Nomic Embed Text v1.5", "Family": "Nomic", "PrimaryCapability": "Embedding", "SecondaryCapabilities": \[\], "QuantizationLevel": "F16", "ContextWindow": 8192, "EstimatedVramMB": 270, "PreferredNodeIds": \[\] }, { "ModelId": "gpt-4o", "DisplayName": "GPT-4o (via Copilot SDK)", "Family": "Copilot", "PrimaryCapability": "Review", "SecondaryCapabilities": \["Chat", "Refactor", "TestGeneration", "AgentStep"\], "ContextWindow": 128000, "EstimatedVramMB": 0, "PreferredNodeIds": \["copilot-cloud"\] } \], "FallbackChains": \[ { "TaskType": "Autocomplete", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] } \] }, { "TaskType": "Chat", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "Review", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "deepseek-coder:6.7b-q4_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "Refactor", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "TestGeneration", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "deepseek-coder:6.7b-q4_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "AgentStep", "Steps": \[ { "ModelId": "qwen2.5-coder:7b-q4_K_M", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "qwen2.5-coder:7b-q5_K_M", "PreferredNodeIds": \["tower-gpu"\] }, { "ModelId": "gpt-4o", "PreferredNodeIds": \["copilot-cloud"\] } \] }, { "TaskType": "Embedding", "Steps": \[ { "ModelId": "nomic-embed-text", "PreferredNodeIds": \["home-laptop"\] }, { "ModelId": "nomic-embed-text", "PreferredNodeIds": \["tower-gpu"\] } \] } \], "Routing": { "Weights": { "Vram": 0.35, "QueueDepth": 0.25, "ModelFit": 0.20, "Latency": 0.10, "ContextFit": 0.10 }, "DefaultPolicy": "WeightedScore", "HardRules": { "AutocompleteForcesFastRole": true, "LargeContextThresholdTokens": 5000, "CloudFailoverOnAllLocalUnavailable": true } }, "Agents": { "MaxIterationsPerLoop": 4, "MaxTokensPerLoop": 12000, "DefaultTimeoutSeconds": 300, "RoleNodeMapping": { "Architect": "Fast", "Coder": "Fast", "Reviewer": "Deep", "Tester": "Deep" } }, "Dashboard": { "LogStreamingMinLevel": "Information", "LogBatchSize": 20, "HealthSnapshotRetentionMinutes": 60 }, "MCP": { "IdempotencyTtlSeconds": 300, "Transport": { "Http": { "Enabled": true, "Port": 5100, "Path": "/mcp" }, "Stdio": { "Enabled": true } } }, "TokenCosts": { "PerModelCostPer1KTokens": { "qwen2.5-coder:7b-q4_K_M": 0.0, "qwen2.5-coder:7b-q5_K_M": 0.0, "deepseek-coder:6.7b-q4_K_M": 0.0, "nomic-embed-text": 0.0, "gpt-4o": 0.005 } } }, "Serilog": { "MinimumLevel": { "Default": "Information", "Override": { "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Information", "System": "Warning" } }, "WriteTo": \[ { "Name": "Console" }, { "Name": "File", "Args": { "path": "logs/splitbrain-.log", "rollingInterval": "Day", "retainedFileCountLimit": 7, "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact" } } \], "Enrich": \["FromLogContext", "WithMachineName", "WithThreadId"\] }, "AllowedHosts": "\*" }

- End of Document -

SplitBrain.AI Unified Architecture Plan v2 | April 22, 2026 | github.com/TheMasonX/SplitBrain.AI