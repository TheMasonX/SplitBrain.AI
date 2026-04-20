using Orchestrator.Core.Interfaces;

namespace Orchestrator.Core.Models;

public sealed class RunTestsRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string ProjectPath { get; init; } = default!;
    public string? TestFilter { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

public sealed class RunTestsResponse : IMcpResponse
{
    public TestSummary Summary { get; init; } = default!;
    public List<TestFailure> Failures { get; init; } = new();
    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}

public sealed class TestSummary
{
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int DurationMs { get; init; }
}

public sealed class TestFailure
{
    public string TestName { get; init; } = default!;
    public string Message { get; init; } = default!;
    public string? StackTrace { get; init; }
}
