using Orchestrator.Core.Enums;

namespace Orchestrator.Core.Models;

public record RoutingWeights
{
    public double Vram { get; init; } = 0.35;
    public double QueueDepth { get; init; } = 0.25;
    public double ModelFit { get; init; } = 0.20;
    public double Latency { get; init; } = 0.10;
    public double ContextFit { get; init; } = 0.10;
}

public record RoutingDecision
{
    public required string TaskId { get; init; }
    public required TaskType TaskType { get; init; }
    public required string SelectedNodeId { get; init; }
    public required string SelectedModelId { get; init; }
    public required double CompositeScore { get; init; }
    public required TimeSpan DecisionDuration { get; init; }
    public required List<RoutingCandidate> CandidatesEvaluated { get; init; }
    public required List<string> HardRulesApplied { get; init; }
    public int FallbackStepIndex { get; init; } = 0;
    public string? FallbackReason { get; init; }
}

public record RoutingCandidate
{
    public required string NodeId { get; init; }
    public required string ModelId { get; init; }
    public required double Score { get; init; }
    public required Dictionary<string, double> ScoreBreakdown { get; init; }
    public bool Excluded { get; init; }
    public string? ExclusionReason { get; init; }
}
