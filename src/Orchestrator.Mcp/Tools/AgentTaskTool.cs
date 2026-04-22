using System.ComponentModel;
using System.Text.Json;
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
    private readonly IIdempotencyCache _idempotency;

    public AgentTaskTool(IAgentOrchestrator agent, IIdempotencyCache idempotency)
    {
        _agent = agent;
        _idempotency = idempotency;
    }

    [McpServerTool(Name = "agent_task"),
     Description("Runs a bounded autonomous agent loop (Plan → Implement → Review → Test) for a natural-language goal. Max 4 iterations, 12k tokens.")]
    public Task<string> RunAgentTaskAsync(
        [Description("High-level goal in natural language (e.g. 'Add null-check to UserService.GetById')")]
        string goal,
        [Description("(Optional) Absolute path to the working directory the agent may patch and test")]
        string? workingDirectory = null,
        [Description("(Optional) Additional context injected into every prompt (e.g. stack trace, spec excerpt)")]
        string? context = null,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(goal, workingDirectory, context, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string goal, string? workingDirectory, string? context, CancellationToken cancellationToken)
    {
        var request = new AgentRequest
        {
            Goal             = goal,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            Context          = string.IsNullOrWhiteSpace(context) ? null : context
        };

        var result = await _agent.RunAsync(request, cancellationToken);

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
                Response = s.Response.Length > 300 ? s.Response[..300] + "…" : s.Response
            }).ToList()
        };

        return JsonSerializer.Serialize(response, JsonConfig.Default);
    }
}

// ---------------------------------------------------------------------------
// Response models
// ---------------------------------------------------------------------------

public sealed class AgentTaskResponse : Orchestrator.Core.Interfaces.IMcpResponse
{
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
    public bool Success { get; init; }
    public string FinalState { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Diff { get; init; } = string.Empty;
    public int TotalIterations { get; init; }
    public int TotalTokens { get; init; }
    public string? AbortReason { get; init; }
    public List<AgentStepSummary> Steps { get; init; } = [];
}

public sealed class AgentStepSummary
{
    public string Role { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int Tokens { get; init; }
    public string Response { get; init; } = string.Empty;
}
