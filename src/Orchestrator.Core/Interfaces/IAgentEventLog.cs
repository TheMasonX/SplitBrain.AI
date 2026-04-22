using System.Runtime.CompilerServices;

namespace Orchestrator.Core.Interfaces;

public enum AgentStepType
{
    Init,
    Plan,
    Implement,
    Review,
    Test,
    Done,
    Fail,
    FallbackTriggered,
    ValidationFailed
}

public record AgentStepEvent
{
    public required string TaskId { get; init; }
    public required int StepIndex { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required AgentStepType StepType { get; init; }
    public required string Summary { get; init; }
    public string? ModelId { get; init; }
    public string? NodeId { get; init; }
    public int? TokensConsumed { get; init; }
    public double? LatencyMs { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public interface IAgentEventLog
{
    Task AppendAsync(AgentStepEvent step, CancellationToken ct = default);
    IAsyncEnumerable<AgentStepEvent> ReplayAsync(
        string taskId,
        [EnumeratorCancellation] CancellationToken ct = default);
    Task<int> GetTotalTokensAsync(string taskId, CancellationToken ct = default);
}
