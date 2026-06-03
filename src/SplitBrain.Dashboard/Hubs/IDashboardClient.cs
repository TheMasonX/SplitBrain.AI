using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace SplitBrain.Dashboard.Hubs;

// ---------------------------------------------------------------------------
// Strongly-typed SignalR client interface
// ---------------------------------------------------------------------------

/// <summary>
/// All real-time dashboard updates flow through these methods.
/// Blazor components subscribe via the DashboardHub.
/// </summary>
public interface IDashboardClient
{
    Task ReceiveNodeHealthUpdate(NodeHealthSnapshot snapshot);
    Task ReceiveLogEntry(StructuredLogEntry entry);
    Task ReceiveTaskUpdate(TaskStatusUpdate update);
    Task ReceiveMetricUpdate(MetricSnapshot snapshot);
    Task ReceiveAlert(SystemAlert alert);
    Task ReceiveAgentStepEvent(AgentStepEvent step);
    Task ReceiveTokenUsageUpdate(TokenUsageRecord record);
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

public record NodeHealthSnapshot
{
    public required string NodeId { get; init; }
    public required string DisplayName { get; init; }
    public required NodeHealthStatus Health { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record StructuredLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? TaskId { get; init; }
    public string? ModelId { get; init; }
    public string? CorrelationId { get; init; }
    public string? Exception { get; init; }
}

public record TaskStatusUpdate
{
    public required string TaskId { get; init; }
    public required string TaskType { get; init; }
    public required string Status { get; init; }
    public int TokensConsumed { get; init; }
    public double ElapsedMs { get; init; }
}

public record MetricSnapshot
{
    public required string MetricName { get; init; }
    public required double Value { get; init; }
    public string? NodeId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record SystemAlert
{
    public required string AlertId { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? NodeId { get; init; }
}
