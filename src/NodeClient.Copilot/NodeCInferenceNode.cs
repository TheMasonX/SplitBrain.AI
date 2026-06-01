using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;
using System.Diagnostics;

namespace NodeClient.Copilot;

/// <summary>
/// Node C — GitHub Copilot API inference node.
/// </summary>
public sealed class NodeCInferenceNode : InferenceNodeBase
{
    private readonly ICopilotClient _client;
    private readonly string _model;
    private readonly ILogger<NodeCInferenceNode> _logger;

    public override string NodeId => "C";
    public override NodeProviderType Provider => NodeProviderType.CopilotSdk;

    public override NodeCapabilities Capabilities { get; }

    public NodeCInferenceNode(ICopilotClient client, IOptions<CopilotClientOptions> options, ILogger<NodeCInferenceNode> logger)
    {
        _client = client;
        _model = options.Value.Model;
        _logger = logger;

        Capabilities = new NodeCapabilities
        {
            NodeId = "C",
            Model = _model,
            VramMb = 0,
            SupportsStreaming = true
        };
    }

    public override async Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var req = request with { Model = _model };
        _logger.LogDebug("Node C executing model={Model} promptLen={Len}", _model, request.Prompt.Length);
        var sw = Stopwatch.StartNew();
        var text = await _client.ExecuteAsync(req, cancellationToken);
        sw.Stop();
        _logger.LogInformation("Node C completed latencyMs={Latency} tokensOut={Tokens}", sw.ElapsedMilliseconds, text.Length / 4);

        return new InferenceResult
        {
            Text = text,
            NodeId = NodeId,
            Model = _model,
            LatencyMs = (int)sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// Override to include AvailableModels and ErrorMessage in health status.
    /// </summary>
    public override async Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        NodeHealthStatus status;
        try
        {
            var isHealthy = await CheckHealthCoreAsync(cancellationToken);
            status = new NodeHealthStatus
            {
                State = isHealthy ? HealthState.Healthy : HealthState.Degraded,
                LastChecked = DateTimeOffset.UtcNow,
                AvailableModels = [_model]
            };
        }
        catch (Exception ex)
        {
            status = new NodeHealthStatus
            {
                State = HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message
            };
        }
        _health = status;
        return status;
    }

    protected override Task<bool> CheckHealthCoreAsync(CancellationToken cancellationToken)
        => _client.IsHealthyAsync(cancellationToken);

    public override Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> result = [new ModelInfo { ModelId = _model }];
        return Task.FromResult(result);
    }

    // ---------------------------------------------------------------------------
    // Factory helper
    // ---------------------------------------------------------------------------

    public static async Task<NodeCInferenceNode> CreateAsync(
        CopilotClientOptions options,
        ILogger<NodeCInferenceNode> logger,
        CancellationToken cancellationToken = default)
    {
        var token = await ResolveApiTokenAsync(options, logger, cancellationToken);
        var client = new CopilotClient(token, Options.Create(options));
        return new NodeCInferenceNode(client, Options.Create(options), logger);
    }

    private static async Task<string> ResolveApiTokenAsync(
        CopilotClientOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.KeyVaultUri))
        {
            logger.LogInformation("Node C: resolving API token from Key Vault {Uri}", options.KeyVaultUri);
            try
            {
                var credential = new DefaultAzureCredential();
                var kvClient = new SecretClient(new Uri(options.KeyVaultUri), credential);
                var secret = await kvClient.GetSecretAsync(options.KeyVaultSecretName, cancellationToken: cancellationToken);
                logger.LogInformation("Node C: API token retrieved from Key Vault");
                return secret.Value.Value;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Node C: Key Vault retrieval failed — falling back to environment variable");
            }
        }

        var envToken = Environment.GetEnvironmentVariable("COPILOT_API_KEY");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            logger.LogInformation("Node C: API token sourced from COPILOT_API_KEY environment variable");
            return envToken;
        }

        var ghToken = TryResolveGhCliToken(logger);
        if (!string.IsNullOrWhiteSpace(ghToken))
            return ghToken;

        throw new InvalidOperationException(
            "Node C (GitHub Copilot) API token could not be resolved. " +
            "Configure CopilotNode:KeyVaultUri, set COPILOT_API_KEY, or run `gh auth login`.");
    }

    private static string? TryResolveGhCliToken(ILogger logger)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            if (!process.Start())
            {
                logger.LogWarning("Node C: gh CLI process failed to start — is GitHub CLI installed?");
                return null;
            }

            // Read stdout/stderr asynchronously BEFORE WaitForExit to avoid deadlocks
            // when the OS pipe buffer fills up on either stream.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                logger.LogWarning("Node C: gh CLI timed out after 5 seconds — killing process");
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return null;
            }

            var token = stdoutTask.GetAwaiter().GetResult().Trim();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(token))
            {
                logger.LogInformation("Node C: API token sourced from gh CLI");
                return token;
            }

            if (!string.IsNullOrWhiteSpace(stderr))
                logger.LogWarning("Node C: gh auth token failed (exit={ExitCode}): {StdErr}", process.ExitCode, stderr.Trim());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("Node C: gh CLI not found on PATH — install GitHub CLI or set COPILOT_API_KEY");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Node C: gh CLI token resolution failed");
        }

        return null;
    }
}
