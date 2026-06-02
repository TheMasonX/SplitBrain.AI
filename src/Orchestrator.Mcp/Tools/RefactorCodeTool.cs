using System.ComponentModel;
using System.Text;
using System.Text.Json;
using FluentValidation;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class RefactorCodeTool
{
    private readonly IRoutingService _routing;
    private readonly IIdempotencyCache _idempotency;

    public RefactorCodeTool(IRoutingService routing, IIdempotencyCache idempotency)
    {
        _routing = routing;
        _idempotency = idempotency;
    }

    [McpServerTool(Name = "refactor_code"), Description("Refactors code for the requested goal while preserving behaviour.")]
    public Task<string> RefactorCodeAsync(
        [Description("Source code to refactor")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Refactor goal: readability | performance | solid | naming | extract_method | reduce_complexity")] string goal,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(code, language, goal, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string code, string language, string goal, CancellationToken cancellationToken)
    {
        try
        {
            var safeLanguage = SanitizeParam(language);
            var safeGoal = SanitizeParam(goal);

            var request = new RefactorCodeRequest
            {
                Goal = safeGoal,
                Codebase = [new CodeFile { Path = $"input.{safeLanguage}", Content = code }]
            };

            request.ValidateOrThrow(new RefactorCodeRequestValidator());

            var prompt = BuildPrompt(code, safeLanguage, safeGoal);
            var taskId = Guid.NewGuid().ToString("N");

            var result = await _routing.RouteAsync(
                TaskType.Refactor,
                new InferenceRequest { Prompt = prompt, Stream = true, Priority = QueuePriority.Normal },
                cancellationToken);

            var response = new RefactorCodeResponse
            {
                Summary = result.Text,
                Meta = new Meta
                {
                    TaskId = taskId,
                    Node = result.NodeId,
                    Model = result.Model,
                    LatencyMs = result.LatencyMs,
                    TokensIn = result.TokensIn,
                    TokensOut = result.TokensOut
                }
            };

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = new { code = "internal_error", message = ex.Message, retryable = true } }, JsonConfig.Default);
        }
    }

    private static string BuildPrompt(string code, string language, string goal)
    {
        var safeCode = SanitizeForFence(code);
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {language} developer. Refactor the following code for: {goal}.");
        sb.AppendLine("Return ONLY the refactored code with no explanation or markdown fences.");
        sb.AppendLine();
        sb.AppendLine($"```{language}");
        sb.AppendLine(safeCode);
        sb.AppendLine("```");
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------
    // Sanitization helpers — strip control chars and limit length on free-form
    // string params before they reach the prompt or validators.
    // ---------------------------------------------------------------------------

    private static string SanitizeParam(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch))
                sb.Append(ch);
        }
        var cleaned = sb.ToString().Trim();
        return cleaned.Length > 256 ? cleaned[..256] : cleaned;
    }

    private static string SanitizeForFence(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // Neutralise triple-backtick sequences so user input cannot break out of the markdown fence.
        return value.Replace("```", "''`");
    }
}
