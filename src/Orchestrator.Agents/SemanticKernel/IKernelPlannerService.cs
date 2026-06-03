namespace Orchestrator.Agents.SemanticKernel;

/// <summary>
/// High-level facade over a Semantic Kernel instance wired to SplitBrain inference nodes.
/// Supports single-shot chat and simple sequential planning.
/// </summary>
public interface IKernelPlannerService
{
    /// <summary>
    /// Runs a single prompt through the SK kernel and returns the text reply.
    /// </summary>
    Task<string> InvokeAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decomposes <paramref name="goal"/> into steps using the planner node, then
    /// executes each step in sequence through the coder node.
    /// Returns the final aggregated output.
    /// </summary>
    Task<KernelPlanResult> PlanAndExecuteAsync(string goal, CancellationToken cancellationToken = default);
}

/// <summary>Result of a <see cref="IKernelPlannerService.PlanAndExecuteAsync"/> call.</summary>
public sealed record KernelPlanResult
{
    public required string Goal { get; init; }
    public required IReadOnlyList<KernelPlanStep> Steps { get; init; }
    public required string FinalOutput { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int TotalTokensEstimate { get; init; }
}

/// <summary>A single executed step within a plan.</summary>
public sealed record KernelPlanStep
{
    public required int Index { get; init; }
    public required string Description { get; init; }
    public required string Output { get; init; }
    public int LatencyMs { get; init; }
}
