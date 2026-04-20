using Orchestrator.Core.Interfaces;

namespace Orchestrator.Core.Models;

public sealed record ReviewCodeRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Code { get; init; } = default!;
    public string Language { get; init; } = default!;
    public string Focus { get; init; } = default!;
    public List<RelatedFile>? Context { get; init; }
}

public sealed class RelatedFile
{
    public string Path { get; init; } = default!;
    public string Content { get; init; } = default!;
}

public sealed class ReviewCodeResponse : IMcpResponse
{
    public string Summary { get; init; } = default!;
    public List<ReviewIssue> Issues { get; init; } = new();
    public Diff? SuggestedDiff { get; init; }
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}

public sealed class ReviewIssue
{
    public string Severity { get; init; } = default!;
    public string Type { get; init; } = default!;
    public string Message { get; init; } = default!;
    public IssueLocation Location { get; init; } = default!;
    public string Suggestion { get; init; } = default!;
}

public sealed class IssueLocation
{
    public string File { get; init; } = default!;
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
}
