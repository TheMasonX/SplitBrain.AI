using System.ComponentModel;
using System.Diagnostics;
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
public sealed class ReviewCodeTool
{
    private readonly IRoutingService _routing;
    private readonly ILoggingService _loggingService;
    private readonly IIdempotencyCache _idempotency;

    public ReviewCodeTool(IRoutingService routing, ILoggingService loggingService, IIdempotencyCache idempotency)
    {
        _routing = routing;
        _loggingService = loggingService;
        _idempotency = idempotency;
    }

    [McpServerTool(Name = "review_code"), Description("Reviews code for architecture, performance, bugs, readability, or security issues.")]
    public Task<string> ReviewCodeAsync(
        [Description("Source code to review")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Review focus: architecture | performance | bugs | readability | security")] string focus,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(code, language, focus, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string code, string language, string focus, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var taskId = Guid.NewGuid().ToString();

        var request = new ReviewCodeRequest
        {
            Code = code,
            Language = language,
            Focus = focus
        };

        try
        {
            // Log incoming request
            await _loggingService.LogRequestAsync("review_code", request, cancellationToken);

            request.ValidateOrThrow(new ReviewCodeRequestValidator());

            var prompt = BuildPrompt(request);

            var inferenceRequest = new InferenceRequest
            {
                Prompt = prompt,
                Stream = true
            };

            var result = await _routing.RouteAsync(TaskType.Review, inferenceRequest, cancellationToken);

            // Log inference details
            await _loggingService.LogInferenceAsync(
                taskId,
                prompt,
                result.Text,
                result.Model,
                result.NodeId,
                result.LatencyMs,
                cancellationToken);

            var response = new ReviewCodeResponse
            {
                Summary = result.Text,
                Issues = [],
                Meta = Meta.FromInferenceResult(taskId, result)
            };

            stopwatch.Stop();

            // Log response
            await _loggingService.LogResponseAsync("review_code", response, stopwatch.ElapsedMilliseconds, cancellationToken);

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await _loggingService.LogErrorAsync("review_code", ex, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Sanitize user-supplied code to prevent markdown fence escape (prompt injection).
    /// Breaks any triple-backtick sequences so they cannot close the code fence.
    /// </summary>
    private static string SanitizeForFence(string input)
        => input.Replace("```", "` ` `");

    private static string BuildPrompt(ReviewCodeRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are an expert {request.Language} code reviewer. Perform a {request.Focus} review of the following code.");
        sb.AppendLine("Provide a concise summary of your findings.");
        sb.AppendLine();
        sb.AppendLine($"```{request.Language}");
        sb.AppendLine(SanitizeForFence(request.Code));
        sb.AppendLine("```");

        if (request.Context?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Related files for context:");
            foreach (var file in request.Context)
            {
                sb.AppendLine($"// {file.Path}");
                sb.AppendLine(SanitizeForFence(file.Content));
            }
        }

        return sb.ToString();
    }
}
