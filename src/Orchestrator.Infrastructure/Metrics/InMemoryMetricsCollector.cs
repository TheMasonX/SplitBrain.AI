using System.Collections.Concurrent;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Metrics;

/// <summary>
/// Thread-safe ring-buffer metrics store.
/// Retains the last <see cref="Capacity"/> records in memory;
/// older records are dropped as new ones arrive.
/// </summary>
public sealed class InMemoryMetricsCollector : IMetricsCollector
{
    private const int Capacity = 1_000;
    private readonly ConcurrentQueue<RequestMetric> _queue = new();

    public void Record(RequestMetric metric)
    {
        _queue.Enqueue(metric);
        // Trim to capacity
        while (_queue.Count > Capacity)
            _queue.TryDequeue(out _);
    }

    public IReadOnlyList<RequestMetric> GetRecent(int count = 100)
    {
        return _queue
            .OrderByDescending(m => m.RecordedAt)
            .Take(count)
            .ToList()
            .AsReadOnly();
    }

    public MetricsSummary GetSummary()
    {
        var all = _queue.ToArray();
        if (all.Length == 0)
            return new MetricsSummary();

        return new MetricsSummary
        {
            TotalRequests  = all.Length,
            SuccessCount   = all.Count(m => m.Success),
            FailureCount   = all.Count(m => !m.Success),
            AvgLatencyMs   = all.Average(m => m.LatencyMs),
            TotalTokensIn  = all.Sum(m => (long)m.TokensIn),
            TotalTokensOut = all.Sum(m => (long)m.TokensOut),
            RequestsByNode = all
                .GroupBy(m => m.NodeId)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
