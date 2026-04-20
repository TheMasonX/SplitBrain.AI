using Orchestrator.Agents.Models;

namespace Orchestrator.Agents;

/// <summary>
/// Drives a bounded, observable agent loop (§9).
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs a single bounded agent session for the given <paramref name="request"/>.
    ///
    /// The loop follows: INIT → PLAN → IMPLEMENT → REVIEW → TEST → DONE | FAIL
    /// with a maximum of 4 iterations and a 12 000-token budget per loop (§9.3).
    /// </summary>
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken cancellationToken = default);
}
