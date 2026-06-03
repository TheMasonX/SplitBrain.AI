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
    public QueryAllowedRootTool(IOptions<WriteAccessOptions> options) => _options = options.Value;

    [McpServerTool(Name = "splitbrain_query_allowed_root"),
     Description("Returns the current write-access mode and allowed write tools. Call this first to understand what operations are permitted before attempting file mutations. Read-only.")]
    public Task<string> QueryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            write_mode = _options.Mode.ToString(),
            allowed_write_tools = _options.AllowedWriteTools,
            hint = _options.Mode switch
            {
                WriteAccessMode.Disabled      => "Write operations are disabled. Ask the operator to set Mcp:WriteAccess:Mode.",
                WriteAccessMode.PerCallEnable => "Write operations require enable_write: true in each call.",
                WriteAccessMode.AlwaysEnabled => "Write operations are unrestricted.",
                _ => ""
            }
        }, JsonConfig.Default));
    }
}
