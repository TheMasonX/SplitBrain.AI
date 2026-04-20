using System.ComponentModel;
using System.Text.Json;
using FluentValidation;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class ReviewCodeTool
{
    private readonly IRoutingService _routing;

    public ReviewCodeTool(IRoutingService routing)
    {
        _routing = routing;
    }

    [McpServerTool(Name = "review_code"), Description("Reviews code for architecture, performance, bugs, readability, or security issues.")]
    public async Task<string> ReviewCodeAsync(
        [Description("Source code to review")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Review focus: architecture | performance | bugs | readability | security")] string focus,
        CancellationToken cancellationToken = default)
    {
        var request = new ReviewCodeRequest
        {
            Code = code,
            Language = language,
            Focus = focus
        };

        request.ValidateOrThrow(new ReviewCodeRequestValidator());

        var prompt = BuildPrompt(request);

        var inferenceRequest = new InferenceRequest
        {
            Prompt = prompt,
            Model = "qwen2.5-coder:7b-instruct-q4_K_M",
            Stream = true
        };

        var result = await _routing.RouteAsync(TaskType.Review, inferenceRequest, cancellationToken);

        var response = new ReviewCodeResponse
        {
            Summary = result.Text,
            Issues = [],
            Meta = new Meta
            {
                TaskId = Guid.NewGuid().ToString(),
                Node = result.NodeId,
                Model = result.Model,
                LatencyMs = result.LatencyMs,
                TokensIn = result.TokensIn,
                TokensOut = result.TokensOut
            }
        };

        return JsonSerializer.Serialize(response, JsonConfig.Default);
    }

    private static string BuildPrompt(ReviewCodeRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are an expert {request.Language} code reviewer. Perform a {request.Focus} review of the following code.");
        sb.AppendLine("Provide a concise summary of your findings.");
        sb.AppendLine();
        sb.AppendLine($"```{request.Language}");
        sb.AppendLine(request.Code);
        sb.AppendLine("```");

        if (request.Context?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Related files for context:");
            foreach (var file in request.Context)
            {
                sb.AppendLine($"// {file.Path}");
                sb.AppendLine(file.Content);
            }
        }

        return sb.ToString();
    }
}
