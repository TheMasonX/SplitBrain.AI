namespace Orchestrator.Core.Configuration;

/// <summary>
/// Provider type determines which adapter creates the IInferenceNode.
/// Extend this enum when adding new provider integrations.
/// </summary>
public enum NodeProviderType
{
    Ollama,
    CopilotSdk,
}

/// <summary>
/// Role governs hard routing rules. Fast nodes handle latency-sensitive work.
/// Deep nodes handle complex reasoning. Hybrid can do both. Standby is reserve.
/// </summary>
public enum NodeRole
{
    Fast,
    Deep,
    Hybrid,
    Standby
}

/// <summary>
/// Complete configuration for a single inference node.
/// Serialized to/from nodes.json. Immutable record for thread safety.
/// </summary>
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
}

/// <summary>
/// Ollama-specific settings per node.
/// </summary>
public record OllamaProviderConfig
{
    public required string Host { get; init; }
    public int Port { get; init; } = 11434;
    public string BaseUrl => $"http://{Host}:{Port}";
    public int NumParallel { get; init; } = 2;
    public int MaxLoadedModels { get; init; } = 2;
    public bool FlashAttention { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 10;
    /// <summary>
    /// Static VRAM capacity. Ollama /api/ps does not expose total GPU memory,
    /// so total VRAM must come from config for utilization calculations.
    /// </summary>
    public long GpuVramTotalMB { get; init; }
}

/// <summary>
/// GitHub Copilot SDK settings.
/// Auth resolution: Azure Key Vault → env var COPILOT_API_KEY → GitHub CLI.
/// </summary>
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

/// <summary>Root object deserialized from nodes.json.</summary>
public sealed class NodeTopologyConfig
{
    public List<NodeConfiguration> Nodes { get; set; } = [];
}
