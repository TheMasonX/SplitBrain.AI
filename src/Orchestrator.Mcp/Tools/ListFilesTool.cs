using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using ModelContextProtocol.Server;
using Orchestrator.Core.Serialization;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class ListFilesTool
{
    [McpServerTool(Name = "splitbrain_list_files"),
     Description("Lists files in a directory using real glob pattern matching. Use to discover files before reading or patching. Supports patterns like **/*.cs, src/**/*.json. Returns path, size, and modified date per file. Read-only.")]
    public Task<string> ListFilesAsync(
        [Description("Absolute path to the directory to search")] string path,
        [Description("Security scope — rejects paths outside this root")] string allowedRoot,
        [Description("Glob pattern. Default **/* returns all files. Example: **/*.cs")] string pattern = "**/*",
        [Description("Max results (default 500)")] int maxResults = 500,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(allowedRoot.TrimEnd(Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(JsonSerializer.Serialize(new { error = "out_of_root", allowed_root = allowedRoot }, JsonConfig.Default));
            if (!Directory.Exists(fullPath))
                return Task.FromResult(JsonSerializer.Serialize(new { error = "directory_not_found", path = fullPath }, JsonConfig.Default));

            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            var results = matcher.GetResultsInFullPath(fullPath)
                .Take(maxResults + 1)
                .Select(f => new FileInfo(f))
                .ToList();
            var truncated = results.Count > maxResults;
            var files = results.Take(maxResults).Select(fi => new
            {
                path = fi.FullName,
                size_bytes = fi.Length,
                modified_utc = fi.LastWriteTimeUtc.ToString("o")
            }).ToArray();
            return Task.FromResult(JsonSerializer.Serialize(new { files, count = files.Length, truncated }, JsonConfig.Default));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(JsonSerializer.Serialize(new { error = new { code = "internal_error", message = ex.Message, retryable = false } }, JsonConfig.Default)); }
    }
}
