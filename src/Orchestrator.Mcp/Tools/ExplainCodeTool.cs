using System.ComponentModel;
using System.Text.Json;
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
public sealed class ExplainCodeTool
{
    private readonly IRoutingService  _routing;
    private readonly IIdempotencyCache _idempotency;
    private readonly ILoggingService  _log;
    private readonly ILogger<ExplainCodeTool> _logger;

    /// <summary>Routes to the fast node — caller expects low latency.</summary>
    private const int DefaultToolTimeoutSeconds = 45;

    public ExplainCodeTool(
        IRoutingService routing,
        IIdempotencyCache idempotency,
        ILoggingService log,
        ILogger<ExplainCodeTool> logger)
    {
        _routing     = routing;
        _idempotency = idempotency;
        _log         = log;
        _logger      = logger;
    }

    [McpServerTool(Name = "splitbrain_explain_code"), Description(
        "Explains what a piece of code does — purpose, behaviour, and key logic — " +
        "without evaluating quality. Distinct from review_code (which finds problems). " +
        "Routes to the fast node for low latency. Read-only.")]
    public Task<string> ExplainCodeAsync(
        [Description("Source code to explain")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Detail level: brief | detailed | eli5 (default: detailed)")] string verbosity = "detailed",
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(
            _idempotency, idempotencyKey,
            () => ExecuteCoreAsync(code, language, verbosity, cancellationToken),
            cancellationToken);

    private async Task<string> ExecuteCoreAsync(
        string code, string language, string verbosity, CancellationToken ct)
    {
        var taskId = Guid.NewGuid().ToString("N");

        // Sanitise short metadata params against prompt injection
        var safeLanguage  = new string(language.Where(c => !char.IsControl(c)).ToArray())[..Math.Min(50, language.Length)];
        var safeVerbosity = verbosity is "brief" or "detailed" or "eli5" ? verbosity : "detailed";

        var prompt = BuildPrompt(code, safeLanguage, safeVerbosity);

        try { await _log.LogRequestAsync("splitbrain_explain_code", new { code, language, verbosity }, ct); } catch (Exception) { /* intentionally silent */ }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(DefaultToolTimeoutSeconds));

        try
        {
            var result = await _routing.RouteAsync(
                TaskType.Chat,
                new InferenceRequest { Prompt = prompt, Stream = false, Priority = QueuePriority.High },
                cts.Token);

            try { await _log.LogInferenceAsync(taskId, prompt, result.Text, result.Model, result.NodeId, result.LatencyMs, ct); } catch (Exception) { /* intentionally silent */ }

            // Strip fences if the model wraps the explanation in a code block
            var explanation = ResponseCleaner.StripFences(result.Text);

            return JsonSerializer.Serialize(new
            {
                explanation,
                meta = Meta.FromInferenceResult(taskId, result)
            }, JsonConfig.Default);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("splitbrain_explain_code timed out after {Timeout}s for task {TaskId}",
                DefaultToolTimeoutSeconds, taskId);

            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code      = "tool_timeout",
                    message   = $"explain_code timed out after {DefaultToolTimeoutSeconds}s.",
                    retryable = true
                }
            }, JsonConfig.Default);
        }
    }

    private static string BuildPrompt(string code, string language, string verbosity)
    {
        var instruction = verbosity switch
        {
            "brief"  => "Explain this code in 2–3 sentences. What does it do and why?",
            "eli5"   => "Explain this code simply, as if I'm new to programming. Use analogies.",
            _        => "Explain clearly: the purpose, how it works step by step, and any important edge cases.",
        };

        return $"{instruction}\n\n```{language}\n{code}\n```";
    }
}
