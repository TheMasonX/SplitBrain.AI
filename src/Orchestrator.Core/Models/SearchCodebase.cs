using Orchestrator.Core.Interfaces;

namespace Orchestrator.Core.Models;

public sealed class SearchCodebaseRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Query { get; init; } = default!;
    public int TopK { get; init; } = 5;
    public SearchFilters? Filters { get; init; }
}

public sealed class SearchFilters
{
    public string? Path { get; init; }
    public string? Language { get; init; }
}

public sealed class SearchCodebaseResponse : IMcpResponse
{
    public List<SearchResult> Results { get; init; } = new();
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}

public sealed class SearchResult
{
    public string FilePath { get; init; } = default!;
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public string Snippet { get; init; } = default!;
    public double Score { get; init; }
}
