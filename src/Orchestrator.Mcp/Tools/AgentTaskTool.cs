using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Orchestrator.Agents;
using Orchestrator.Agents.Models;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.Idempotency;
using Orchestrator.Mcp.Patching;
using Orchestrator.Mcp.WriteAccess;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class AgentTaskTool
{
    private readonly IAgentOrchestrator _agent;
    private readonly IIdempotencyCache  _idempotency;
    private readonly ILoggingService    _log;
    private readonly ILogger<AgentTaskTool> _logger;
    private readonly WriteAccessGuard   _writeGuard;

    /// <summary>Outer timeout for the entire agent task (all iterations).</summary>
    private const int DefaultAgentTimeoutSeconds = 300;

    public AgentTaskTool(
        IAgentOrchestrator agent,
        IIdempotencyCache idempotency,
        ILoggingService log,
        ILogger<AgentTaskTool> logger,
        WriteAccessGuard writeGuard)
    {
        _agent       = agent;
        _idempotency = idempotency;
        _log         = log;
        _logger      = logger;
        _writeGuard  = writeGuard;
    }

    [McpServerTool(Name = "agent_task"), Description(
        "Runs a bounded autonomous agent loop (Plan → Implement → Review → Test) " +
        "for a natural-language goal. Max 4 iterations, 12k tokens. " +
        "Set applyChanges=true to apply generated diffs to disk; requires write gate enabled. " +
        "Call splitbrain_query_allowed_root to check write gate mode first.")]
    public Task<string> RunAgentTaskAsync(
        [Description("High-level goal in natural language (e.g. 'Add null-check to UserService.GetById')")]
        string goal,
        [Description("(Optional) Absolute path to the working directory the agent may patch and test")]
        string? workingDirectory = null,
        [Description("(Optional) Additional context injected into every prompt")]
        string? context = null,
        [Description("(Optional) Apply generated diff to disk. Requires write gate ≠ Disabled. " +
                     "workingDirectory must also be set. Default: false (diff returned but not applied).")]
        bool applyChanges = false,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(
            _idempotency, idempotencyKey,
            () => ExecuteCoreAsync(goal, workingDirectory, context, applyChanges, cancellationToken),
            cancellationToken);

    private async Task<string> ExecuteCoreAsync(
        string goal, string? workingDirectory, string? context,
        bool applyChanges, CancellationToken ct)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var sw     = Stopwatch.StartNew();

        try { await _log.LogRequestAsync("agent_task", new { goal, workingDirectory, applyChanges }, ct); }
        catch (Exception) { /* log failure — intentionally silent */ }


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

            // ── Apply diff to disk (write-gated) ──────────────────────────────
            string? writeGateError = null;
            List<string> appliedFiles = [];

            if (applyChanges && !string.IsNullOrWhiteSpace(result.Diff))
            {
                // applyChanges=true counts as per-call enableWrite authorisation
                writeGateError = _writeGuard.CheckWrite("agent_task", enableWriteParam: true);

                if (writeGateError is not null)
                {
                    _logger.LogWarning("agent_task: applyChanges blocked by write gate for task {TaskId}", taskId);
                }
                else if (string.IsNullOrWhiteSpace(workingDirectory))
                {
                    _logger.LogWarning(
                        "agent_task: applyChanges=true but workingDirectory is not set. " +
                        "Diff returned in response but NOT written to disk.");
                }
                else
                {
                    appliedFiles = TryApplyDiffToDirectory(result.Diff, workingDirectory);
                    if (appliedFiles.Count > 0)
                        _logger.LogInformation(
                            "agent_task: applied diff to {Count} file(s) in {Dir}: {Files}",
                            appliedFiles.Count, workingDirectory, string.Join(", ", appliedFiles));
                }
            }

            var response = new AgentTaskResponse
            {
                Success         = result.Success,
                FinalState      = result.FinalState.ToString(),
                Summary         = result.Summary,
                Diff            = result.Diff,
                AppliedFiles    = appliedFiles,
                TotalIterations = result.TotalIterations,
                TotalTokens     = result.TotalTokensUsed,
                AbortReason     = result.AbortReason,
                WriteGateError  = writeGateError,
                Steps           = result.Steps.Select(s => new AgentStepSummary
                {
                    Role     = s.Role.ToString(),
                    State    = s.State.ToString(),
                    Success  = s.Success,
                    Tokens   = s.TokensEstimated,
                    Response = s.Response.Length > 500 ? s.Response[..500] + "…" : s.Response
                }).ToList(),
                Meta = Meta.FromInferenceResult(taskId, new InferenceResult
                {
                    Text      = result.Summary,
                    NodeId    = "agent",
                    Model     = "multi",
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    TokensIn  = result.TotalTokensUsed / 2,
                    TokensOut = result.TotalTokensUsed / 2
                })
            };

            try { await _log.LogResponseAsync("agent_task", response, sw.ElapsedMilliseconds, ct); }
            catch (Exception) { /* log failure — intentionally silent */ }

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
                                "The agent loop did not complete. Try a simpler goal or check node health.",
                    retryable = false
                },
                meta = new { taskId, latencyMs = (int)sw.ElapsedMilliseconds }
            }, JsonConfig.Default);
        }
    }

    // ── Diff application helpers ───────────────────────────────────────────────

    /// <summary>
    /// Parses a unified diff for target file paths and applies each hunk.
    /// Security: all paths are validated against workingDirectory before writing.
    /// Best-effort: logs per-file failures without aborting the batch.
    /// </summary>
    private List<string> TryApplyDiffToDirectory(string unifiedDiff, string workingDirectory)
    {
        var applied = new List<string>();
        var fullDir = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;

        // Extract relative file paths from "+++ b/relative/path" lines
        // Also handles "+++ relative/path" (standard unified diff without b/ prefix)
        var filePattern = new Regex(
            @"^\+\+\+ (?:b/)?(.+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        var targetFiles = filePattern.Matches(unifiedDiff)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(p => p != "/dev/null" && !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToList();

        if (targetFiles.Count == 0)
        {
            _logger.LogWarning("agent_task: no target files found in diff");
            return applied;
        }

        foreach (var relPath in targetFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(workingDirectory, relPath));

            // Security: reject paths outside workingDirectory
            if (!fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "agent_task: SECURITY — skipping {RelPath} (resolves outside workingDirectory)",
                    relPath);
                continue;
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("agent_task: skipping {RelPath} — file not found at {FullPath}",
                    relPath, fullPath);
                continue;
            }

            try
            {
                var original = File.ReadAllText(fullPath);
                var patched  = UnifiedDiffApplier.Apply(original, unifiedDiff);

                // Atomic write (temp + rename)
                var tmp = fullPath + ".agentpatch.tmp";
                File.WriteAllText(tmp, patched);
                File.Move(tmp, fullPath, overwrite: true);
                applied.Add(relPath);
            }
            catch (PatchException ex)
            {
                _logger.LogWarning("agent_task: patch failed for {RelPath}: {Msg}", relPath, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "agent_task: unexpected error applying patch to {RelPath}", relPath);
            }
        }

        return applied;
    }
}

public sealed class AgentTaskResponse : Orchestrator.Core.Interfaces.IMcpResponse
{
    public Meta     Meta           { get; init; } = default!;
    public McpError? Error         { get; init; }
    public bool     Success        { get; init; }
    public string   FinalState     { get; init; } = string.Empty;
    public string   Summary        { get; init; } = string.Empty;
    public string   Diff           { get; init; } = string.Empty;

    /// <summary>Files that were actually written to disk when applyChanges=true. Empty if not applied.</summary>
    public List<string> AppliedFiles { get; init; } = [];

    /// <summary>Non-null when applyChanges=true was requested but the write gate blocked it.</summary>
    public string? WriteGateError  { get; init; }

    public int    TotalIterations  { get; init; }
    public int    TotalTokens      { get; init; }
    public string? AbortReason     { get; init; }
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
