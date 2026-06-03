using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;
using Orchestrator.Mcp.Helpers;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class SearchCodebaseTool
{
    private readonly IRoutingService  _routing;
    private readonly IIdempotencyCache _idempotency;
    private readonly ILoggingService  _log;
    private readonly ILogger<SearchCodebaseTool> _logger;

    private const int DefaultToolTimeoutSeconds = 60;

    public SearchCodebaseTool(
        IRoutingService routing,
        IIdempotencyCache idempotency,
        ILoggingService log,
        ILogger<SearchCodebaseTool> logger)
    {
        _routing     = routing;
        _idempotency = idempotency;
        _log         = log;
        _logger      = logger;
    }

    [McpServerTool(Name = "search_codebase"), Description(
        "Searches the codebase semantically using glob pattern matching + LLM ranking. " +
        "Returns relevant file snippets ranked by relevance to the query.")]
    public Task<string> SearchCodebaseAsync(
        [Description("Natural language query or symbol name to search for")] string query,
        [Description("Root path of the codebase to search (absolute or relative)")] string rootPath,
        [Description("File glob pattern to limit search scope, e.g. **/*.cs or src/**/*.ts")] string pattern = "**/*",
        [Description("Maximum number of results to return (1–20)")] int topK = 10,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(
            _idempotency, idempotencyKey,
            () => ExecuteCoreAsync(query, rootPath, pattern, topK, cancellationToken),
            cancellationToken);

    private async Task<string> ExecuteCoreAsync(
        string query, string rootPath, string pattern, int topK, CancellationToken ct)
    {
        var request = new SearchCodebaseRequest
        {
            Query   = query,
            TopK    = topK,
            Filters = new SearchFilters { Path = rootPath }
        };
        request.ValidateOrThrow(new SearchCodebaseRequestValidator());

        var taskId = Guid.NewGuid().ToString("N");
        var files = CollectFiles(rootPath, pattern, topK * 5);
        var prompt = BuildPrompt(query, files, topK);

        try { await _log.LogRequestAsync("search_codebase", request, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(DefaultToolTimeoutSeconds));

        try
        {
            var result = await _routing.RouteAsync(
                TaskType.AgentStep,
                new InferenceRequest { Prompt = prompt, Stream = false, Priority = QueuePriority.Normal },
                cts.Token);

            try { await _log.LogInferenceAsync(taskId, prompt, result.Text, result.Model, result.NodeId, result.LatencyMs, ct); } catch (Exception) { /* log failure — intentionally silent; tool must not fail on logging errors */ }

            // Parse the model's JSON array response — maximally forgiving
            var results = TryParseResults(result.Text, topK);

            var response = new SearchCodebaseResponse
            {
                Results = results,
                Meta    = new Meta
                {
                    TaskId    = taskId,
                    Node      = result.NodeId,
                    Model     = result.Model,
                    LatencyMs = result.LatencyMs,
                    TokensIn  = result.TokensIn,
                    TokensOut = result.TokensOut
                }
            };

            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("search_codebase timed out after {Timeout}s for task {TaskId}",
                DefaultToolTimeoutSeconds, taskId);

            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code      = "tool_timeout",
                    message   = $"search_codebase timed out after {DefaultToolTimeoutSeconds}s.",
                    retryable = true
                }
            }, JsonConfig.Default);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects files matching a real glob pattern using Microsoft.Extensions.FileSystemGlobbing.
    /// Correctly handles patterns like src/**/*.cs, **/*.{ts,js}, etc.
    /// Previously this was a fake glob (extension-only) that silently ignored directory paths.
    /// </summary>
    private static List<(string Path, string Snippet)> CollectFiles(
        string rootPath, string pattern, int limit)
    {
        var results = new List<(string, string)>();

        if (!Directory.Exists(rootPath)) return results;

        // Use real glob matching — this correctly handles directory-scoped patterns
        // like src/**/*.cs which the old MatchGlob(path, pattern) completely ignored.
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);

        // Exclude common non-source directories
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/node_modules/**");

        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(rootPath));
        var matchResult   = matcher.Execute(directoryInfo);

        foreach (var file in matchResult.Files.Take(limit))
        {
            var fullPath = Path.Combine(rootPath, file.Path);
            try
            {
                var content = File.ReadAllText(fullPath);
                var snippet = string.Join('\n', content.Split('\n').Take(200));
                results.Add((fullPath, snippet));
            }
            catch (IOException) { /* skip unreadable files */ }
        }

        return results;
    }

    private static string BuildPrompt(string query, List<(string Path, string Snippet)> files, int topK)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Find the top {topK} most relevant locations for the query: \"{query}\"");
        sb.AppendLine("Return a JSON array of objects with fields: filePath, lineStart, lineEnd, snippet, score (0.0-1.0).");
        sb.AppendLine("Return ONLY the JSON array with no explanation or markdown fences.");
        sb.AppendLine();

        foreach (var (path, snippet) in files)
        {
            sb.AppendLine($"### FILE: {path}");
            sb.AppendLine(snippet);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses search results from the model response, trying multiple extraction
    /// strategies including JSON extraction from markdown-wrapped responses.
    /// </summary>
    private static List<SearchResult> TryParseResults(string modelText, int topK)
    {
        // Use ResponseCleaner.TryExtractJson for maximum forgiveness
        if (ResponseCleaner.TryExtractJson<List<SearchResult>>(modelText, out var parsed)
            && parsed is { Count: > 0 })
        {
            return parsed.Take(topK).ToList();
        }

        // Fallback: return the raw model text as a single result
        // This ensures callers always get something, even if structuring failed.
        return [new SearchResult { FilePath = "model_output", Snippet = modelText, Score = 1.0 }];
    }
}
