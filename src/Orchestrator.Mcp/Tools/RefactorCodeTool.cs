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

    public RefactorCodeTool(IRoutingService routing, IIdempotencyCache idempotency) { _routing = routing; _idempotency = idempotency; }

    [McpServerTool(Name = "refactor_code"), Description("Refactors code for the requested goal while preserving behaviour.")]
    public Task<string> RefactorCodeAsync(
        [Description("Source code to refactor")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Refactor goal: readability | performance | solid | naming | extract_method | reduce_complexity")] string goal,
        [Description("(Optional) Idempotency key")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(code, language, goal, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string code, string language, string goal, CancellationToken cancellationToken)
    {
        try
        {
            var safeLanguage = SanitizeParam(language);
            var safeGoal = SanitizeParam(goal);
            var request = new RefactorCodeRequest { Goal = safeGoal, Codebase = [new CodeFile { Path = $"input.{safeLanguage}", Content = code }] };
            request.ValidateOrThrow(new RefactorCodeRequestValidator());
            var prompt = BuildPrompt(code, safeLanguage, safeGoal);
            var taskId = Guid.NewGuid().ToString("N");
            var result = await _routing.RouteAsync(TaskType.Refactor, new InferenceRequest { Prompt = prompt, Stream = true, Priority = QueuePriority.Normal }, cancellationToken);
            var response = new RefactorCodeResponse { Summary = result.Text, Meta = Meta.FromInferenceResult(taskId, result) };
            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (ValidationException vex) { return JsonSerializer.Serialize(new { error = new { code = "validation_error", message = vex.Message, retryable = false } }, JsonConfig.Default); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return JsonSerializer.Serialize(new { error = new { code = "internal_error", message = ex.Message, retryable = true } }, JsonConfig.Default); }
    }

    private static string BuildPrompt(string code, string language, string goal)
    {
        var safeCode = SanitizeForFence(code);
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {language} developer. Refactor the following code for: {goal}.");
        sb.AppendLine("Return ONLY the refactored code with no explanation or markdown fences.");
        sb.AppendLine(); sb.AppendLine($"```{language}"); sb.AppendLine(safeCode); sb.AppendLine("```");
        return sb.ToString();
    }

    private static string SanitizeParam(string value) { if (string.IsNullOrEmpty(value)) return string.Empty; var sb = new StringBuilder(value.Length); foreach (var ch in value) if (ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch)) sb.Append(ch); var c = sb.ToString().Trim(); return c.Length > 256 ? c[..256] : c; }
    private static string SanitizeForFence(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("```", "''`");
}
