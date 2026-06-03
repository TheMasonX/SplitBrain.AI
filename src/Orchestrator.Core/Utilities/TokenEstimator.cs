namespace Orchestrator.Core.Utilities;

/// <summary>
/// Rough token estimation: ~4 characters per token.
/// Single canonical implementation replacing duplicates in
/// RoutingService, AgentOrchestrator, and KernelPlannerService.
/// </summary>
public static class TokenEstimator
{
    public static int Estimate(string text) => text.Length / 4;
}
