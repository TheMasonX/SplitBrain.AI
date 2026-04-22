using Microsoft.Extensions.Options;
using Orchestrator.Core.Models;
using SdkClient = GitHub.Copilot.SDK.CopilotClient;
using SdkClientOptions = GitHub.Copilot.SDK.CopilotClientOptions;

namespace NodeClient.Copilot;

/// <summary>
/// GitHub Copilot inference client backed by <c>GitHub.Copilot.SDK</c>.
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

        var sdkOpts = new SdkClientOptions { LogLevel = "warning" };

        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            sdkOpts.GitHubToken = apiToken;
        }

        if (!string.IsNullOrWhiteSpace(options.Value.CliPath))
            sdkOpts.CliPath = options.Value.CliPath;

        if (!string.IsNullOrWhiteSpace(options.Value.CliUrl))
            sdkOpts.CliUrl = options.Value.CliUrl;

        _sdk = new SdkClient(sdkOpts);
    }

    public async Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model;

        await using var session = await _sdk.CreateSessionAsync(new GitHub.Copilot.SDK.SessionConfig
        {
            Model = model,
            OnPermissionRequest = GitHub.Copilot.SDK.PermissionHandler.ApproveAll,
            Streaming = request.Stream,
            InfiniteSessions = new GitHub.Copilot.SDK.InfiniteSessionConfig { Enabled = false },
        });

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? lastContent = null;

        using var sub = session.On(evt =>
        {
            switch (evt)
            {
                case GitHub.Copilot.SDK.AssistantMessageEvent msg:
                    lastContent = msg.Data.Content;
                    break;
                case GitHub.Copilot.SDK.SessionIdleEvent:
                    tcs.TrySetResult(lastContent ?? string.Empty);
                    break;
                case GitHub.Copilot.SDK.SessionErrorEvent err:
                    tcs.TrySetException(new InvalidOperationException(err.Data.Message));
                    break;
            }
        });

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        await session.SendAsync(new GitHub.Copilot.SDK.MessageOptions { Prompt = request.Prompt });
        return await tcs.Task;
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
