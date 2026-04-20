namespace Orchestrator.Core.Models;

/// <summary>A single telemetry record captured per inference request.</summary>
public sealed class RequestMetric
{
    public string TaskId { get; init; } = default!;
    public string NodeId { get; init; } = default!;
    public string Model { get; init; } = default!;
    public string TaskType { get; init; } = default!;
    public int TokensIn { get; init; }
    public int TokensOut { get; init; }
    public int LatencyMs { get; init; }
    public bool Success { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}
