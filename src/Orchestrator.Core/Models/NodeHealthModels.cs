namespace Orchestrator.Core.Models;

public enum HealthState
{
    Healthy,
    Degraded,
    Unavailable
}

public record NodeHealthStatus
{
    public required HealthState State { get; init; }
    public required DateTimeOffset LastChecked { get; init; }
    public double LatencyMs { get; init; }
    public IReadOnlyList<string> AvailableModels { get; init; } = [];
    public IReadOnlyList<RunningModelInfo> RunningModels { get; init; } = [];
    public long? VramLoadedMB { get; init; }
    public long? VramTotalMB { get; init; }
    public string? ErrorMessage { get; init; }
    public int ActiveRequests { get; init; }
}

public record RunningModelInfo
{
    public required string ModelId { get; init; }
    public required long SizeVramBytes { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record InferenceChunk
{
    public required string Content { get; init; }
    public int? TokensGenerated { get; init; }
    public bool IsFinal { get; init; }
    public InferenceResult? FinalResult { get; init; }
}

public record ModelInfo
{
    public required string ModelId { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
}
