using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Orchestrator.Core.Observability;

/// <summary>
/// Central OpenTelemetry ActivitySource and Meters for SplitBrain.AI.
/// Reference this static class from any layer to emit traces and metrics.
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "SplitBrain.AI";
    public const string ServiceVersion = "2.0.0";

    /// <summary>Distributed tracing source. Wrap inference calls in activities.</summary>
    public static readonly ActivitySource Source = new(ServiceName, ServiceVersion);

    /// <summary>Meter for all SplitBrain.AI metrics.</summary>
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // ---------------------------------------------------------------------------
    // Token metrics
    // ---------------------------------------------------------------------------

    /// <summary>Inference throughput in tokens per second.</summary>
    public static readonly Histogram<double> TokensPerSecond =
        Meter.CreateHistogram<double>("splitbrain.tokens.per_second", "tokens/s",
            "Inference throughput in tokens per second");

    /// <summary>Total prompt tokens consumed across all requests.</summary>
    public static readonly Counter<long> PromptTokensTotal =
        Meter.CreateCounter<long>("splitbrain.tokens.prompt", "tokens",
            "Total prompt tokens consumed");

    /// <summary>Total completion tokens generated across all requests.</summary>
    public static readonly Counter<long> CompletionTokensTotal =
        Meter.CreateCounter<long>("splitbrain.tokens.completion", "tokens",
            "Total completion tokens generated");

    /// <summary>Estimated USD cost for cloud inference (0 for local models).</summary>
    public static readonly Counter<double> EstimatedCostUsd =
        Meter.CreateCounter<double>("splitbrain.cost.estimated_usd", "USD",
            "Estimated cost for cloud inference");

    // ---------------------------------------------------------------------------
    // Routing metrics
    // ---------------------------------------------------------------------------

    /// <summary>Time taken to compute a routing decision in milliseconds.</summary>
    public static readonly Histogram<double> RoutingLatencyMs =
        Meter.CreateHistogram<double>("splitbrain.routing.latency_ms", "ms",
            "Time to compute a routing decision");

    /// <summary>Number of fallback steps triggered.</summary>
    public static readonly Counter<long> FallbacksTriggered =
        Meter.CreateCounter<long>("splitbrain.routing.fallbacks", "count",
            "Number of fallback routing steps triggered");

    // ---------------------------------------------------------------------------
    // Queue metrics
    // ---------------------------------------------------------------------------

    /// <summary>Current queue depth across all nodes.</summary>
    public static readonly UpDownCounter<int> QueueDepth =
        Meter.CreateUpDownCounter<int>("splitbrain.queue.depth", "requests",
            "Current queue depth across all nodes");

    // ---------------------------------------------------------------------------
    // Agent metrics
    // ---------------------------------------------------------------------------

    /// <summary>Number of agent tasks completed successfully.</summary>
    public static readonly Counter<long> AgentTasksCompleted =
        Meter.CreateCounter<long>("splitbrain.agent.tasks_completed", "count",
            "Agent tasks completed successfully");

    /// <summary>Number of agent tasks that failed or hit iteration limit.</summary>
    public static readonly Counter<long> AgentTasksFailed =
        Meter.CreateCounter<long>("splitbrain.agent.tasks_failed", "count",
            "Agent tasks failed or aborted");

    /// <summary>Distribution of agent loop iteration counts.</summary>
    public static readonly Histogram<int> AgentIterations =
        Meter.CreateHistogram<int>("splitbrain.agent.iterations", "iterations",
            "Number of iterations per agent task");

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Records token usage for a completed inference call.
    /// Divide-by-zero guarded: skips throughput if duration is zero.
    /// </summary>
    public static void RecordTokenUsage(
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string nodeId,
        string modelId)
    {
        var tags = new TagList
        {
            { "node_id", nodeId },
            { "model_id", modelId }
        };

        PromptTokensTotal.Add(promptTokens, tags);
        CompletionTokensTotal.Add(completionTokens, tags);

        if (duration.TotalSeconds > 0)
            TokensPerSecond.Record(completionTokens / duration.TotalSeconds, tags);
    }
}
