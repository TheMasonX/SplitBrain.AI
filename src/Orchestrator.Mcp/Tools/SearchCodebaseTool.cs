using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class SearchCodebaseTool
{
    private readonly IRoutingService _routing;
    private readonly IIdempotencyCache _idempotency;

    public SearchCodebaseTool(IRoutingService routing, IIdempotencyCache idempotency)
    {
        _routing = routing;
        _idempotency = idempotency;
    }

    [McpServerTool(Name = "search_codebase"), Description("Searches the codebase semantically and returns relevant snippets.")]
    public Task<string> SearchCodebaseAsync(
        [Description("Natural language query or symbol name to search for")] string query,
        [Description("Root path of the codebase to search (absolute or relative)")] string rootPath,
        [Description("File glob pattern to limit search scope, e.g. **/*.cs")] string pattern = "**/*",
        [Description("Maximum number of results to return (1–20)")] int topK = 10,
        [Description("(Optional) Idempotency key — same key returns cached result within 5 minutes")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(query, rootPath, pattern, topK, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string query, string rootPath, string pattern, int topK, CancellationToken cancellationToken)
    {
        var request = new SearchCodebaseRequest
        {
            Query = query,
            TopK = topK,
            Filters = new SearchFilters { Path = rootPath }
        };

        request.ValidateOrThrow(new SearchCodebaseRequestValidator());

        var files = CollectFiles(rootPath, pattern, topK * 5);
        var prompt = BuildPrompt(query, files, topK);
        var taskId = Guid.NewGuid().ToString("N");

        var result = await _routing.RouteAsync(
            TaskType.AgentStep,
            new InferenceRequest { Prompt = prompt, Stream = false, Priority = QueuePriority.Normal },
            cancellationToken);

        // Try to parse the model's JSON array response; fall back to a single summary result
        var results = TryParseResults(result.Text, topK);

        var response = new SearchCodebaseResponse
        {
            Results = results,
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

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static List<(string Path, string Snippet)> CollectFiles(string rootPath, string pattern, int limit)
    {
        var results = new List<(string, string)>();

        if (!Directory.Exists(rootPath))
            return results;

        var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(f => MatchGlob(f, pattern))
            .Take(limit);

        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                var snippet = string.Join('\n', content.Split('\n').Take(200));
                results.Add((file, snippet));
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
        sb.AppendLine("Return ONLY the JSON array with no explanation or markdown.");
        sb.AppendLine();

        foreach (var (path, snippet) in files)
        {
            sb.AppendLine($"### FILE: {path}");
            sb.AppendLine(snippet);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<SearchResult> TryParseResults(string modelText, int topK)
    {
        try
        {
            // Strip markdown fences if present
            var json = modelText.Trim();
            if (json.StartsWith("```")) json = string.Join('\n', json.Split('\n').Skip(1).SkipLast(1));

            var parsed = JsonSerializer.Deserialize<List<SearchResult>>(json, JsonConfig.Default);
            if (parsed is { Count: > 0 })
                return parsed.Take(topK).ToList();
        }
        catch { /* fall through to summary result */ }

        return [new SearchResult { FilePath = "model_output", Snippet = modelText, Score = 1.0 }];
    }

    private static bool MatchGlob(string path, string pattern)
    {
        if (pattern is "*" or "**/*")
            return true;

        var ext = Path.GetExtension(pattern).TrimStart('*');
        return string.IsNullOrEmpty(ext) || path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
    }
}
