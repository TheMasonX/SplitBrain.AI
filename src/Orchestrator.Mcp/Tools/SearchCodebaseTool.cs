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
    private const long MaxFileSizeBytes = 512 * 1024;
    private readonly IRoutingService _routing;
    private readonly IIdempotencyCache _idempotency;

    public SearchCodebaseTool(IRoutingService routing, IIdempotencyCache idempotency) { _routing = routing; _idempotency = idempotency; }

    [McpServerTool(Name = "search_codebase"), Description("Searches the codebase semantically and returns relevant snippets.")]
    public Task<string> SearchCodebaseAsync(
        [Description("Natural language query or symbol name to search for")] string query,
        [Description("Root path of the codebase to search (absolute or relative)")] string rootPath,
        [Description("File glob pattern to limit search scope, e.g. **/*.cs")] string pattern = "**/*",
        [Description("Maximum number of results to return (1-20)")] int topK = 10,
        [Description("(Optional) Idempotency key")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => IdempotencyHelper.ExecuteAsync(_idempotency, idempotencyKey, () => ExecuteCoreAsync(query, rootPath, pattern, topK, cancellationToken), cancellationToken);

    private async Task<string> ExecuteCoreAsync(string query, string rootPath, string pattern, int topK, CancellationToken cancellationToken)
    {
        try
        {
            var request = new SearchCodebaseRequest { Query = query, TopK = topK, Filters = new SearchFilters { Path = rootPath } };
            request.ValidateOrThrow(new SearchCodebaseRequestValidator());
            var files = CollectFiles(rootPath, pattern, topK * 5);
            var prompt = BuildPrompt(query, files, topK);
            var taskId = Guid.NewGuid().ToString("N");
            var result = await _routing.RouteAsync(TaskType.AgentStep, new InferenceRequest { Prompt = prompt, Stream = false, Priority = QueuePriority.Normal }, cancellationToken);
            var results = TryParseResults(result.Text, topK);
            var response = new SearchCodebaseResponse { Results = results, Meta = Meta.FromInferenceResult(taskId, result) };
            return JsonSerializer.Serialize(response, JsonConfig.Default);
        }
        catch (ValidationException vex) { return JsonSerializer.Serialize(new { error = new { code = "validation_error", message = vex.Message, retryable = false } }, JsonConfig.Default); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return JsonSerializer.Serialize(new { error = new { code = "internal_error", message = ex.Message, retryable = true } }, JsonConfig.Default); }
    }

    private static List<(string Path, string Snippet)> CollectFiles(string rootPath, string pattern, int limit)
    {
        var results = new List<(string, string)>();
        if (!Directory.Exists(rootPath)) return results;
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).Where(f => MatchGlob(f, pattern)).Take(limit))
        {
            try { var info = new FileInfo(file); if (info.Length > MaxFileSizeBytes) continue; var content = File.ReadAllText(file); var snippet = string.Join('\n', content.Split('\n').Take(200)); results.Add((file, snippet)); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
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
        foreach (var (path, snippet) in files) { sb.AppendLine($"### FILE: {path}"); sb.AppendLine(snippet); sb.AppendLine(); }
        return sb.ToString();
    }

    private static List<SearchResult> TryParseResults(string modelText, int topK)
    {
        try { var json = modelText.Trim(); if (json.StartsWith("```")) json = string.Join('\n', json.Split('\n').Skip(1).SkipLast(1)); var parsed = JsonSerializer.Deserialize<List<SearchResult>>(json, JsonConfig.Default); if (parsed is { Count: > 0 }) return parsed.Take(topK).ToList(); } catch { }
        return [new SearchResult { FilePath = "model_output", Snippet = modelText, Score = 1.0 }];
    }

    private static bool MatchGlob(string path, string pattern) { if (pattern is "*" or "**/*") return true; var ext = Path.GetExtension(pattern).TrimStart('*'); return string.IsNullOrEmpty(ext) || path.EndsWith(ext, StringComparison.OrdinalIgnoreCase); }
}
