using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Events;
using Orchestrator.Agents;
using Orchestrator.Agents.Sandbox;
using Orchestrator.Core.Interfaces;
using Orchestrator.Hosting.DependencyInjection;
using Orchestrator.Infrastructure.AgentLog;
using Orchestrator.Infrastructure.Logging;
using Orchestrator.Mcp.Idempotency;
using Orchestrator.Mcp.Tools;
using Orchestrator.Mcp.WriteAccess;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "nodes.json"),
    optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("nodes.json", optional: true, reloadOnChange: true);

LoggerConfiguration loggerBuilder = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .WriteTo.File(
        path: Path.Combine(Path.GetTempPath(), "splitbrain-mcp-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);

if (OperatingSystem.IsWindows())
    loggerBuilder = loggerBuilder.WriteTo.EventLog("SplitBrain MCP", manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Information);

Log.Logger = loggerBuilder.CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

// ---------------------------------------------------------------------------
// Shared infrastructure: options, HTTP clients, nodes, queues, routing, etc.
// ---------------------------------------------------------------------------
builder.Services.AddSplitBrainInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------
// MCP-host-specific services
// ---------------------------------------------------------------------------

// P0.5: Write-access gate — Disabled by default; configure via Mcp:WriteAccess in appsettings.json
builder.Services.Configure<WriteAccessOptions>(builder.Configuration.GetSection(WriteAccessOptions.Section));
builder.Services.AddSingleton<WriteAccessGuard>();

// File logging for input/output capture
builder.Services.Configure<FileLoggingOptions>(
    builder.Configuration.GetSection(FileLoggingOptions.Section));
builder.Services.AddSingleton<ILoggingService, FileLoggingService>();

// No SignalR dashboard in MCP host — use no-op publishers
builder.Services.AddSingleton<INodeHealthPublisher, NullNodeHealthPublisher>();
builder.Services.AddSingleton<ILogEntryPublisher, NullLogEntryPublisher>();
builder.Services.AddSingleton<IInferenceNodeFactory, Orchestrator.Infrastructure.Registry.InferenceNodeFactory>();

// MCP idempotency cache (TTL-based deduplication, in-process)
builder.Services.AddSingleton<IIdempotencyCache, InMemoryIdempotencyCache>();

// Phase 3 — Agent system
builder.Services.AddSingleton<ICodeSandbox, ProcessCodeSandbox>();
builder.Services.AddSingleton<IAgentEventLog>(_ => new LiteDbAgentEventLog());
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    // Existing tools (Phase 0 fixed)
    .WithTools<ReviewCodeTool>()
    .WithTools<RefactorCodeTool>()
    .WithTools<GenerateTestsTool>()
    .WithTools<SearchCodebaseTool>()
    .WithTools<ApplyPatchTool>()
    .WithTools<RunTestsTool>()
    .WithTools<AgentTaskTool>()
    // Phase 1 tools (from feature/p0-p1-tools reconciliation)
    .WithTools<ReadFileTool>()
    .WithTools<ListFilesTool>()
    .WithTools<WriteFileTool>()
    .WithTools<QueryAllowedRootTool>()
    .WithTools<ExplainCodeTool>();

// OTel: only export if OTEL_EXPORTER_OTLP_ENDPOINT is explicitly set (prevents startup noise)
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var otelBuilder = builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("SplitBrain.Mcp", serviceVersion: "3.0.0"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());

if (!string.IsNullOrEmpty(otlpEndpoint))
{
    otelBuilder
        .WithTracing(t => t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!)))
        .WithMetrics(m => m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!)));
}

var app = builder.Build();
app.MapMcp("/mcp");
await app.RunAsync();
