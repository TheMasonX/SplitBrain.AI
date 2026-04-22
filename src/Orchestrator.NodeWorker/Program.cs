using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NodeClient.Ollama;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.Metrics;
using Orchestrator.NodeWorker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OllamaClientOptions>(
    builder.Configuration.GetSection(OllamaClientOptions.Section));

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();
builder.Services.AddSingleton<NodeBInferenceNode>();
builder.Services.AddSingleton<IInferenceNode>(sp => sp.GetRequiredService<NodeBInferenceNode>());
builder.Services.AddSingleton<INodeHealthCache, InMemoryNodeHealthCache>();
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddHostedService<NodeWorkerService>();

var app = builder.Build();

// -------------------------------------------------------------------------
// GET /health — returns current node status and last health snapshot
// -------------------------------------------------------------------------
app.MapGet("/health", async (IInferenceNode node, INodeHealthCache cache, CancellationToken ct) =>
{
    // Always return the cached snapshot so this endpoint is fast
    var cached = cache.Get(node.NodeId);
    if (cached is not null)
        return Results.Ok(new
        {
            nodeId = cached.NodeId,
            status = cached.Status.ToString(),
            queueDepth = cached.QueueDepth,
            availableVramMb = cached.AvailableVramMb,
            checkedAt = cached.CheckedAt
        });

    // Cold cache: probe live
    var nodeHealth = await node.GetHealthAsync(ct);
    var legacyStatus = nodeHealth.State switch
    {
        Orchestrator.Core.Models.HealthState.Healthy => Orchestrator.Core.Enums.NodeStatus.Healthy,
        Orchestrator.Core.Models.HealthState.Degraded => Orchestrator.Core.Enums.NodeStatus.Degraded,
        _ => Orchestrator.Core.Enums.NodeStatus.Unavailable
    };
    var legacy = new NodeHealth
    {
        NodeId = node.NodeId,
        Status = legacyStatus,
        QueueDepth = nodeHealth.ActiveRequests,
        AvailableVramMb = nodeHealth.VramLoadedMB.HasValue && nodeHealth.VramTotalMB.HasValue
            ? (int)(nodeHealth.VramTotalMB.Value - nodeHealth.VramLoadedMB.Value)
            : 0,
        CheckedAt = nodeHealth.LastChecked
    };
    cache.Set(legacy);
    return Results.Ok(new
    {
        nodeId = legacy.NodeId,
        status = legacy.Status.ToString(),
        queueDepth = legacy.QueueDepth,
        availableVramMb = legacy.AvailableVramMb,
        checkedAt = legacy.CheckedAt
    });
});

// -------------------------------------------------------------------------
// POST /inference — executes an InferenceRequest directly on this node
// -------------------------------------------------------------------------
app.MapPost("/inference", async (HttpRequest req, IInferenceNode node, IMetricsCollector metrics, CancellationToken ct) =>
{
    InferenceRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<InferenceRequest>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct);
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON body." });
    }

    if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
        return Results.BadRequest(new { error = "prompt is required." });

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var result = await node.ExecuteAsync(request, ct);
        sw.Stop();

        metrics.Record(new RequestMetric
        {
            TaskId    = Guid.NewGuid().ToString("N"),
            NodeId    = result.NodeId,
            Model     = result.Model,
            TaskType  = "Remote",
            TokensIn  = result.TokensIn,
            TokensOut = result.TokensOut,
            LatencyMs = result.LatencyMs,
            Success   = true
        });

        return Results.Ok(result);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(503);
    }
    catch (Exception ex)
    {
        sw.Stop();
        metrics.Record(new RequestMetric
        {
            TaskId    = Guid.NewGuid().ToString("N"),
            NodeId    = node.NodeId,
            Model     = node.Capabilities.Model,
            TaskType  = "Remote",
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Success   = false
        });
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// -------------------------------------------------------------------------
// GET /metrics — aggregated request telemetry
// -------------------------------------------------------------------------
app.MapGet("/metrics", (IMetricsCollector metrics) =>
    Results.Ok(metrics.GetSummary()));

app.MapGet("/metrics/recent", (IMetricsCollector metrics, int count = 50) =>
    Results.Ok(metrics.GetRecent(count)));

await app.RunAsync();
