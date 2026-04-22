namespace NodeClient.Worker;

/// <summary>Configuration options for the Worker HTTP client.</summary>
public sealed class WorkerClientOptions
{
    public const string Section = "WorkerClient";

    /// <summary>Base URL of the remote Orchestrator.NodeWorker (e.g. http://192.168.1.100:5100).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5100";
    public int TimeoutSeconds { get; set; } = 30;
}
