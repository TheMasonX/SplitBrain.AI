using System.ComponentModel;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<RefactorCodeTool> _logger;

    public RefactorCodeTool(IRoutingService routing, IIdempotencyCache idempotency, ILogger<RefactorCodeTool> logger)
    {
        _routing = routing;
        _idempotency = idempotency;
        _logger = logger;
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
        var request = new RefactorCodeRequest
        {
            Goal = goal,
            Codebase = [new CodeFile { Path = $"input.{language}", Content = code }]
        };

        request.ValidateOrThrow(new RefactorCodeRequestValidator());

        var prompt = BuildPrompt(code, language, goal);
        var taskId = Guid.NewGuid().ToString("N");

        var result = await _routing.RouteAsync(
            TaskType.Refactor,
            new InferenceRequest { Prompt = prompt, Stream = true, Priority = QueuePriority.Normal },
            cancellationToken);

        _logger.LogDebug("[{ToolName}] Raw response (node={Node}, model={Model}, {Tokens} chars): {Preview}",
            GetType().Name, result.NodeId, result.Model, result.Text.Length,
            result.Text.Length > 500 ? result.Text[..500] + "…" : result.Text);

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

    private static string BuildPrompt(string code, string language, string goal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {language} developer. Refactor the following code for: {goal}.");
        sb.AppendLine("Return ONLY the refactored code with no explanation or markdown fences.");
        sb.AppendLine();
        sb.AppendLine($"```{language}");
        sb.AppendLine(code);
        sb.AppendLine("```");
        return sb.ToString();
    }
}
