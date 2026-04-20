using Orchestrator.Core.Enums;

namespace Orchestrator.Core.Models;

public sealed class Meta
{
    public string TaskId { get; init; } = default!;
    public string Node { get; init; } = default!;
    public string Model { get; init; } = default!;
    public int LatencyMs { get; init; }
    public int TokensIn { get; init; }
    public int TokensOut { get; init; }
}

public sealed class McpError
{
    public string Code { get; init; } = default!;
    public string Message { get; init; } = default!;
    public bool Retryable { get; init; }
    public Dictionary<string, object>? Details { get; init; }
}

public sealed class Diff
{
    public List<DiffFile> Files { get; init; } = new();
}

public sealed class DiffFile
{
    public string Path { get; init; } = default!;
    public string ChangeType { get; init; } = default!;
    public string Patch { get; init; } = default!;
}

public sealed class NodeCapabilities
{
    public string NodeId { get; init; } = default!;
    public string Model { get; init; } = default!;
    public int VramMb { get; init; }
    public bool SupportsStreaming { get; init; } = true;
}

public sealed class NodeHealth
{
    public string NodeId { get; init; } = default!;
    public NodeStatus Status { get; init; }
    public int QueueDepth { get; init; }
    public int AvailableVramMb { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record InferenceRequest
{
    public string Prompt { get; init; } = default!;
    public string Model { get; init; } = default!;
    public bool Stream { get; init; } = true;
    public bool UseFallback { get; init; } = false;
    /// <summary>Caller-assigned priority. Lower value = higher priority. Defaults to Normal (50).</summary>
    public int Priority { get; init; } = QueuePriority.Normal;
}

public sealed record InferenceResult
{
    public string Text { get; init; } = default!;
    public string NodeId { get; init; } = default!;
    public string Model { get; init; } = default!;
    public int LatencyMs { get; init; }
    public int TokensIn { get; init; }
    public int TokensOut { get; init; }
}

/// <summary>A request plus its completion source — travels through the queue together.</summary>
public sealed class InferenceQueueItem
{
    public InferenceRequest Request { get; init; } = default!;
    public TaskType TaskType { get; init; }
    public string TaskId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public TaskCompletionSource<InferenceResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Named priority constants matching the spec: Node A = high, Node B = normal.</summary>
public static class QueuePriority
{
    public const int High   = 10;
    public const int Normal = 50;
    public const int Low    = 90;
}
