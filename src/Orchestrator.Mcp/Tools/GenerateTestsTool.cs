using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateTestsTool
{
    private readonly IRoutingService _routing;
    private readonly IIdempotencyCache _idempotency;

    public GenerateTestsTool(IRoutingService routing, IIdempotencyCache idempotency)
    {
        _routing = routing;
        _idempotency = idempotency;
    }

    [McpServerTool(Name = "generate_tests"), Description("Generates unit tests for the provided source code.")]
    public Task<string> GenerateTestsAsync(
        [Description("Source code to generate tests for")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Test framework (e.g. xunit, nunit, pytest, jest)")] string framework,
        [Description("Coverage focus: happy_path | edge_cases | error_handling | all")] string coverage = "all",
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(code, language, framework, coverage, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string code, string language, string framework, string coverage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("code is required.");
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("language is required.");

        var prompt = BuildPrompt(code, language, framework, coverage);
        var taskId = Guid.NewGuid().ToString("N");

        var result = await _routing.RouteAsync(
            TaskType.TestGeneration,
            new InferenceRequest { Prompt = prompt, Stream = true, Priority = QueuePriority.Normal },
            cancellationToken);

        var response = new GenerateTestsResponse
        {
            Files =
            [
                new GeneratedTestFile
                {
                    Path = $"Tests.{language}",
                    Content = result.Text
                }
            ],
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

    private static string BuildPrompt(string code, string language, string framework, string coverage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {language} developer specialising in test-driven development.");
        sb.AppendLine($"Generate {coverage} unit tests using {framework} for the following code.");
        sb.AppendLine("Return ONLY the test file content with no explanation or extra markdown.");
        sb.AppendLine();
        sb.AppendLine($"```{language}");
        sb.AppendLine(code);
        sb.AppendLine("```");
        return sb.ToString();
    }
}
