using Microsoft.Extensions.Options;
using Orchestrator.Core.Models;
using SdkClient = GitHub.Copilot.CopilotClient;
using SdkClientOptions = GitHub.Copilot.CopilotClientOptions;

namespace NodeClient.Copilot;

/// <summary>
/// GitHub Copilot inference client backed by <c>GitHub.Copilot</c>.
/// Each <see cref="ExecuteAsync"/> call opens a fresh single-turn session so
/// that concurrent requests are fully isolated.
///
/// Authentication priority:
///   1. <paramref name="apiToken"/> injected at construction (from Key Vault or env var)
///   2. GitHub CLI logged-in user (when <paramref name="apiToken"/> is null/empty)
/// </summary>
public sealed class CopilotClient : ICopilotClient, IAsyncDisposable
{
    private readonly SdkClient _sdk;
    private readonly string _model;

    /// <param name="apiToken">GitHub token with Copilot access — sourced from Key Vault or env var. Pass null to use the CLI logged-in user.</param>
    /// <param name="options">Bound option values.</param>
    public CopilotClient(string? apiToken, IOptions<CopilotClientOptions> options)
    {
        _model = options.Value.Model;

        var sdkOpts = new SdkClientOptions
        {
            LogLevel = GitHub.Copilot.CopilotLogLevel.Warning,
        };

        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            sdkOpts.GitHubToken = apiToken;
        }

        if (!string.IsNullOrWhiteSpace(options.Value.CliPath))
        {
            sdkOpts.Mode = GitHub.Copilot.CopilotClientMode.CopilotCli;
        }

        _sdk = new SdkClient(sdkOpts);
    }

    public async Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model;

        await using var session = await _sdk.CreateSessionAsync(new GitHub.Copilot.SessionConfig
        {
            Model = model,
            OnPermissionRequest = GitHub.Copilot.PermissionHandler.ApproveAll,
            Streaming = request.Stream,
            InfiniteSessions = new GitHub.Copilot.InfiniteSessionConfig { Enabled = false },
        });

        // SDK v1.0.0: SendAsync returns the response directly.
        using var reg = cancellationToken.Register(() => session.AbortAsync());
        return await session.SendAsync(
            new GitHub.Copilot.MessageOptions { Prompt = request.Prompt });
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pong = await _sdk.PingAsync();
            return pong is not null;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _sdk.StopAsync(); } catch { /* best-effort */ }
        await _sdk.DisposeAsync();
    }
}
