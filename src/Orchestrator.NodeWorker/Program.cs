using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeClient.LlamaCpp;
using NodeClient.Ollama;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.Metrics;
using Orchestrator.NodeWorker;

var builder = WebApplication.CreateBuilder(args);

// Signal Windows SCM that this process is a Windows Service (no-op when run interactively)
builder.Host.UseWindowsService();

// -------------------------------------------------------------------------
// Backend selection: set NODE_B_BACKEND=llamacpp to use llama.cpp server,
// leave unset (or set to "ollama") for the default Ollama backend.
//
// llama.cpp backend: configure "LlamaCppNode" section in appsettings.NodeB.json
//   and launch llama-server on Machine B before starting this worker.
//   Key server flags: --n-cpu-moe 25 --no-mmap --mlock
//     --cache-type-k turbo4 --cache-type-v turbo3
//     --host 0.0.0.0 --port 8080
//
// Ollama backend: configure "OllamaNode" section (existing behaviour).
// -------------------------------------------------------------------------
var backend = Environment.GetEnvironmentVariable("NODE_B_BACKEND") ?? "ollama";

if (backend.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<LlamaCppClientOptions>(
        builder.Configuration.GetSection(LlamaCppClientOptions.Section));

    // Timeout is set here via ConfigureHttpClient, not in LlamaCppClient constructor.
    builder.Services.AddHttpClient<ILlamaCppClient, LlamaCppClient>()
        .ConfigureHttpClient((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlamaCppClientOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

    builder.Services.AddSingleton<LlamaCppInferenceNode>();
    builder.Services.AddSingleton<IInferenceNode>(sp =>
        sp.GetRequiredService<LlamaCppInferenceNode>());
}
else
{
    builder.Services.Configure<OllamaClientOptions>(
        builder.Configuration.GetSection(OllamaClientOptions.Section));

    builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();
    builder.Services.AddSingleton<NodeBInferenceNode>();
    builder.Services.AddSingleton<IInferenceNode>(sp =>
        sp.GetRequiredService<NodeBInferenceNode>());
}

builder.Services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddHostedService<NodeWorkerService>();

// -------------------------------------------------------------------------
// Optional bearer token auth for the inference endpoints.
// Set NodeWorker:Auth:RequireToken=true in appsettings.json to enable.
// Default: false (unauthenticated — suitable for trusted LAN deployments).
// When enabled, set NodeWorker:Auth:Token or SPLITBRAIN_WORKER_TOKEN env var.
// If RequireToken=true but no token is configured, a one-time ephemeral token
// is generated and logged at startup (Warning level).
// -------------------------------------------------------------------------
var requireAuth = builder.Configuration.GetValue<bool>("NodeWorker:Auth:RequireToken", false);
var workerToken = builder.Configuration["NodeWorker:Auth:Token"]
    ?? Environment.GetEnvironmentVariable("SPLITBRAIN_WORKER_TOKEN")
    ?? string.Empty;

if (requireAuth && string.IsNullOrWhiteSpace(workerToken))
{
    workerToken = Guid.NewGuid().ToString("N");
    // Log via the builder's logging provider (available before Build())
    Console.Error.WriteLine(
        $"[WARN] NodeWorker:Auth:RequireToken=true but no token is configured. " +
        $"Generated ephemeral token: {workerToken}  " +
        $"Set NodeWorker:Auth:Token in appsettings.json to persist across restarts.");
}

var app = builder.Build();

// Auth middleware
var sharedKey = app.Configuration["Auth:SharedKey"];
if (!string.IsNullOrWhiteSpace(sharedKey))
{
    app.Use(async (ctx, next) =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/health"))
        {
            if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) ||
                providedKey != sharedKey)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"unauthorized\",\"hint\":\"Provide X-Api-Key header matching Auth:SharedKey config\"}");
                return;
            }
        }
        await next(ctx);
    });
}
app.MapGet("/health", async (IInferenceNode node, INodeHealthCache cache, CancellationToken ct) =>
{
    var cached = cache.Get(node.NodeId);
    if (cached is not null)
        return Results.Ok(new { nodeId = cached.NodeId, status = cached.Status.ToString(), queueDepth = cached.QueueDepth, availableVramMb = cached.AvailableVramMb, checkedAt = cached.CheckedAt });
    var nodeHealth = await node.GetHealthAsync(ct);
    var legacyStatus = nodeHealth.State switch
    {
        Orchestrator.Core.Models.HealthState.Healthy => Orchestrator.Core.Enums.NodeStatus.Healthy,
        Orchestrator.Core.Models.HealthState.Degraded => Orchestrator.Core.Enums.NodeStatus.Degraded,
        _ => Orchestrator.Core.Enums.NodeStatus.Unavailable
    };
    var legacy = new NodeHealth { NodeId = node.NodeId, Status = legacyStatus, QueueDepth = nodeHealth.ActiveRequests, AvailableVramMb = nodeHealth.VramLoadedMB.HasValue && nodeHealth.VramTotalMB.HasValue ? (int)(nodeHealth.VramTotalMB.Value - nodeHealth.VramLoadedMB.Value) : 0, CheckedAt = nodeHealth.LastChecked };
    cache.Set(legacy);
    return Results.Ok(new { nodeId = legacy.NodeId, status = legacy.Status.ToString(), queueDepth = legacy.QueueDepth, availableVramMb = legacy.AvailableVramMb, checkedAt = legacy.CheckedAt });
});

app.MapPost("/inference", async (HttpRequest req, IInferenceNode node, IMetricsCollector metrics, CancellationToken ct) =>
{
    InferenceRequest? request;
    try { request = await JsonSerializer.DeserializeAsync<InferenceRequest>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct); }
    catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }
    if (request is null || string.IsNullOrWhiteSpace(request.Prompt)) return Results.BadRequest(new { error = "prompt is required." });
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var result = await node.ExecuteAsync(request, ct);
        sw.Stop();
        metrics.Record(new RequestMetric { TaskId = Guid.NewGuid().ToString("N"), NodeId = result.NodeId, Model = result.Model, TaskType = "Remote", TokensIn = result.TokensIn, TokensOut = result.TokensOut, LatencyMs = result.LatencyMs, Success = true });
        return Results.Ok(result);
    }
    catch (OperationCanceledException) { return Results.StatusCode(503); }
    catch (Exception ex)
    {
        sw.Stop();
        metrics.Record(new RequestMetric { TaskId = Guid.NewGuid().ToString("N"), NodeId = node.NodeId, Model = node.Capabilities.Model, TaskType = "Remote", LatencyMs = (int)sw.ElapsedMilliseconds, Success = false });
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

app.MapGet("/models", async (IInferenceNode node, CancellationToken ct) => { var models = await node.ListModelsAsync(ct); return Results.Ok(models); });
app.MapGet("/metrics", (IMetricsCollector metrics) => Results.Ok(metrics.GetSummary()));
app.MapGet("/metrics/recent", (IMetricsCollector metrics, int count = 50) => Results.Ok(metrics.GetRecent(count)));

await app.RunAsync();
