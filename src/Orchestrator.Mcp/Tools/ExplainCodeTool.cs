using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class ExplainCodeTool
{
    private readonly IRoutingService _routing;
    private readonly IIdempotencyCache _idempotency;

    public ExplainCodeTool(IRoutingService routing, IIdempotencyCache idempotency)
    {
        _routing = routing;
        _idempotency = idempotency;
    }

    [McpServerTool(Name = "splitbrain_explain_code"),
     Description("Explains what a piece of code does — its purpose, behavior, and key logic — without evaluating quality. Distinct from review_code which looks for problems. Use when you need to understand unfamiliar code quickly. Routes to fast node for low latency. Read-only.")]
    public Task<string> ExplainCodeAsync(
        [Description("Source code to explain")] string code,
        [Description("Programming language (e.g. csharp, python, typescript)")] string language,
        [Description("Level of detail: brief | detailed | eli5")] string verbosity = "detailed",
        [Description("Optional idempotency key — returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(code, language, verbosity, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string code, string language, string verbosity, CancellationToken ct)
    {
        var safeLanguage = new string(language.Where(c => !char.IsControl(c)).ToArray())[..Math.Min(50, language.Length)];
        var safeVerbosity = verbosity is "brief" or "detailed" or "eli5" ? verbosity : "detailed";

        var promptInstruction = safeVerbosity switch
        {
            "brief"    => "Explain this code in 2-3 sentences. What does it do and why?",
            "eli5"     => "Explain this code as if I'm new to programming. Use simple analogies.",
            _          => "Explain this code clearly: its purpose, how it works step by step, and any important edge cases.",
        };

        var prompt = $"{promptInstruction}\n\n```{safeLanguage}\n{code}\n```";

        var result = await _routing.RouteAsync(TaskType.Chat,
            new InferenceRequest { Prompt = prompt, Stream = false, Priority = QueuePriority.High },
            ct);

        return JsonSerializer.Serialize(new
        {
            explanation = result.Text,
            meta = Meta.FromInferenceResult(Guid.NewGuid().ToString("N"), result)
        }, JsonConfig.Default);
    }
}
