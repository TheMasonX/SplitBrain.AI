namespace Orchestrator.Mcp.WriteAccess;

public sealed class WriteAccessOptions
{
    public const string Section = "Mcp:WriteAccess";

    /// <summary>
    /// Disabled = no write operations allowed.
    /// PerCallEnable = caller must include enable_write: true per call.
    /// AlwaysEnabled = unrestricted writes (trust-all; use only on isolated dev machines).
    /// Default: Disabled — safe for shared or production environments.
    /// </summary>
    public WriteAccessMode Mode { get; init; } = WriteAccessMode.Disabled;

    /// <summary>
    /// When non-empty, restricts writes to only these tool names (in addition to Mode).
    /// Empty list = no extra restriction beyond Mode.
    /// </summary>
    public List<string> AllowedWriteTools { get; init; } = new();
}

public enum WriteAccessMode
{
    Disabled,
    PerCallEnable,
    AlwaysEnabled
}
