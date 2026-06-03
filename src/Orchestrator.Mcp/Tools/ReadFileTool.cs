using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Orchestrator.Core.Serialization;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class ReadFileTool
{
    /// <summary>Files larger than this are written to a temp file and a path+preview is returned.</summary>
    private const long LargeFileThreshold = 8 * 1024;

    [McpServerTool(Name = "splitbrain_read_file"), Description(
        "Reads the full text content of a file from the local filesystem. " +
        "Use before reviewing, refactoring, or quoting code. " +
        "Returns content inline for files under 8 KB; returns a temp file path + preview for larger files. " +
        "Read-only — safe without write access.")]
    public Task<string> ReadFileAsync(
        [Description("Absolute path to the file to read")] string path,
        [Description("Security scope — rejects paths outside this root")] string allowedRoot,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(allowedRoot.TrimEnd(Path.DirectorySeparatorChar))
                          + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    error        = "out_of_root",
                    allowed_root = allowedRoot,
                    hint         = "path must be within allowedRoot"
                }, JsonConfig.Default));

            if (!File.Exists(fullPath))
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    error = "file_not_found",
                    path  = fullPath
                }, JsonConfig.Default));

            var content = File.ReadAllText(fullPath);
            var bytes   = new FileInfo(fullPath).Length;

            if (bytes > LargeFileThreshold)
            {
                // Write to temp so the caller can read it via their own mechanism
                var tmpDir  = Path.Combine(Path.GetTempPath(), "splitbrain");
                Directory.CreateDirectory(tmpDir);
                var tmpFile = Path.Combine(tmpDir,
                    $"sb-read_file-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}.txt");
                File.WriteAllText(tmpFile, content);

                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    summary     = $"File {path} ({bytes:N0} bytes) written to temp file",
                    output_file = tmpFile,
                    output_bytes = bytes,
                    preview     = content.Length > 2048 ? content[..2048] + "…" : content
                }, JsonConfig.Default));
            }

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                path    = fullPath,
                content,
                bytes
            }, JsonConfig.Default));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = new { code = "internal_error", message = ex.Message, retryable = false }
            }, JsonConfig.Default));
        }
    }
}
