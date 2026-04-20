using Orchestrator.Core.Interfaces;

namespace Orchestrator.Core.Models;

public sealed class ApplyPatchRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public Diff Diff { get; init; } = new();
    public bool DryRun { get; init; }
}

public sealed class ApplyPatchResponse : IMcpResponse
{
    public bool Success { get; init; }
    public List<string> AppliedFiles { get; init; } = new();
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}
