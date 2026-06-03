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
using Orchestrator.Core.Utilities;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateTestsTool
{
    private readonly IRoutingService _routing;
    private readonly IIdempotencyCache _idempotency;
    private readonly ILogger<GenerateTestsTool> _logger;

    public GenerateTestsTool(IRoutingService routing, IIdempotencyCache idempotency, ILogger<GenerateTestsTool> logger)
    {
        _routing = routing;
        _idempotency = idempotency;
        _logger = logger;
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

            _logger.LogDebug("[{ToolName}] Raw response (node={Node}, model={Model}, {Tokens} chars): {Preview}",
                GetType().Name, result.NodeId, result.Model, result.Text.Length,
                result.Text.Length > 500 ? result.Text[..500] + "…" : result.Text);

            // Strip any markdown code fences the model may have wrapped around the test file content
            // even though the prompt forbids them. ResponseParser is a no-op if no fences are present.
            var cleaned = ResponseParser.StripFences(result.Text);

            var response = new GenerateTestsResponse
            {
                Files = new List<GeneratedTestFile>
                {
                    new GeneratedTestFile
                    {
                        Path = $"Tests.{language}",
                        Content = cleaned
                    }
                },
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

    /// <summary>
    /// Strip control characters (including newlines) from a short metadata parameter and
    /// truncate to <paramref name="maxLength"/> to prevent prompt-injection via
    /// language/framework/coverage-style fields.
    /// </summary>
    private static string SanitizeParam(string value, int maxLength = 50)
    {
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }

    private static string BuildPrompt(string code, string language, string framework, string coverage)
    {
        var safeLanguage = SanitizeParam(language);
        var safeFramework = SanitizeParam(framework);
        var safeCoverage = SanitizeParam(coverage);

        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {safeLanguage} developer specialising in test-driven development.");
        sb.AppendLine($"Generate {safeCoverage} unit tests using {safeFramework} for the following code.");
        sb.AppendLine("Return ONLY the test file content with no explanation or extra markdown.");
        sb.AppendLine();
        sb.AppendLine($"```{safeLanguage}");
        sb.AppendLine(SanitizeForFence(code));
        sb.AppendLine("```");
        return sb.ToString();
    }
}
