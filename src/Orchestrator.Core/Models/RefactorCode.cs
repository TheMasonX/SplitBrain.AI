using Orchestrator.Core.Interfaces;

namespace Orchestrator.Core.Models;

public sealed class RefactorCodeRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Goal { get; init; } = default!;
    public List<CodeFile> Codebase { get; init; } = new();
    public RefactorConstraints Constraints { get; init; } = new();
}

public sealed class CodeFile
{
    public string Path { get; init; } = default!;
    public string Content { get; init; } = default!;
}

public sealed class RefactorConstraints
{
    public bool PreserveBehavior { get; init; } = true;
    public int MaxFiles { get; init; } = 10;
}

public sealed class RefactorCodeResponse : IMcpResponse
{
    public string Summary { get; init; } = default!;
    public Diff? SuggestedDiff { get; init; }
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}
