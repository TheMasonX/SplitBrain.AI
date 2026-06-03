using Microsoft.Extensions.Logging;
using Orchestrator.Agents.Models;
using Orchestrator.Agents.Sandbox;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Utilities;

namespace Orchestrator.Agents;

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private const int MaxIterations      = 4;
    private const int MaxTokensPerLoop   = 12_000;
    private const int MaxConsecFailures  = 2;

    private readonly IRoutingService _routing;
    private readonly ICodeSandbox    _sandbox;
    private readonly IAgentEventLog  _eventLog;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(IRoutingService routing, ICodeSandbox sandbox, IAgentEventLog eventLog, ILogger<AgentOrchestrator> logger)
    {
        _routing  = routing;
        _sandbox  = sandbox;
        _eventLog = eventLog;
        _logger   = logger;
    }

    private Task LogStepAsync(AgentSession session, AgentStepType type, string summary, CancellationToken ct, int? tokensConsumed = null)
        => _eventLog.AppendAsync(new AgentStepEvent { TaskId = session.TaskId, StepIndex = session.Steps.Count, Timestamp = DateTimeOffset.UtcNow, StepType = type, Summary = summary, TokensConsumed = tokensConsumed }, ct);

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Agent started -- goal: {Goal}", request.Goal);
        var session = new AgentSession();
        await LogStepAsync(session, AgentStepType.Init, $"Goal: {request.Goal}", cancellationToken);
        session.State = AgentState.Plan;

        while (!cancellationToken.IsCancellationRequested)
        {
            var abortReason = CheckAbortConditions(session);
            if (abortReason is not null) { _logger.LogWarning("Agent aborting: {Reason}", abortReason); return BuildResult(session, success: false, abortReason); }

            switch (session.State)
            {
                case AgentState.Plan: await PlanAsync(request, session, cancellationToken); break;
                case AgentState.Implement: await ImplementAsync(request, session, cancellationToken); break;
                case AgentState.Review: await ReviewAsync(request, session, cancellationToken); break;
                case AgentState.Test: await TestAsync(request, session, cancellationToken); break;
                case AgentState.Done:
                    _logger.LogInformation("Agent completed successfully after {Iter} iteration(s)", session.Iteration);
                    await LogStepAsync(session, AgentStepType.Done, $"Completed after {session.Iteration} iteration(s)", cancellationToken, tokensConsumed: session.TokensUsed);
                    return BuildResult(session, success: true, abortReason: null);
                case AgentState.Failed:
                    await LogStepAsync(session, AgentStepType.Fail, "Agent state machine reached Failed", cancellationToken);
                    return BuildResult(session, success: false, "Agent state machine reached Failed");
                default: return BuildResult(session, success: false, $"Unexpected state: {session.State}");
            }
        }
        return BuildResult(session, success: false, "Cancelled");
    }

    private async Task PlanAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] PLAN", session.Iteration);
        await LogStepAsync(session, AgentStepType.Plan, $"Planning iteration {session.Iteration}", ct);
        var prevState = session.State;
        var prompt = BuildPlanPrompt(request, session);
        var result = await CallNodeAsync(AgentRole.Architect, TaskType.Chat, prompt, session, ct);
        if (result is not null) session.State = AgentState.Implement;
        else { session.ConsecutiveFailures++; if (session.State == prevState) session.State = AgentState.Failed; }
    }

    private async Task ImplementAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] IMPLEMENT", session.Iteration);
        await LogStepAsync(session, AgentStepType.Implement, $"Implementing iteration {session.Iteration}", ct);
        session.PreviousDiff = session.LastDiff;
        var prevState = session.State;
        var prompt = BuildImplementPrompt(request, session);
        var result = await CallNodeAsync(AgentRole.Coder, TaskType.Refactor, prompt, session, ct);
        if (result is not null) { session.LastDiff = ExtractDiff(result); session.ConsecutiveFailures = 0; session.State = AgentState.Review; session.Iteration++; }
        else { session.ConsecutiveFailures++; if (session.State == prevState) session.State = AgentState.Failed; }
    }

    private async Task ReviewAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] REVIEW", session.Iteration);
        await LogStepAsync(session, AgentStepType.Review, $"Reviewing iteration {session.Iteration}", ct);
        var prevState = session.State;
        var prompt = BuildReviewPrompt(request, session);
        var result = await CallNodeAsync(AgentRole.Reviewer, TaskType.Review, prompt, session, ct);
        if (result is not null) { session.LastReview = result; session.ConsecutiveFailures = 0; session.State = ReviewApproves(result) ? AgentState.Test : AgentState.Implement; }
        else { session.ConsecutiveFailures++; if (session.State == prevState) session.State = AgentState.Failed; }
    }

    private async Task TestAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] TEST", session.Iteration);
        await LogStepAsync(session, AgentStepType.Test, $"Testing iteration {session.Iteration}", ct);
        var prevState = session.State;
        var testCode = await CallNodeAsync(AgentRole.Tester, TaskType.TestGeneration, BuildTestPrompt(request, session), session, ct);
        if (testCode is null) { session.ConsecutiveFailures++; if (session.State == prevState) session.State = AgentState.Failed; return; }
        if (request.WorkingDirectory is not null)
        {
            var sandboxResult = await _sandbox.RunAsync("dotnet test --no-build --verbosity minimal", request.WorkingDirectory, ct);
            _logger.LogInformation("Sandbox test run exited={Code} timedOut={TimedOut}", sandboxResult.ExitCode, sandboxResult.TimedOut);
            if (!sandboxResult.Success) { session.ConsecutiveFailures++; session.LastReview = $"Tests failed:\n{sandboxResult.Output}"; session.State = AgentState.Implement; return; }
        }
        session.ConsecutiveFailures = 0;
        session.State = AgentState.Done;
    }

    private async Task<string?> CallNodeAsync(AgentRole role, TaskType taskType, string prompt, AgentSession session, CancellationToken ct)
    {
        try
        {
            var result = await _routing.RouteAsync(taskType, new InferenceRequest { Prompt = prompt }, ct);
            var step = new AgentStep { Role = role, State = session.State, Prompt = prompt, Response = result.Text, TokensEstimated = TokenEstimator.Estimate(prompt) + TokenEstimator.Estimate(result.Text), Success = true };
            session.RecordStep(step);
            _logger.LogDebug("Agent step {Role} completed -- tokensEst={Tokens} nodeId={Node}", role, step.TokensEstimated, result.NodeId);
            return result.Text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Agent step {Role} failed", role);
            session.RecordStep(new AgentStep { Role = role, State = session.State, Prompt = prompt, Response = string.Empty, TokensEstimated = TokenEstimator.Estimate(prompt), Success = false });
            return null;
        }
    }

    private static string? CheckAbortConditions(AgentSession session)
    {
        if (session.Iteration >= MaxIterations) return $"Max iterations reached ({MaxIterations})";
        if (session.TokensUsed >= MaxTokensPerLoop) return $"Token budget exhausted ({session.TokensUsed} / {MaxTokensPerLoop})";
        if (session.ConsecutiveFailures >= MaxConsecFailures) return $"Repeated failure ({session.ConsecutiveFailures} consecutive)";
        if (session.Iteration > 0 && session.State is AgentState.Review or AgentState.Test && string.IsNullOrWhiteSpace(session.LastDiff)) return "No code diff produced";
        if (session.Iteration > 1 && session.LastDiff == session.PreviousDiff && !string.IsNullOrWhiteSpace(session.LastDiff)) return "No state change -- repeated identical diff";
        return null;
    }

    private static string BuildPlanPrompt(AgentRequest request, AgentSession session) { var sb = new System.Text.StringBuilder(); sb.AppendLine("You are an expert software architect. Decompose the following goal into clear implementation steps."); sb.AppendLine(); sb.AppendLine($"GOAL: {request.Goal}"); if (!string.IsNullOrWhiteSpace(request.Context)) { sb.AppendLine(); sb.AppendLine($"CONTEXT:\n{request.Context}"); } if (session.Steps.Count > 0) { sb.AppendLine(); sb.AppendLine("PREVIOUS REVIEW FEEDBACK:"); sb.AppendLine(session.LastReview); } sb.AppendLine(); sb.AppendLine("Produce a numbered list of steps. Be concise."); return sb.ToString(); }
    private static string BuildImplementPrompt(AgentRequest request, AgentSession session) { var sb = new System.Text.StringBuilder(); sb.AppendLine("You are an expert software engineer. Produce a unified diff implementing the goal."); sb.AppendLine(); sb.AppendLine($"GOAL: {request.Goal}"); if (!string.IsNullOrWhiteSpace(request.Context)) { sb.AppendLine(); sb.AppendLine($"CONTEXT:\n{request.Context}"); } if (!string.IsNullOrWhiteSpace(session.LastReview)) { sb.AppendLine(); sb.AppendLine($"REVIEWER FEEDBACK:\n{session.LastReview}"); } sb.AppendLine(); sb.AppendLine("Output ONLY a valid unified diff. Do not include explanations outside the diff."); return sb.ToString(); }
    private static string BuildReviewPrompt(AgentRequest request, AgentSession session) { var sb = new System.Text.StringBuilder(); sb.AppendLine("You are a senior code reviewer. Review the following diff for correctness, safety, and adherence to the goal."); sb.AppendLine(); sb.AppendLine($"GOAL: {request.Goal}"); sb.AppendLine(); sb.AppendLine($"DIFF:\n{session.LastDiff}"); sb.AppendLine(); sb.AppendLine("If the diff fully achieves the goal and has no issues reply with exactly: APPROVED"); sb.AppendLine("Otherwise describe the specific problems that must be fixed."); return sb.ToString(); }
    private static string BuildTestPrompt(AgentRequest request, AgentSession session) { var sb = new System.Text.StringBuilder(); sb.AppendLine("You are a senior test engineer. Generate xunit tests verifying the following change is correct."); sb.AppendLine(); sb.AppendLine($"GOAL: {request.Goal}"); sb.AppendLine(); sb.AppendLine($"DIFF:\n{session.LastDiff}"); sb.AppendLine(); sb.AppendLine("Output only valid C# xunit test code."); return sb.ToString(); }
    private static string ExtractDiff(string response) { var start = response.IndexOf("```diff", StringComparison.OrdinalIgnoreCase); if (start < 0) start = response.IndexOf("```", StringComparison.Ordinal); if (start >= 0) { var contentStart = response.IndexOf('\n', start) + 1; var end = response.IndexOf("```", contentStart, StringComparison.Ordinal); if (contentStart > 0 && end > contentStart) return response[contentStart..end].Trim(); } if (response.TrimStart().StartsWith("---", StringComparison.Ordinal) || response.TrimStart().StartsWith("diff --git", StringComparison.Ordinal)) return response.Trim(); return response.Trim(); }
    private static bool ReviewApproves(string review) => review.Contains("APPROVED", StringComparison.OrdinalIgnoreCase);
    private static AgentResult BuildResult(AgentSession session, bool success, string? abortReason) => new() { Success = success, FinalState = session.State, Diff = session.LastDiff, Steps = session.Steps.AsReadOnly(), AbortReason = abortReason, TotalIterations = session.Iteration, TotalTokensUsed = session.TokensUsed, Summary = success ? $"Completed in {session.Iteration} iteration(s), {session.TokensUsed} tokens estimated" : $"Failed at {session.State}: {abortReason}" };
}
