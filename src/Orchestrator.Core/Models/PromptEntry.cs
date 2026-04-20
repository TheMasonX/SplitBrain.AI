using Orchestrator.Core.Enums;

namespace Orchestrator.Core.Models;

/// <summary>A single entry in the prompt history ring buffer.</summary>
public sealed class PromptEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Prompt { get; init; } = default!;
    public string NodeId { get; init; } = default!;
    public TaskType TaskType { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Non-null once the request completes.</summary>
    public string? Response { get; set; }
    public bool Success { get; set; }
}
