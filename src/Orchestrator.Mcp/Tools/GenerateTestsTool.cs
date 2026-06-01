using System.ComponentModel;
using System.Text;
using System.Text.Json;
using FluentValidation;
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
        try
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
                Meta = Meta.FromInferenceResult(taskId, result)
            };

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (ValidationException vex)
        {
            return JsonSerializer.Serialize(new { error = new { code = "validation_error", message = vex.Message, retryable = false } }, JsonConfig.Default);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = new { code = "internal_error", message = ex.Message, retryable = true } }, JsonConfig.Default);
        }
    }

    /// <summary>
    /// Sanitize user-supplied code to prevent markdown fence escape (prompt injection).
    /// Breaks any triple-backtick sequences so they cannot close the code fence.
    /// </summary>
    private static string SanitizeForFence(string input)
        => input.Replace("```", "` ` `");

    private static string BuildPrompt(string code, string language, string framework, string coverage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {language} developer specialising in test-driven development.");
        sb.AppendLine($"Generate {coverage} unit tests using {framework} for the following code.");
        sb.AppendLine("Return ONLY the test file content with no explanation or extra markdown.");
        sb.AppendLine();
        sb.AppendLine($"```{language}");
        sb.AppendLine(SanitizeForFence(code));
        sb.AppendLine("```");
        return sb.ToString();
    }
}
