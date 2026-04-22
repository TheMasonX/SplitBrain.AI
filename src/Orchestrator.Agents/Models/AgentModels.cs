namespace Orchestrator.Agents.Models;

// ---------------------------------------------------------------------------
// Enumerations
// ---------------------------------------------------------------------------

/// <summary>States in the bounded agent loop (§9.2).</summary>
public enum AgentState
{
    Init,
    Plan,
    Implement,
    Review,
    Test,
    Done,
    Failed
}

/// <summary>Agent roles mapped to nodes per §9.4.</summary>
public enum AgentRole
{
    /// <summary>Plans the task decomposition. Routes to Node A.</summary>
    Architect,

    /// <summary>Generates the code change. Routes to Node A.</summary>
    Coder,

    /// <summary>Reviews the produced diff. Routes to Node B.</summary>
    Reviewer,

    /// <summary>Generates and verifies tests. Routes to Node B.</summary>
    Tester
}

// ---------------------------------------------------------------------------
// Immutable request / result
// ---------------------------------------------------------------------------

/// <summary>Input to a single bounded agent run.</summary>
public sealed record AgentRequest
{
    /// <summary>High-level objective in natural language.</summary>
    public required string Goal { get; init; }

    /// <summary>
    /// Repository / working-directory root the agent may read and patch.
    /// If null the agent operates without file access.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Optional extra context injected into every prompt (e.g. stack traces, prior review).</summary>
    public string? Context { get; init; }
}

/// <summary>Outcome of a bounded agent run.</summary>
public sealed record AgentResult
{
    public bool Success { get; init; }
    public AgentState FinalState { get; init; }
    public string Summary { get; init; } = string.Empty;

    /// <summary>Unified diff produced by the Coder step (may be empty on failure).</summary>
    public string Diff { get; init; } = string.Empty;

    /// <summary>Ordered log of every step taken.</summary>
    public IReadOnlyList<AgentStep> Steps { get; init; } = [];

    /// <summary>Human-readable reason for termination (useful when Success = false).</summary>
    public string? AbortReason { get; init; }

    public int TotalIterations { get; init; }
    public int TotalTokensUsed { get; init; }
}

// ---------------------------------------------------------------------------
// Per-step record (immutable snapshot)
// ---------------------------------------------------------------------------

/// <summary>A single prompt/response exchange in the agent loop.</summary>
public sealed record AgentStep
{
    public AgentRole Role { get; init; }
    public AgentState State { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public int TokensEstimated { get; init; }
    public bool Success { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

// ---------------------------------------------------------------------------
// Mutable session state (internal to AgentOrchestrator)
// ---------------------------------------------------------------------------

/// <summary>
/// Mutable run-state for a single agent session.
/// Not part of the public API — used only inside <see cref="Orchestrator.Agents.AgentOrchestrator"/>.
/// </summary>
internal sealed class AgentSession
{
    internal string TaskId { get; } = Guid.NewGuid().ToString("N");
    internal AgentState State { get; set; } = AgentState.Init;
    internal int Iteration { get; set; }
    internal int TokensUsed { get; set; }
    internal string LastDiff { get; set; } = string.Empty;
    internal string LastReview { get; set; } = string.Empty;
    internal string PreviousDiff { get; set; } = string.Empty;
    internal int ConsecutiveFailures { get; set; }
    internal List<AgentStep> Steps { get; } = [];

    internal void RecordStep(AgentStep step)
    {
        Steps.Add(step);
        TokensUsed += step.TokensEstimated;
    }
}
