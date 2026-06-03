using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Agents.SemanticKernel;

/// <summary>
/// Builds a Semantic Kernel instance backed by SplitBrain inference nodes and
/// provides single-shot chat and a simple sequential planner.
///
/// Planning strategy (lightweight, no external planner package required):
///   1. Send the goal to the Architect node (Node A / Chat) — ask it to emit a
///      numbered step list.
///   2. Parse the numbered list into discrete steps.
///   3. Execute each step through the Coder node (Node A / Refactor), passing
///      prior step output as context.
///   4. Return aggregated output + per-step metadata.
/// </summary>
public sealed class KernelPlannerService : IKernelPlannerService
{
    private static readonly Regex StepPattern = new(@"^\s*\d+[\.\)]\s+(.+)$", RegexOptions.Multiline);
    private const int MaxPlanSteps = 10;
    private const string PlannerSystemPrompt =
        "You are a planning assistant. Break the user's goal into a concise numbered list of steps (max 10). " +
        "Each step must be on its own line in the format: '1. <action>'. No extra commentary.";

    private readonly Kernel _kernel;
    private readonly IChatCompletionService _planner;   // Node A / Chat
    private readonly IChatCompletionService _executor;  // Node A / Refactor
    private readonly ILogger<KernelPlannerService> _logger;

    public KernelPlannerService(IRoutingService routing, ILogger<KernelPlannerService> logger)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // Build two SK completion services backed by the routing layer
        _planner  = new RoutingChatCompletionService(routing, TaskType.Chat,    "planner");
        _executor = new RoutingChatCompletionService(routing, TaskType.Refactor, "executor");

        _kernel = Kernel.CreateBuilder()
            .Build();
    }

    /// <inheritdoc/>
    public async Task<string> InvokeAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        _logger.LogDebug("KernelPlannerService.InvokeAsync — promptLen={Len}", prompt.Length);

        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        var results = await _planner.GetChatMessageContentsAsync(
            history, kernel: _kernel, cancellationToken: cancellationToken);

        return results.FirstOrDefault()?.Content ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<KernelPlanResult> PlanAndExecuteAsync(
        string goal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        _logger.LogInformation("PlanAndExecuteAsync — goal: {Goal}", goal);

        // ── Step 1: generate a plan ──────────────────────────────────────────
        string planText;
        try
        {
            var planHistory = new ChatHistory(PlannerSystemPrompt);
            planHistory.AddUserMessage(goal);

            var planResult = await _planner.GetChatMessageContentsAsync(
                planHistory, kernel: _kernel, cancellationToken: cancellationToken);

            planText = planResult.FirstOrDefault()?.Content ?? string.Empty;
            _logger.LogDebug("Plan generated: {Plan}", planText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Planning phase failed");
            return new KernelPlanResult
            {
                Goal = goal,
                Steps = [],
                FinalOutput = string.Empty,
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        // ── Step 2: parse numbered steps ────────────────────────────────────
        var stepDescriptions = StepPattern.Matches(planText)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(MaxPlanSteps)
            .ToList();

        if (stepDescriptions.Count == 0)
        {
            // Plan returned no parseable steps — treat the whole plan as one step
            stepDescriptions.Add(goal);
        }

        _logger.LogInformation("Executing {Count} plan step(s)", stepDescriptions.Count);

        // ── Step 3: execute each step ────────────────────────────────────────
        var executedSteps = new List<KernelPlanStep>(stepDescriptions.Count);
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"Goal: {goal}");
        contextBuilder.AppendLine();

        foreach (var (description, index) in stepDescriptions.Select((d, i) => (d, i + 1)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            string stepOutput;
            try
            {
                var stepPrompt = new StringBuilder();
                stepPrompt.AppendLine("You are executing step of a multi-step plan.");
                stepPrompt.AppendLine($"Overall goal: {goal}");
                if (contextBuilder.Length > 0)
                {
                    stepPrompt.AppendLine("Previous steps summary:");
                    stepPrompt.Append(contextBuilder);
                }
                stepPrompt.AppendLine($"Current step ({index}/{stepDescriptions.Count}): {description}");
                stepPrompt.AppendLine("Provide a concise, actionable output for this step only.");

                var execHistory = new ChatHistory();
                execHistory.AddUserMessage(stepPrompt.ToString());

                var execResult = await _executor.GetChatMessageContentsAsync(
                    execHistory, kernel: _kernel, cancellationToken: cancellationToken);

                stepOutput = execResult.FirstOrDefault()?.Content ?? string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Step {Index} failed: {Description}", index, description);
                stepOutput = $"[Step failed: {ex.Message}]";
            }

            sw.Stop();
            executedSteps.Add(new KernelPlanStep
            {
                Index = index,
                Description = description,
                Output = stepOutput,
                LatencyMs = (int)sw.ElapsedMilliseconds
            });

            contextBuilder.AppendLine($"Step {index} ({description}): {stepOutput[..Math.Min(200, stepOutput.Length)]}");
            _logger.LogDebug("Step {Index} done in {Ms}ms", index, sw.ElapsedMilliseconds);
        }

        // ── Step 4: aggregate ────────────────────────────────────────────────
        var finalOutput = executedSteps.Count == 1
            ? executedSteps[0].Output
            : string.Join("\n\n", executedSteps.Select(s => $"## Step {s.Index}: {s.Description}\n{s.Output}"));

        var totalTokens = finalOutput.Length / 4 + planText.Length / 4; // rough estimate

        _logger.LogInformation("PlanAndExecuteAsync complete — {Steps} steps, ~{Tokens} tokens",
            executedSteps.Count, totalTokens);

        return new KernelPlanResult
        {
            Goal = goal,
            Steps = executedSteps,
            FinalOutput = finalOutput,
            Success = true,
            TotalTokensEstimate = totalTokens
        };
    }

    // -------------------------------------------------------------------------
    // Inner adapter — routes SK chat calls through IRoutingService
    // -------------------------------------------------------------------------

    private sealed class RoutingChatCompletionService : IChatCompletionService
    {
        private readonly IRoutingService _routing;
        private readonly TaskType _taskType;

        public IReadOnlyDictionary<string, object?> Attributes { get; }

        public RoutingChatCompletionService(IRoutingService routing, TaskType taskType, string name)
        {
            _routing  = routing;
            _taskType = taskType;
            Attributes = new Dictionary<string, object?> { ["name"] = name, ["taskType"] = taskType.ToString() };
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var prompt = BuildPrompt(chatHistory);
            var result = await _routing.RouteAsync(
                _taskType,
                new Orchestrator.Core.Models.InferenceRequest { Prompt = prompt },
                cancellationToken);

            return [new ChatMessageContent(AuthorRole.Assistant, result.Text)];
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
            foreach (var msg in results)
                yield return new StreamingChatMessageContent(msg.Role, msg.Content);
        }

        private static string BuildPrompt(ChatHistory history)
        {
            var sb = new StringBuilder();
            foreach (var msg in history)
            {
                var role = msg.Role == AuthorRole.System    ? "SYSTEM"
                         : msg.Role == AuthorRole.User      ? "USER"
                         : msg.Role == AuthorRole.Assistant ? "ASSISTANT"
                         : msg.Role.Label.ToUpperInvariant();
                sb.AppendLine($"[{role}]");
                sb.AppendLine(msg.Content);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }
}
