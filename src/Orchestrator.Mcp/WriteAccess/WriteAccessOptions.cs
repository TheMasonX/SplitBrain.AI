namespace Orchestrator.Mcp.WriteAccess;

public sealed class WriteAccessOptions
{
    public const string Section = "Mcp:WriteAccess";

    /// <summary>Disabled = no write ops; PerCallEnable = requires enable_write param; AlwaysEnabled = trust-all.</summary>
    public WriteAccessMode Mode { get; init; } = WriteAccessMode.Disabled;

    /// <summary>When non-empty, only these tool names are allowed to write (in addition to Mode check).</summary>
    public List<string> AllowedWriteTools { get; init; } = new();
}

public enum WriteAccessMode { Disabled, PerCallEnable, AlwaysEnabled }
