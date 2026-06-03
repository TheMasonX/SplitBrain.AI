using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Orchestrator.Agents;
using Orchestrator.Agents.Models;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class AgentTaskTool
{
    private readonly IAgentOrchestrator _agent;
    private readonly IIdempotencyCache  _idempotency;
    private readonly ILoggingService    _log;
    private readonly ILogger<AgentTaskTool> _logger;

    /// <summary>
    /// Outer timeout for the entire agent task (all iterations).
    /// With MaxIterations=4 and ~30s per inference call, a generous ceiling is needed.
    /// </summary>
    private const int DefaultAgentTimeoutSeconds = 300;  // 5 minutes, adjustable

    public AgentTaskTool(
        IAgentOrchestrator agent,
        IIdempotencyCache idempotency,
        ILoggingService log,
        ILogger<AgentTaskTool> logger)
    {
        _agent       = agent;
        _idempotency = idempotency;
        _log         = log;
        _logger      = logger;
    }

    [McpServerTool(Name = "agent_task"), Description(
        "Runs a bounded autonomous agent loop (Plan → Implement → Review → Test) " +
        "for a natural-language goal. Max 4 iterations, 12k tokens. " +
        "Set applyChanges=true (and configure write gate) to apply diffs to disk.")]
    public Task<string> RunAgentTaskAsync(
        [Description("High-level goal in natural language (e.g. 'Add null-check to UserService.GetById')")]
        string goal,
        [Description("(Optional) Absolute path to the working directory the agent may patch and test")]
        string? workingDirectory = null,
        [Description("(Optional) Additional context injected into every prompt (e.g. stack trace, spec excerpt)")]
        string? context = null,
        [Description("(Optional) When true, applies the generated diff to disk. Requires write gate to be enabled. Default: false.")]
        bool applyChanges = false,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(
            _idempotency, idempotencyKey,
            () => ExecuteCoreAsync(goal, workingDirectory, context, applyChanges, cancellationToken),
            cancellationToken);

    private async Task<string> ExecuteCoreAsync(
        string goal,
        string? workingDirectory,
        string? context,
        bool applyChanges,
        CancellationToken ct)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var sw     = Stopwatch.StartNew();

        try { await _log.LogRequestAsync("agent_task", new { goal, workingDirectory, applyChanges }, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(DefaultAgentTimeoutSeconds));

        try
        {
            var request = new AgentRequest
            {
                Goal             = goal,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                Context          = string.IsNullOrWhiteSpace(context) ? null : context
            };

            var result = await _agent.RunAsync(request, cts.Token);
            sw.Stop();

            // Phase 0: applyChanges parameter is accepted but disk write is deferred
            // to Phase 1 when the 3-mode write gate is implemented.
            // TODO Phase 1: wire UnifiedDiffApplier when write gate is configured.
            if (applyChanges && !string.IsNullOrWhiteSpace(result.Diff))
            {
                _logger.LogWarning(
                    "agent_task: applyChanges=true but write gate not yet configured. " +
                    "Diff returned in response but NOT written to disk. Phase 1 will add write gate.");
            }

            var response = new AgentTaskResponse
            {
                Success         = result.Success,
                FinalState      = result.FinalState.ToString(),
                Summary         = result.Summary,
                Diff            = result.Diff,
                TotalIterations = result.TotalIterations,
                TotalTokens     = result.TotalTokensUsed,
                AbortReason     = result.AbortReason,
                Steps           = result.Steps.Select(s => new AgentStepSummary
                {
                    Role     = s.Role.ToString(),
                    State    = s.State.ToString(),
                    Success  = s.Success,
                    Tokens   = s.TokensEstimated,
                    // Truncate individual step responses to keep the MCP response manageable
                    Response = s.Response.Length > 500 ? s.Response[..500] + "…" : s.Response
                }).ToList(),
                // Fixed: Meta was default! (null) — now populated with agent run metadata
                Meta = new Meta
                {
                    TaskId    = taskId,
                    Node      = "agent",    // agent_task runs across multiple nodes internally
                    Model     = "multi",    // multiple models are used across agent steps
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    // Token counts: sum across all steps
                    TokensIn  = result.TotalTokensUsed / 2,   // rough split estimate
                    TokensOut = result.TotalTokensUsed / 2
                }
            };

            try { await _log.LogResponseAsync("agent_task", response, sw.ElapsedMilliseconds, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("agent_task timed out after {Timeout}s for task {TaskId}",
                DefaultAgentTimeoutSeconds, taskId);

            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code      = "tool_timeout",
                    message   = $"agent_task timed out after {DefaultAgentTimeoutSeconds}s. " +
                                "The agent loop did not complete within the allowed time. " +
                                "Try a simpler goal or check node health.",
                    retryable = false   // timeout on a long agent run shouldn't auto-retry
                },
                meta = new { taskId, latencyMs = (int)sw.ElapsedMilliseconds }
            }, JsonConfig.Default);
        }
    }
}

// ---------------------------------------------------------------------------
// Response models
// ---------------------------------------------------------------------------

public sealed class AgentTaskResponse : Orchestrator.Core.Interfaces.IMcpResponse
{
    public Meta Meta      { get; init; } = default!;
    public McpError? Error { get; init; }
    public bool   Success         { get; init; }
    public string FinalState      { get; init; } = string.Empty;
    public string Summary         { get; init; } = string.Empty;
    public string Diff            { get; init; } = string.Empty;
    public int    TotalIterations { get; init; }
    public int    TotalTokens     { get; init; }
    public string? AbortReason    { get; init; }
    public List<AgentStepSummary> Steps { get; init; } = [];
}

public sealed class AgentStepSummary
{
    public string Role     { get; init; } = string.Empty;
    public string State    { get; init; } = string.Empty;
    public bool   Success  { get; init; }
    public int    Tokens   { get; init; }
    public string Response { get; init; } = string.Empty;
}
