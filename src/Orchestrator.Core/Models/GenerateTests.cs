using Orchestrator.Core.Interfaces;

namespace Orchestrator.Core.Models;

public sealed class GenerateTestsRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Code { get; init; } = default!;
    public string Language { get; init; } = default!;
    public string? Framework { get; init; }
}

public sealed class GenerateTestsResponse : IMcpResponse
{
    public List<GeneratedTestFile> Files { get; init; } = new();
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}

public sealed class GeneratedTestFile
{
    public string Path { get; init; } = default!;
    public string Content { get; init; } = default!;
}
