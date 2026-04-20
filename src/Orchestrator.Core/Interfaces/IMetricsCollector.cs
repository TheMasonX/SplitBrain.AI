using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Records per-request telemetry and exposes recent history.
/// Implementations must be thread-safe.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>Records a completed inference request.</summary>
    void Record(RequestMetric metric);

    /// <summary>Returns the most recent metrics, newest first.</summary>
    IReadOnlyList<RequestMetric> GetRecent(int count = 100);

    /// <summary>Returns a lightweight aggregate snapshot.</summary>
    MetricsSummary GetSummary();
}

/// <summary>Aggregate metrics snapshot.</summary>
public sealed class MetricsSummary
{
    public int TotalRequests { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public double AvgLatencyMs { get; init; }
    public long TotalTokensIn { get; init; }
    public long TotalTokensOut { get; init; }
    public Dictionary<string, int> RequestsByNode { get; init; } = new();
}
