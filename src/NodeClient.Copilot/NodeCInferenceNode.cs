using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NodeClient.Copilot;

/// <summary>
/// Node C — GitHub Copilot API inference node.
/// </summary>
public sealed class NodeCInferenceNode : IInferenceNode
{
    private readonly ICopilotClient _client;
    private readonly string _model;
    private readonly ILogger<NodeCInferenceNode> _logger;
    private NodeHealthStatus _health = new() { State = HealthState.Unavailable, LastChecked = DateTimeOffset.MinValue };

    public string NodeId => "C";
    public NodeProviderType Provider => NodeProviderType.CopilotSdk;
    public NodeHealthStatus Health => _health;

    public NodeCapabilities Capabilities { get; }

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

    public async Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
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

    public async IAsyncEnumerable<InferenceChunk> StreamAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(request, cancellationToken);
        yield return new InferenceChunk
        {
            Content = result.Text,
            IsFinal = true,
            FinalResult = new Orchestrator.Core.Models.InferenceResult
            {
                Text = result.Text,
                NodeId = result.NodeId,
                Model = result.Model,
                LatencyMs = result.LatencyMs
            }
        };
    }

    public async Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        NodeHealthStatus status;
        try
        {
            var isHealthy = await _client.IsHealthyAsync(cancellationToken);
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

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> result = [new ModelInfo { ModelId = _model }];
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
            process.Start();
            var token = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(token))
            {
                logger.LogInformation("Node C: API token sourced from gh CLI");
                return token;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Node C: gh CLI token resolution failed");
        }

        return null;
    }
}
