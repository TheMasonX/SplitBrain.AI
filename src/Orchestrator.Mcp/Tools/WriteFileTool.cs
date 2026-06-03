using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.WriteAccess;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class WriteFileTool
{
    private readonly WriteAccessGuard _writeGuard;
    public WriteFileTool(WriteAccessGuard writeGuard) => _writeGuard = writeGuard;

    [McpServerTool(Name = "splitbrain_write_file"),
     Description("Writes full content to a file within allowedRoot. Creates new files or overwrites existing ones. Requires write access enabled. Use enable_write: true when write gate is in PerCallEnable mode. Pair with read_file before and run_tests after. MUTATES files.")]
    public async Task<string> WriteFileAsync(
        [Description("Absolute path to the file to write")] string path,
        [Description("Full content to write to the file")] string content,
        [Description("Security scope — rejects paths outside this root")] string allowedRoot,
        [Description("Required when write gate is PerCallEnable. Set true to authorize mutation.")] bool enableWrite = false,
        CancellationToken cancellationToken = default)
    {
        var writeError = _writeGuard.CheckWrite("splitbrain_write_file", enableWrite);
        if (writeError is not null) return writeError;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(allowedRoot.TrimEnd(Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Serialize(new { error = "out_of_root", allowed_root = allowedRoot }, JsonConfig.Default);
            var dir = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(dir);
            // Atomic write: write to temp then rename
            var tmp = fullPath + ".tmp";
            await File.WriteAllTextAsync(tmp, content, cancellationToken);
            File.Move(tmp, fullPath, overwrite: true);
            return JsonSerializer.Serialize(new { written = true, path = fullPath, bytes = content.Length }, JsonConfig.Default);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return JsonSerializer.Serialize(new { error = new { code = "internal_error", message = ex.Message, retryable = false } }, JsonConfig.Default); }
    }
}
