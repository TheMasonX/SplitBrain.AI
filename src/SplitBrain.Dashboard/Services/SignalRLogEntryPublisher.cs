using Microsoft.AspNetCore.SignalR;
using Orchestrator.Core.Interfaces;
using SplitBrain.Dashboard.Hubs;

namespace SplitBrain.Dashboard.Services;

/// <summary>
/// Publishes structured log entries to all connected dashboard clients via SignalR
/// and updates the in-process DashboardState for Blazor component subscriptions.
/// </summary>
public sealed class SignalRLogEntryPublisher : ILogEntryPublisher
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    private readonly DashboardState _state;

    public SignalRLogEntryPublisher(
        IHubContext<DashboardHub, IDashboardClient> hub,
        DashboardState state)
    {
        _hub = hub;
        _state = state;
    }

    public async Task PublishAsync(
        string level,
        string message,
        string? category,
        DateTimeOffset timestamp,
        CancellationToken ct = default)
    {
        var entry = new StructuredLogEntry
        {
            Level = level,
            Message = message,
            Timestamp = timestamp
        };

        _state.AddLogEntry(entry);
        await _hub.Clients.All.ReceiveLogEntry(entry);
    }
}
