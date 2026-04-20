using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Orchestrator.Agents.Sandbox;

/// <summary>
/// Runs commands in an isolated child process with a hard kill timeout (§13).
///
/// Safety guarantees:
///   • Process spawned with <c>UseShellExecute = false</c> — no shell injection
///   • Working directory restricted to a caller-supplied or temp path
///   • Killed unconditionally after <see cref="KillTimeoutSeconds"/> seconds
///   • Temp directory (when auto-created) deleted after the run
/// </summary>
public sealed class ProcessCodeSandbox : ICodeSandbox
{
    private const int KillTimeoutSeconds = 30;

    private readonly ILogger<ProcessCodeSandbox> _logger;

    public ProcessCodeSandbox(ILogger<ProcessCodeSandbox> logger)
    {
        _logger = logger;
    }

    public async Task<SandboxResult> RunAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var tempDir = workingDirectory is null ? CreateTempDirectory() : null;
        var runDir  = workingDirectory ?? tempDir!;

        try
        {
            return await ExecuteAsync(command, runDir, cancellationToken);
        }
        finally
        {
            if (tempDir is not null)
                TryDeleteDirectory(tempDir);
        }
    }

    // -----------------------------------------------------------------------

    private async Task<SandboxResult> ExecuteAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // Split "dotnet test --no-build" → ("dotnet", "test --no-build")
        var (fileName, arguments) = SplitCommand(command);

        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            WorkingDirectory       = workingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        _logger.LogInformation(
            "Sandbox: executing '{Command}' in '{Dir}' (timeout {Timeout}s)",
            command, workingDirectory, KillTimeoutSeconds);

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) outputBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Combined kill token: caller cancel OR 30 s hard timeout
        using var killCts   = new CancellationTokenSource(TimeSpan.FromSeconds(KillTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, killCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);

            var timedOut = killCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            if (timedOut)
                _logger.LogWarning("Sandbox: process killed after {Timeout}s timeout", KillTimeoutSeconds);

            return new SandboxResult
            {
                ExitCode = -1,
                Output   = outputBuilder.ToString(),
                TimedOut = timedOut
            };
        }

        var output = outputBuilder.ToString();
        _logger.LogInformation(
            "Sandbox: exited with code {Code}, output length {Len}",
            process.ExitCode, output.Length);

        return new SandboxResult
        {
            ExitCode = process.ExitCode,
            Output   = output
        };
    }

    // -----------------------------------------------------------------------

    private static (string fileName, string arguments) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        var space   = trimmed.IndexOf(' ');
        return space < 0
            ? (trimmed, string.Empty)
            : (trimmed[..space], trimmed[(space + 1)..]);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "splitbrain-sandbox-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    private void TryDeleteDirectory(string path)
    {
        try   { Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Sandbox: failed to delete temp dir '{Path}'", path); }
    }

    private void TryKillProcess(Process process)
    {
        try   { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Sandbox: failed to kill process"); }
    }
}
