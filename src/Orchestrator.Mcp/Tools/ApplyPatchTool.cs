using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;
using Orchestrator.Mcp.Patching;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class ApplyPatchTool
{
    [McpServerTool(Name = "apply_patch"), Description("Applies a unified diff patch to a file on disk within the allowed root directory.")]
    public async Task<string> ApplyPatchAsync(
        [Description("Absolute path to the file to patch")] string filePath,
        [Description("Unified diff patch content (output of `diff -u`)")] string patch,
        [Description("Allowed root directory — patch is rejected if filePath is outside this scope")] string allowedRoot,
        [Description("When true, validates the patch without writing to disk")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ApplyPatchRequest
        {
            DryRun = dryRun,
            Diff = new Diff
            {
                Files = [new DiffFile { Path = filePath, ChangeType = "modify", Patch = patch }]
            }
        };

        request.ValidateOrThrow(new ApplyPatchRequestValidator());

        // Security: enforce that the target file is under the allowed root
        var fullPath = Path.GetFullPath(filePath);
        var fullRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new ApplyPatchResponse
            {
                Success = false,
                Error = new McpError
                {
                    Code = "PATH_VIOLATION",
                    Message = $"filePath '{filePath}' is outside the allowed root '{allowedRoot}'",
                    Retryable = false
                },
                Meta = new Meta { TaskId = Guid.NewGuid().ToString("N"), Node = "local" }
            }, JsonConfig.Default);
        }

        if (!File.Exists(fullPath))
        {
            return JsonSerializer.Serialize(new ApplyPatchResponse
            {
                Success = false,
                Error = new McpError
                {
                    Code = "FILE_NOT_FOUND",
                    Message = $"File not found: {fullPath}",
                    Retryable = false
                },
                Meta = new Meta { TaskId = Guid.NewGuid().ToString("N"), Node = "local" }
            }, JsonConfig.Default);
        }

        try
        {
            var original = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var patched = UnifiedDiffApplier.Apply(original, patch);

            if (!dryRun)
                await File.WriteAllTextAsync(fullPath, patched, cancellationToken);

            return JsonSerializer.Serialize(new ApplyPatchResponse
            {
                Success = true,
                AppliedFiles = [fullPath],
                Meta = new Meta { TaskId = Guid.NewGuid().ToString("N"), Node = "local" }
            }, JsonConfig.Default);
        }
        catch (PatchException ex)
        {
            return JsonSerializer.Serialize(new ApplyPatchResponse
            {
                Success = false,
                Error = new McpError
                {
                    Code = "PATCH_FAILED",
                    Message = ex.Message,
                    Retryable = false
                },
                Meta = new Meta { TaskId = Guid.NewGuid().ToString("N"), Node = "local" }
            }, JsonConfig.Default);
        }
    }
}
