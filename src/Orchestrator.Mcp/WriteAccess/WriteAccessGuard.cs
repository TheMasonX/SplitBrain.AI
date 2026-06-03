using Microsoft.Extensions.Options;
using System.Text.Json;
using Orchestrator.Core.Serialization;

namespace Orchestrator.Mcp.WriteAccess;

/// <summary>
/// Centralised write-access gating for all mutating MCP tools.
/// Inject this singleton into any tool that writes files, applies patches, or runs tests.
/// Returns a structured JSON error string when write is not permitted, or null when allowed.
/// </summary>
public sealed class WriteAccessGuard
{
    private readonly WriteAccessOptions _options;

    public WriteAccessGuard(IOptions<WriteAccessOptions> options)
        => _options = options.Value;

    /// <summary>
    /// Checks whether the calling tool is permitted to perform a write operation.
    /// </summary>
    /// <param name="toolName">The MCP tool name (for error messages).</param>
    /// <param name="enableWriteParam">Per-call override; only used in PerCallEnable mode.</param>
    /// <returns>Null if allowed; structured JSON error string if denied.</returns>
    public string? CheckWrite(string toolName, bool enableWriteParam = false)
    {
        if (_options.Mode == WriteAccessMode.AlwaysEnabled) return null;

        if (_options.Mode == WriteAccessMode.Disabled)
            return JsonSerializer.Serialize(new
            {
                error = "write_access_disabled",
                gate  = "global",
                tool  = toolName,
                hint  = "Set Mcp:WriteAccess:Mode to PerCallEnable or AlwaysEnabled in appsettings.json."
            }, JsonConfig.Default);

        // PerCallEnable: caller must explicitly pass enable_write: true
        if (!enableWriteParam)
            return JsonSerializer.Serialize(new
            {
                error = "write_access_disabled",
                gate  = "per_call",
                tool  = toolName,
                hint  = "Include enable_write: true in the tool call parameters to authorise this write."
            }, JsonConfig.Default);

        return null; // write permitted
    }
}
