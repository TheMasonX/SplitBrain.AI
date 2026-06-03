namespace Orchestrator.Core.Models;

public record TokenUsageRecord
{
    public required string TaskId { get; init; }
    public required string ModelId { get; init; }
    public required string NodeId { get; init; }
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public required DateTimeOffset Timestamp { get; init; }
    public required TimeSpan InferenceDuration { get; init; }
    /// <summary>Divide-by-zero guarded throughput calculation.</summary>
    public double TokensPerSecond =>
        InferenceDuration.TotalSeconds > 0
            ? CompletionTokens / InferenceDuration.TotalSeconds
            : 0;
    public decimal EstimatedCostUSD { get; init; }
}
