using Microsoft.Extensions.Options;
using System.Text.Json;
using Orchestrator.Core.Serialization;

namespace Orchestrator.Mcp.WriteAccess;

public sealed class WriteAccessGuard
{
    private readonly WriteAccessOptions _options;

    public WriteAccessGuard(IOptions<WriteAccessOptions> options)
        => _options = options.Value;

    /// <summary>
    /// Returns a structured error JSON string if write is not permitted, or null if allowed.
    /// </summary>
    public string? CheckWrite(string toolName, bool enableWriteParam)
    {
        if (_options.Mode == WriteAccessMode.AlwaysEnabled) return null;
        if (_options.Mode == WriteAccessMode.Disabled)
            return JsonSerializer.Serialize(new {
                error = "write_access_disabled",
                gate  = "global",
                hint  = "Set Mcp:WriteAccess:Mode to PerCallEnable or AlwaysEnabled in appsettings.json"
            }, JsonConfig.Default);
        // PerCallEnable
        if (!enableWriteParam)
            return JsonSerializer.Serialize(new {
                error = "write_access_disabled",
                gate  = "per_call",
                hint  = "Include enable_write: true in the tool parameters to authorize this mutation"
            }, JsonConfig.Default);
        return null; // allowed
    }
}
