namespace Orchestrator.Core.Models;

public sealed record LogEntry
{
    public string Timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("o");
    public string Operation { get; init; } = default!;
    public string Type { get; init; } = default!; // Request, Response, Error, Inference
    public string? Data { get; init; }
    public long? ElapsedMs { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StackTrace { get; init; }
}

public sealed record InferenceLogEntry
{
    public string Timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("o");
    public string TaskId { get; init; } = default!;
    public string Model { get; init; } = default!;
    public string NodeId { get; init; } = default!;
    public string Prompt { get; init; } = default!;
    public string Response { get; init; } = default!;
    public long LatencyMs { get; init; }
    public int PromptLength { get; init; }
    public int ResponseLength { get; init; }
}
