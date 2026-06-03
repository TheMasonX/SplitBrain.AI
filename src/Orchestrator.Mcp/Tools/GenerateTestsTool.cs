using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.Helpers;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateTestsTool
{
    private readonly IRoutingService  _routing;
    private readonly IIdempotencyCache _idempotency;
    private readonly ILoggingService  _log;
    private readonly ILogger<GenerateTestsTool> _logger;

    private const int DefaultToolTimeoutSeconds = 60;   // test gen can be slow

    public GenerateTestsTool(
        IRoutingService routing,
        IIdempotencyCache idempotency,
        ILoggingService log,
        ILogger<GenerateTestsTool> logger)
    {
        _routing     = routing;
        _idempotency = idempotency;
        _log         = log;
        _logger      = logger;
    }

    [McpServerTool(Name = "generate_tests"), Description(
        "Generates unit tests for the provided source code. " +
        "Returns test file content in Files[0].Content (markdown stripped).")]
    public Task<string> GenerateTestsAsync(
        [Description("Source code to generate tests for")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Test framework (e.g. xunit, nunit, pytest, jest)")] string framework,
        [Description("Coverage focus: happy_path | edge_cases | error_handling | all")] string coverage = "all",
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(
            _idempotency, idempotencyKey,
            () => ExecuteCoreAsync(code, language, framework, coverage, cancellationToken),
            cancellationToken);

    private async Task<string> ExecuteCoreAsync(
        string code, string language, string framework, string coverage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("code is required.");
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("language is required.");

        var taskId = Guid.NewGuid().ToString("N");
        var prompt = BuildPrompt(code, language, framework, coverage);

        try { await _log.LogRequestAsync("generate_tests", new { code, language, framework, coverage }, ct); } catch { }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(DefaultToolTimeoutSeconds));

        try
        {
            var result = await _routing.RouteAsync(
                TaskType.TestGeneration,
                new InferenceRequest { Prompt = prompt, Stream = true, Priority = QueuePriority.Normal },
                cts.Token);

            try { await _log.LogInferenceAsync(taskId, prompt, result.Text, result.Model, result.NodeId, result.LatencyMs, ct); } catch { }

            // Clean the model output — strip markdown fences, preamble, and outro
            var cleanedTests = ResponseCleaner.ExtractCode(result.Text, language);

            // Build a sensible output filename based on the primary class in the source
            var testFileName = BuildTestFileName(code, language, framework);

            var response = new GenerateTestsResponse
            {
                Files = new List<GeneratedTestFile>
                {
                    new GeneratedTestFile
                    {
                        Path    = testFileName,
                        Content = cleanedTests
                    }
                },
                Meta = new Meta
                {
                    TaskId    = taskId,
                    Node      = result.NodeId,
                    Model     = result.Model,
                    LatencyMs = result.LatencyMs,
                    TokensIn  = result.TokensIn,
                    TokensOut = result.TokensOut
                }
            };

            try { await _log.LogResponseAsync("generate_tests", response, result.LatencyMs, ct); } catch { }

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("generate_tests timed out after {Timeout}s for task {TaskId}",
                DefaultToolTimeoutSeconds, taskId);

            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code      = "tool_timeout",
                    message   = $"generate_tests timed out after {DefaultToolTimeoutSeconds}s. " +
                                "Try a smaller input or check node health.",
                    retryable = true
                },
                meta = new { taskId }
            }, JsonConfig.Default);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a sensible test file name from the source code's primary class
    /// and the target language's conventional test file extension.
    /// Falls back to "GeneratedTests.{ext}" if no class is detected.
    /// </summary>
    private static string BuildTestFileName(string code, string language, string framework)
    {
        var className = ExtractPrimaryClassName(code) ?? "Generated";
        var ext = GetTestExtension(language, framework);
        return $"{className}Tests.{ext}";
    }

    private static string? ExtractPrimaryClassName(string code)
    {
        // Match: class Foo, public class Foo, struct Foo, interface IFoo
        var m = Regex.Match(code,
            @"\b(?:class|struct|interface)\s+([A-Z][A-Za-z0-9_]*)",
            RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string GetTestExtension(string language, string framework)
        => language.ToLowerInvariant() switch
        {
            "csharp" or "cs" or "c#"          => "cs",
            "python" or "py"                   => "py",
            "typescript" or "ts"               => "ts",
            "javascript" or "js"               => "js",
            "java"                             => "java",
            "rust" or "rs"                     => "rs",
            "go"                               => "go",
            "kotlin" or "kt"                   => "kt",
            "swift"                            => "swift",
            "ruby" or "rb"                     => "rb",
            _                                  => language.ToLowerInvariant()
        };

    private static string SanitizeForFence(string input)
        => input.Replace("```", "` ` `");

    private static string SanitizeParam(string value, int maxLength = 50)
    {
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }

    private static string BuildPrompt(string code, string language, string framework, string coverage)
    {
        var safeLanguage  = SanitizeParam(language);
        var safeFramework = SanitizeParam(framework);
        var safeCoverage  = SanitizeParam(coverage);

        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert {safeLanguage} developer specialising in test-driven development.");
        sb.AppendLine($"Generate {safeCoverage} unit tests using {safeFramework} for the following code.");
        sb.AppendLine();
        sb.AppendLine("Output format requirements:");
        sb.AppendLine("  - Return ONLY the test file source code");
        sb.AppendLine("  - Do NOT include explanations, descriptions, or commentary");
        sb.AppendLine("  - Do NOT wrap your response in code fences or backticks");
        sb.AppendLine("  - Start your response immediately with the first import or using statement");
        sb.AppendLine();
        sb.AppendLine("Source code to test:");
        sb.AppendLine($"```{safeLanguage}");
        sb.AppendLine(SanitizeForFence(code));
        sb.AppendLine("```");
        return sb.ToString();
    }
}
