using Microsoft.Extensions.Logging;
using Orchestrator.Agents.Models;
using Orchestrator.Agents.Sandbox;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Agents;

/// <summary>
/// Bounded agent loop per §9.
///
/// State machine: INIT → PLAN → IMPLEMENT → REVIEW → TEST → DONE | FAIL
///
/// §9.3 limits
///   • Max iterations:        4
///   • Max tokens per loop:   12 000
///   • Abort if:              no code diff produced
///                            repeated failure (≥2 consecutive)
///                            no state change
///
/// §9.4 role → node mapping
///   Architect  → Node A  (TaskType.Chat)
///   Coder      → Node A  (TaskType.Refactor)
///   Reviewer   → Node B  (TaskType.Review)
///   Tester     → Node B  (TaskType.TestGeneration)
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private const int MaxIterations      = 4;
    private const int MaxTokensPerLoop   = 12_000;
    private const int MaxConsecFailures  = 2;

    /// <summary>Rough estimate: 4 characters ≈ 1 token.</summary>
    private static int EstimateTokens(string text) => text.Length / 4;

    private readonly IRoutingService _routing;
    private readonly ICodeSandbox    _sandbox;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IRoutingService routing,
        ICodeSandbox sandbox,
        ILogger<AgentOrchestrator> logger)
    {
        _routing = routing;
        _sandbox = sandbox;
        _logger  = logger;
    }

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    public async Task<AgentResult> RunAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Agent started — goal: {Goal}", request.Goal);

        var session = new AgentSession();

        // INIT: validate
        session.State = AgentState.Plan;

        while (!cancellationToken.IsCancellationRequested)
        {
            var abortReason = CheckAbortConditions(session);
            if (abortReason is not null)
            {
                _logger.LogWarning("Agent aborting: {Reason}", abortReason);
                return BuildResult(session, success: false, abortReason);
            }

            switch (session.State)
            {
                case AgentState.Plan:
                    await PlanAsync(request, session, cancellationToken);
                    break;

                case AgentState.Implement:
                    await ImplementAsync(request, session, cancellationToken);
                    break;

                case AgentState.Review:
                    await ReviewAsync(request, session, cancellationToken);
                    break;

                case AgentState.Test:
                    await TestAsync(request, session, cancellationToken);
                    break;

                case AgentState.Done:
                    _logger.LogInformation("Agent completed successfully after {Iter} iteration(s)", session.Iteration);
                    return BuildResult(session, success: true, abortReason: null);

                case AgentState.Failed:
                    return BuildResult(session, success: false, "Agent state machine reached Failed");

                default:
                    return BuildResult(session, success: false, $"Unexpected state: {session.State}");
            }
        }

        return BuildResult(session, success: false, "Cancelled");
    }

    // -----------------------------------------------------------------------
    // State handlers
    // -----------------------------------------------------------------------

    private async Task PlanAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] PLAN", session.Iteration);
        var prevState = session.State;

        var prompt = BuildPlanPrompt(request, session);
        var result = await CallNodeAsync(AgentRole.Architect, TaskType.Chat, prompt, session, ct);

        if (result is not null)
        {
            session.State = AgentState.Implement;
        }
        else
        {
            session.ConsecutiveFailures++;
            if (session.State == prevState)
                session.State = AgentState.Failed;
        }
    }

    private async Task ImplementAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] IMPLEMENT", session.Iteration);
        session.PreviousDiff = session.LastDiff;
        var prevState = session.State;

        var prompt = BuildImplementPrompt(request, session);
        var result = await CallNodeAsync(AgentRole.Coder, TaskType.Refactor, prompt, session, ct);

        if (result is not null)
        {
            session.LastDiff = ExtractDiff(result);
            session.ConsecutiveFailures = 0;
            session.State = AgentState.Review;
            session.Iteration++;
        }
        else
        {
            session.ConsecutiveFailures++;
            if (session.State == prevState)
                session.State = AgentState.Failed;
        }
    }

    private async Task ReviewAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] REVIEW", session.Iteration);
        var prevState = session.State;

        var prompt = BuildReviewPrompt(request, session);
        var result = await CallNodeAsync(AgentRole.Reviewer, TaskType.Review, prompt, session, ct);

        if (result is not null)
        {
            session.LastReview = result;
            session.ConsecutiveFailures = 0;

            // If reviewer approves move to test, else loop back to implement
            if (ReviewApproves(result))
                session.State = AgentState.Test;
            else
                session.State = AgentState.Implement;
        }
        else
        {
            session.ConsecutiveFailures++;
            if (session.State == prevState)
                session.State = AgentState.Failed;
        }
    }

    private async Task TestAsync(AgentRequest request, AgentSession session, CancellationToken ct)
    {
        _logger.LogInformation("Agent [{Iter}] TEST", session.Iteration);
        var prevState = session.State;

        // Step 1: generate tests via Node B
        var testPrompt = BuildTestPrompt(request, session);
        var testCode   = await CallNodeAsync(AgentRole.Tester, TaskType.TestGeneration, testPrompt, session, ct);

        if (testCode is null)
        {
            session.ConsecutiveFailures++;
            if (session.State == prevState)
                session.State = AgentState.Failed;
            return;
        }

        // Step 2: run tests in sandbox (only if working directory is set)
        if (request.WorkingDirectory is not null)
        {
            var sandboxResult = await _sandbox.RunAsync(
                "dotnet test --no-build --verbosity minimal",
                request.WorkingDirectory,
                ct);

            _logger.LogInformation(
                "Sandbox test run exited={Code} timedOut={TimedOut}",
                sandboxResult.ExitCode, sandboxResult.TimedOut);

            if (!sandboxResult.Success)
            {
                session.ConsecutiveFailures++;
                // Feed sandbox output back as context and retry from Implement
                session.LastReview = $"Tests failed:\n{sandboxResult.Output}";
                session.State      = AgentState.Implement;
                return;
            }
        }

        session.ConsecutiveFailures = 0;
        session.State = AgentState.Done;
    }

    // -----------------------------------------------------------------------
    // Node invocation
    // -----------------------------------------------------------------------

    private async Task<string?> CallNodeAsync(
        AgentRole role,
        TaskType taskType,
        string prompt,
        AgentSession session,
        CancellationToken ct)
    {
        try
        {
            var inferenceRequest = new InferenceRequest { Prompt = prompt };
            var result = await _routing.RouteAsync(taskType, inferenceRequest, ct);

            var step = new AgentStep
            {
                Role            = role,
                State           = session.State,
                Prompt          = prompt,
                Response        = result.Text,
                TokensEstimated = EstimateTokens(prompt) + EstimateTokens(result.Text),
                Success         = true
            };
            session.RecordStep(step);

            _logger.LogDebug(
                "Agent step {Role} completed — tokensEst={Tokens} nodeId={Node}",
                role, step.TokensEstimated, result.NodeId);

            return result.Text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Agent step {Role} failed", role);

            session.RecordStep(new AgentStep
            {
                Role            = role,
                State           = session.State,
                Prompt          = prompt,
                Response        = string.Empty,
                TokensEstimated = EstimateTokens(prompt),
                Success         = false
            });
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Abort conditions (§9.3)
    // -----------------------------------------------------------------------

    private static string? CheckAbortConditions(AgentSession session)
    {
        if (session.Iteration >= MaxIterations)
            return $"Max iterations reached ({MaxIterations})";

        if (session.TokensUsed >= MaxTokensPerLoop)
            return $"Token budget exhausted ({session.TokensUsed} / {MaxTokensPerLoop})";

        if (session.ConsecutiveFailures >= MaxConsecFailures)
            return $"Repeated failure ({session.ConsecutiveFailures} consecutive)";

        // No diff produced after at least one implement step
        if (session.Iteration > 0
            && session.State is AgentState.Review or AgentState.Test
            && string.IsNullOrWhiteSpace(session.LastDiff))
            return "No code diff produced";

        // Same diff as last iteration — no state change
        if (session.Iteration > 1
            && session.LastDiff == session.PreviousDiff
            && !string.IsNullOrWhiteSpace(session.LastDiff))
            return "No state change — repeated identical diff";

        return null;
    }

    // -----------------------------------------------------------------------
    // Prompt builders
    // -----------------------------------------------------------------------

    private static string BuildPlanPrompt(AgentRequest request, AgentSession session)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are an expert software architect. Decompose the following goal into clear implementation steps.");
        sb.AppendLine();
        sb.AppendLine($"GOAL: {request.Goal}");

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine();
            sb.AppendLine($"CONTEXT:\n{request.Context}");
        }

        if (session.Steps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("PREVIOUS REVIEW FEEDBACK:");
            sb.AppendLine(session.LastReview);
        }

        sb.AppendLine();
        sb.AppendLine("Produce a numbered list of steps. Be concise.");
        return sb.ToString();
    }

    private static string BuildImplementPrompt(AgentRequest request, AgentSession session)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are an expert software engineer. Produce a unified diff implementing the goal.");
        sb.AppendLine();
        sb.AppendLine($"GOAL: {request.Goal}");

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine();
            sb.AppendLine($"CONTEXT:\n{request.Context}");
        }

        if (!string.IsNullOrWhiteSpace(session.LastReview))
        {
            sb.AppendLine();
            sb.AppendLine($"REVIEWER FEEDBACK:\n{session.LastReview}");
        }

        sb.AppendLine();
        sb.AppendLine("Output ONLY a valid unified diff. Do not include explanations outside the diff.");
        return sb.ToString();
    }

    private static string BuildReviewPrompt(AgentRequest request, AgentSession session)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a senior code reviewer. Review the following diff for correctness, safety, and adherence to the goal.");
        sb.AppendLine();
        sb.AppendLine($"GOAL: {request.Goal}");
        sb.AppendLine();
        sb.AppendLine($"DIFF:\n{session.LastDiff}");
        sb.AppendLine();
        sb.AppendLine("If the diff fully achieves the goal and has no issues reply with exactly: APPROVED");
        sb.AppendLine("Otherwise describe the specific problems that must be fixed.");
        return sb.ToString();
    }

    private static string BuildTestPrompt(AgentRequest request, AgentSession session)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a senior test engineer. Generate xunit tests verifying the following change is correct.");
        sb.AppendLine();
        sb.AppendLine($"GOAL: {request.Goal}");
        sb.AppendLine();
        sb.AppendLine($"DIFF:\n{session.LastDiff}");
        sb.AppendLine();
        sb.AppendLine("Output only valid C# xunit test code.");
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts a unified diff block from a model response.
    /// If the response already looks like a raw diff, returns it as-is.
    /// </summary>
    private static string ExtractDiff(string response)
    {
        // Look for a fenced ```diff or ``` block
        var start = response.IndexOf("```diff", StringComparison.OrdinalIgnoreCase);
        if (start < 0) start = response.IndexOf("```", StringComparison.Ordinal);
        if (start >= 0)
        {
            var contentStart = response.IndexOf('\n', start) + 1;
            var end          = response.IndexOf("```", contentStart, StringComparison.Ordinal);
            if (contentStart > 0 && end > contentStart)
                return response[contentStart..end].Trim();
        }

        // If response starts with a diff header treat the whole thing as the diff
        if (response.TrimStart().StartsWith("---", StringComparison.Ordinal)
            || response.TrimStart().StartsWith("diff --git", StringComparison.Ordinal))
            return response.Trim();

        return response.Trim();
    }

    /// <summary>Returns true when the reviewer's response indicates approval.</summary>
    private static bool ReviewApproves(string review) =>
        review.Contains("APPROVED", StringComparison.OrdinalIgnoreCase);

    private static AgentResult BuildResult(AgentSession session, bool success, string? abortReason) =>
        new()
        {
            Success          = success,
            FinalState       = session.State,
            Diff             = session.LastDiff,
            Steps            = session.Steps.AsReadOnly(),
            AbortReason      = abortReason,
            TotalIterations  = session.Iteration,
            TotalTokensUsed  = session.TokensUsed,
            Summary          = success
                ? $"Completed in {session.Iteration} iteration(s), {session.TokensUsed} tokens estimated"
                : $"Failed at {session.State}: {abortReason}"
        };
}
