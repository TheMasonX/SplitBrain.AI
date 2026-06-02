namespace Orchestrator.Core.Configuration;

public enum NodeProviderType { Ollama, CopilotSdk, Worker, LlamaCpp }
public enum NodeRole { Fast, Deep, Hybrid, Standby }

public record NodeConfiguration
{
    public required string NodeId { get; init; }
    public required string DisplayName { get; init; }
    public required NodeProviderType Provider { get; init; }
    public required NodeRole Role { get; init; }
    public int Priority { get; init; } = 100;
    public int MaxConcurrentRequests { get; init; } = 2;
    public List<string> Tags { get; init; } = [];
    public bool Enabled { get; init; } = true;
    public int HealthCheckIntervalMs { get; init; } = 2000;
    public OllamaProviderConfig? Ollama { get; init; }
    public CopilotProviderConfig? Copilot { get; init; }
    public WorkerProviderConfig? Worker { get; init; }
    public LlamaCppProviderConfig? LlamaCpp { get; init; }
}

public record OllamaProviderConfig
{
    public required string Host { get; init; }
    public int Port { get; init; } = 11434;
    public string BaseUrl => $"http://{Host}:{Port}";
    public int NumParallel { get; init; } = 2;
    public int MaxLoadedModels { get; init; } = 2;
    public bool FlashAttention { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 10;
    public long GpuVramTotalMB { get; init; }
}

public record CopilotProviderConfig
{
    public string? CliPath { get; init; }
    public string? CliUrl { get; init; }
    public bool UseStdio { get; init; } = true;
    public string DefaultModel { get; init; } = "gpt-4o";
    public string? KeyVaultUri { get; init; }
    public string? KeyVaultSecretName { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

public record WorkerProviderConfig
{
    public required string BaseUrl { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public long GpuVramTotalMB { get; init; }
    public string DefaultModel { get; init; } = string.Empty;
}

public record LlamaCppProviderConfig
{
    public required string Host { get; init; }
    public int Port { get; init; } = 8080;
    public string BaseUrl => $"http://{Host}:{Port}";
    public int TimeoutSeconds { get; init; } = 180;
    public string ModelLabel { get; init; } = "llama-cpp-default";
    public long GpuVramTotalMB { get; init; } = 8192;
}

public sealed class NodeTopologyConfig
{
    public List<NodeConfiguration> Nodes { get; set; } = [];
}
