namespace Orchestrator.Core.Models;

public enum ModelFamily
{
    Qwen,
    DeepSeek,
    Nomic,
    Copilot,
}

public record ModelDefinition
{
    public required string ModelId { get; init; }
    public required string DisplayName { get; init; }
    public required ModelFamily Family { get; init; }
    public required Enums.TaskType PrimaryCapability { get; init; }
    public List<Enums.TaskType> SecondaryCapabilities { get; init; } = [];
    public string? QuantizationLevel { get; init; }
    public int ContextWindow { get; init; } = 8192;
    public int EstimatedVramMB { get; init; }
    public List<string> PreferredNodeIds { get; init; } = [];
}

public record FallbackChainConfig
{
    public required Enums.TaskType TaskType { get; init; }
    public required List<FallbackStep> Steps { get; init; }
}

public record FallbackStep
{
    public required string ModelId { get; init; }
    public List<string>? PreferredNodeIds { get; init; }
    public int TimeoutOverrideMs { get; init; } = 0;
}
