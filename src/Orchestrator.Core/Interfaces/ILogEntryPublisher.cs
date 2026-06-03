namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Abstraction for publishing structured log entries to connected clients.
/// Implemented by the Blazor dashboard via SignalR. A no-op default is
/// registered in non-dashboard deployments.
/// </summary>
public interface ILogEntryPublisher
{
    Task PublishAsync(string level, string message, string? category, DateTimeOffset timestamp, CancellationToken ct = default);
}

/// <summary>No-op publisher used when no dashboard is present.</summary>
public sealed class NullLogEntryPublisher : ILogEntryPublisher
{
    public Task PublishAsync(string level, string message, string? category, DateTimeOffset timestamp, CancellationToken ct = default)
        => Task.CompletedTask;
}
