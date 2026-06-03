using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Orchestrator.Core.Serialization;
using Orchestrator.Mcp.WriteAccess;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class QueryAllowedRootTool
{
    private readonly WriteAccessOptions _options;
    public QueryAllowedRootTool(IOptions<WriteAccessOptions> options)
        => _options = options.Value;

    [McpServerTool(Name = "splitbrain_query_allowed_root"), Description(
        "Returns the current write-access mode and allowed write tools. " +
        "Call this first to understand what file mutations are permitted. Read-only.")]
    public Task<string> QueryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            write_mode          = _options.Mode.ToString(),
            allowed_write_tools = _options.AllowedWriteTools,
            hint = _options.Mode switch
            {
                WriteAccessMode.Disabled      => "Write ops disabled. Set Mcp:WriteAccess:Mode in appsettings.json.",
                WriteAccessMode.PerCallEnable => "Write ops require enable_write: true in each call.",
                WriteAccessMode.AlwaysEnabled => "Write ops unrestricted.",
                _                             => string.Empty
            }
        }, JsonConfig.Default));
    }
}
