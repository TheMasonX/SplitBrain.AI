namespace Orchestrator.Agents.Sandbox;

/// <summary>
/// Runs a shell command in an isolated, restricted environment (§13).
/// </summary>
public interface ICodeSandbox
{
    /// <summary>
    /// Executes <paramref name="command"/> inside a sandboxed working directory.
    /// </summary>
    /// <param name="command">The command to run (e.g. "dotnet test").</param>
    /// <param name="workingDirectory">
    /// Absolute path to the restricted working directory.
    /// If null a temporary directory is created and deleted after the run.
    /// </param>
    /// <param name="cancellationToken">Propagated from the caller; also kills the process on cancel.</param>
    /// <returns>Exit code and combined stdout/stderr output.</returns>
    Task<SandboxResult> RunAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a sandboxed process run.</summary>
public sealed record SandboxResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public bool Success => ExitCode == 0 && !TimedOut;
}
