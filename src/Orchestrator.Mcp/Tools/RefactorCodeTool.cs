using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;
using Orchestrator.Mcp.Helpers;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class RefactorCodeTool
{
    private readonly IRoutingService  _routing;
    private readonly IIdempotencyCache _idempotency;
    private readonly ILoggingService  _log;
    private readonly ILogger<RefactorCodeTool> _logger;

    /// <summary>
    /// Per-tool inference timeout.
    /// Prevents the MCP caller from hanging when a node is slow/unreachable.
    /// Configurable via appsettings: ToolOptions:RefactorTimeoutSeconds (default 45).
    /// </summary>
    private const int DefaultToolTimeoutSeconds = 45;

    public RefactorCodeTool(
        IRoutingService routing,
        IIdempotencyCache idempotency,
        ILoggingService log,
        ILogger<RefactorCodeTool> logger)
    {
        _routing     = routing;
        _idempotency = idempotency;
        _log         = log;
        _logger      = logger;
    }

    [McpServerTool(Name = "refactor_code"), Description(
        "Refactors code for the requested goal while preserving behaviour. " +
        "Returns the refactored source code in the 'summary' field (markdown stripped).")]
    public Task<string> RefactorCodeAsync(
        [Description("Source code to refactor")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Refactor goal: readability | performance | solid | naming | extract_method | reduce_complexity")] string goal,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(
            _idempotency, idempotencyKey,
            () => ExecuteCoreAsync(code, language, goal, cancellationToken),
            cancellationToken);

    private async Task<string> ExecuteCoreAsync(
        string code, string language, string goal, CancellationToken ct)
    {
        var taskId = Guid.NewGuid().ToString("N");

        // Validate before hitting the network
        var request = new RefactorCodeRequest
        {
            Goal     = goal,
            Codebase = [new CodeFile { Path = $"input.{language}", Content = code }]
        };
        request.ValidateOrThrow(new RefactorCodeRequestValidator());

        var prompt = BuildPrompt(code, language, goal);

        // Log request (fire-and-forget safe — log failures never abort inference)
        try { await _log.LogRequestAsync("refactor_code", request, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

        // Per-tool timeout — avoids 4-minute hangs when NodeB is unreachable.
        // If THIS timeout fires (not the caller's ct), return a structured error.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(DefaultToolTimeoutSeconds));

        try
        {
            var result = await _routing.RouteAsync(
                TaskType.Refactor,
                new InferenceRequest { Prompt = prompt, Stream = true, Priority = QueuePriority.Normal },
                cts.Token);

            // Log raw inference result before cleaning
            try { await _log.LogInferenceAsync(taskId, prompt, result.Text, result.Model, result.NodeId, result.LatencyMs, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

            // --- MAXIMALLY FORGIVING output cleaning ---
            // The model may wrap the refactored code in markdown fences or add
            // preamble/outro prose. Extract the clean code regardless of format.
            var cleanedCode = ResponseCleaner.ExtractCode(result.Text, language);

            var response = new RefactorCodeResponse
            {
                Summary = cleanedCode,   // callers get clean code, not markdown
                Meta    = Meta.FromInferenceResult(taskId, result)
            };

            try { await _log.LogResponseAsync("refactor_code", response, result.LatencyMs, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // OUR timeout fired (not the caller's) — return structured error
            _logger.LogWarning("refactor_code timed out after {Timeout}s for task {TaskId}",
                DefaultToolTimeoutSeconds, taskId);

            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code      = "tool_timeout",
                    message   = $"refactor_code timed out after {DefaultToolTimeoutSeconds}s. " +
                                "Try a smaller input or check node health via get_node_status.",
                    retryable = true
                },
                meta = new { taskId, node = (string?)null }
            }, JsonConfig.Default);
        }
    }

    private static string BuildPrompt(string code, string language, string goal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {language} developer.");
        sb.AppendLine($"Refactor the following code for: {goal}.");
        sb.AppendLine();
        // Output format instruction separated from input section to reduce confusion
        sb.AppendLine("Output format requirements:");
        sb.AppendLine("  - Return ONLY the refactored source code");
        sb.AppendLine("  - Do NOT include explanations, summaries, or change descriptions");
        sb.AppendLine("  - Do NOT wrap your response in code fences or backticks");
        sb.AppendLine("  - Start your response immediately with the first line of code");
        sb.AppendLine();
        sb.AppendLine("Input code to refactor:");
        sb.AppendLine($"```{language}");
        sb.AppendLine(code);
        sb.AppendLine("```");
        return sb.ToString();
    }
}
