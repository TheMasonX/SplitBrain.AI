namespace SplitBrain.Dashboard.Components.Pages;

/// <summary>Data point for the latency-over-time ApexChart series.</summary>
internal sealed record LatencyPoint(DateTimeOffset Time, double LatencyMs);

/// <summary>Data point for the tokens/second over-time ApexChart series.</summary>
internal sealed record ThroughputPoint(DateTimeOffset Time, double TokensPerSecond);

/// <summary>Groups chart data points by node for multi-series charts.</summary>
internal sealed class NodeSeries<T>
{
    public string NodeId { get; init; } = "";
    public List<T> Points { get; init; } = [];
}

/// <summary>
/// Single-value data point used by the VRAM radial-bar gauge on the Home page.
/// <see cref="Pct"/> is 0–100 representing percentage of VRAM loaded.
/// </summary>
internal sealed record VramGaugePoint(decimal Pct);
