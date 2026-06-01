ï»¿using Microsoft.Extensions.Hosting;
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

// ---------------------------------------------------------------------------
// Shared infrastructure: options, HTTP clients, nodes, queues, routing, etc.
// ---------------------------------------------------------------------------
builder.Services.AddSplitBrainInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------
// MCP-host-specific services
// ---------------------------------------------------------------------------

// File logging for input/output capture
builder.Services.Configure<FileLoggingOptions>(
    builder.Configuration.GetSection(FileLoggingOptions.Section));
builder.Services.AddSingleton<ILoggingService, FileLoggingService>();

// No SignalR dashboard in MCP host — use no-op publishers
builder.Services.AddSingleton<INodeHealthPublisher, NullNodeHealthPublisher>();
builder.Services.AddSingleton<ILogEntryPublisher, NullLogEntryPublisher>();

// MCP idempotency cache (TTL-based deduplication, in-process)
builder.Services.AddSingleton<IIdempotencyCache, InMemoryIdempotencyCache>();

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
