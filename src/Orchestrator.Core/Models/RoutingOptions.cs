namespace Orchestrator.Core.Models;

/// <summary>
/// Controls how the routing service falls back across nodes when a node is
/// unreachable or its queue is full.
/// Bind from appsettings section "Routing".
/// </summary>
public sealed class RoutingOptions
{
    public const string Section = "Routing";

    /// <summary>
    /// Maps a node ID to an ordered list of fallback node IDs to try when that
    /// node is unreachable (connectivity failure).
    /// Example: { "B": ["C", "A"], "C": ["A"] }
    /// </summary>
    public Dictionary<string, List<string>> FallbackChains { get; set; } = new()
    {
        ["B"] = ["C", "A"],
        ["C"] = ["A"],
        ["A"] = []
    };
}
