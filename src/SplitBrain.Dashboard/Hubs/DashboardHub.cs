using Microsoft.AspNetCore.SignalR;

namespace SplitBrain.Dashboard.Hubs;

/// <summary>
/// Strongly-typed SignalR hub for the Blazor dashboard.
/// Clients connect here to receive real-time updates; the server pushes
/// via IHubContext&lt;DashboardHub, IDashboardClient&gt;.
/// </summary>
public sealed class DashboardHub : Hub<IDashboardClient>
{
    public override async Task OnConnectedAsync()
    {
        // Client immediately joins the "dashboard" group so targeted broadcasts work
        await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard");
        await base.OnDisconnectedAsync(exception);
    }
}
