using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Diagnostics;

namespace NodeClient.Copilot;

/// <summary>
/// Node C — GitHub Copilot API inference node.
///
/// API key resolution order (enterprise-safe, no plaintext in config):
///   1. Azure Key Vault  (when <c>CopilotNode:KeyVaultUri</c> is configured)
///   2. Environment variable  COPILOT_API_KEY
///
/// <c>DefaultAzureCredential</c> is used for Key Vault access, which supports
/// managed identity, workload identity, Azure CLI, and Visual Studio — suitable
/// for both cloud deployments and enterprise developer machines without storing
/// credentials in source-controlled files.
/// </summary>
public sealed class NodeCInferenceNode : IInferenceNode
{
    private readonly ICopilotClient _client;
    private readonly string _model;
    private readonly ILogger<NodeCInferenceNode> _logger;

    public string NodeId => "C";

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
            VramMb = 0,           // cloud — no local VRAM constraint
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

    public async Task<NodeHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        NodeStatus status;
        try
        {
            status = await _client.IsHealthyAsync(cancellationToken)
                ? NodeStatus.Healthy
                : NodeStatus.Degraded;
        }
        catch
        {
            status = NodeStatus.Unavailable;
        }

        return new NodeHealth
        {
            NodeId = NodeId,
            Status = status,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    // ---------------------------------------------------------------------------
    // Factory helper — resolves API token from Key Vault or environment variable
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="NodeCInferenceNode"/> after securely resolving the
    /// GitHub Copilot API token.  Call once at startup from DI registration.
    /// </summary>
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
        // 1. Azure Key Vault — enterprise preferred path
        if (!string.IsNullOrWhiteSpace(options.KeyVaultUri))
        {
            logger.LogInformation(
                "Node C: resolving API token from Key Vault {Uri}, secret={Secret}",
                options.KeyVaultUri, options.KeyVaultSecretName);
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

        // 2. Environment variable — acceptable for developer machines
        var envToken = Environment.GetEnvironmentVariable("COPILOT_API_KEY");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            logger.LogInformation("Node C: API token sourced from COPILOT_API_KEY environment variable");
            return envToken;
        }

        // 3. gh CLI OAuth token — used when the developer is logged in via `gh auth login`
        var ghToken = TryResolveGhCliToken(logger);
        if (!string.IsNullOrWhiteSpace(ghToken))
            return ghToken;

        throw new InvalidOperationException(
            "Node C (GitHub Copilot) API token could not be resolved. " +
            "Configure CopilotNode:KeyVaultUri (recommended for enterprise), " +
            "set the COPILOT_API_KEY environment variable, " +
            "or log in with the GitHub CLI (`gh auth login`).");
    }

    private static string? TryResolveGhCliToken(ILogger logger)
    {
        // gh CLI token via `gh auth token` — works if the user is logged in
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
                logger.LogInformation("Node C: API token sourced from gh CLI (`gh auth token`)");
                return token;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Node C: gh CLI token resolution failed (gh not installed or not logged in)");
        }

        return null;
    }
}
