using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Orchestrator.Core.Models;
using Orchestrator.Core.Serialization;
using Orchestrator.Core.Validation;

namespace Orchestrator.Mcp.Tools;

[McpServerToolType]
public sealed class RunTestsTool
{
    [McpServerTool(Name = "run_tests"), Description("Runs the test suite for a project and returns pass/fail results.")]
    public async Task<string> RunTestsAsync(
        [Description("Absolute path to the .csproj or solution file to test")] string projectPath,
        [Description("Optional test filter expression (e.g. FullyQualifiedName~MyTest)")] string filter = "",
        [Description("Allowed root directory — path is rejected if outside this scope")] string allowedRoot = "",
        [Description("Timeout in seconds for the full test run (1–120)")] int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        var request = new RunTestsRequest
        {
            ProjectPath = projectPath,
            TestFilter = string.IsNullOrWhiteSpace(filter) ? null : filter,
            TimeoutSeconds = timeoutSeconds
        };

        request.ValidateOrThrow(new RunTestsRequestValidator());

        // Security: if an allowedRoot is specified, enforce it
        if (!string.IsNullOrWhiteSpace(allowedRoot))
        {
            var fullPath = Path.GetFullPath(projectPath);
            var fullRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Serialize(new RunTestsResponse
                {
                    Summary = new TestSummary(),
                    Failures = [],
                    Error = new McpError
                    {
                        Code = "PATH_VIOLATION",
                        Message = $"projectPath '{projectPath}' is outside the allowed root '{allowedRoot}'",
                        Retryable = false
                    },
                    Meta = new Meta { TaskId = Guid.NewGuid().ToString("N"), Node = "local" }
                }, JsonConfig.Default);
            }
        }

        var args = $"test \"{projectPath}\" --no-build --logger console;verbosity=normal";
        if (!string.IsNullOrWhiteSpace(filter))
            args += $" --filter \"{filter}\"";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (exitCode, stdout, stderr) = await RunProcessAsync("dotnet", args, cts.Token);
        sw.Stop();

        var (summary, failures) = ParseDotnetTestOutput(stdout + "\n" + stderr, (int)sw.ElapsedMilliseconds);

        McpError? error = null;
        if (exitCode != 0 && failures.Count == 0)
        {
            error = new McpError
            {
                Code = "TEST_RUN_FAILED",
                Message = stderr.Length > 0 ? stderr[..Math.Min(500, stderr.Length)] : "dotnet test exited with non-zero code",
                Retryable = false
            };
        }

        var response = new RunTestsResponse
        {
            Summary = summary,
            Failures = failures,
            Error = error,
            Meta = new Meta
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Node = "local",
                LatencyMs = (int)sw.ElapsedMilliseconds
            }
        };

        return JsonSerializer.Serialize(response, JsonConfig.Default);
    }

    // ---------------------------------------------------------------------------
    // Output parsing
    // ---------------------------------------------------------------------------

    private static (TestSummary Summary, List<TestFailure> Failures) ParseDotnetTestOutput(
        string output, int durationMs)
    {
        var failures = new List<TestFailure>();
        int passed = 0, failed = 0, skipped = 0;

        // Dotnet test summary line: "Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 1 s"
        var summaryMatch = Regex.Match(output,
            @"(?:Passed|Failed)!.*?Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)",
            RegexOptions.IgnoreCase);

        if (summaryMatch.Success)
        {
            failed  = int.Parse(summaryMatch.Groups[1].Value);
            passed  = int.Parse(summaryMatch.Groups[2].Value);
            skipped = int.Parse(summaryMatch.Groups[3].Value);
        }

        // Parse individual failure blocks
        var failureBlocks = Regex.Matches(output,
            @"Failed\s+(?<name>[^\r\n]+)\r?\n(?<body>.*?)(?=\r?\nFailed |\r?\nPassed!|\r?\n\s*\n\s*\n|$)",
            RegexOptions.Singleline);

        foreach (Match m in failureBlocks)
        {
            var body = m.Groups["body"].Value.Trim();
            var msgMatch = Regex.Match(body, @"Error Message:\s*(?<msg>[^\r\n]+)");
            var stackMatch = Regex.Match(body, @"Stack Trace:\s*(?<st>[\s\S]+)");
            failures.Add(new TestFailure
            {
                TestName  = m.Groups["name"].Value.Trim(),
                Message   = msgMatch.Success ? msgMatch.Groups["msg"].Value.Trim() : body[..Math.Min(200, body.Length)],
                StackTrace = stackMatch.Success ? stackMatch.Groups["st"].Value.Trim() : null
            });
        }

        var summary = new TestSummary
        {
            Total      = passed + failed + skipped,
            Passed     = passed,
            Failed     = failed,
            Skipped    = skipped,
            DurationMs = durationMs
        };

        return (summary, failures);
    }

    // ---------------------------------------------------------------------------
    // Process helper
    // ---------------------------------------------------------------------------

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string executable, string arguments, CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
