using Microsoft.Extensions.Logging;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NodeClient.Ollama;

/// <summary>
/// Node B — GTX 1080 8 GB (Pascal).
/// Primary: qwen2.5-coder:7b-instruct-q5_K_M
/// Fallback: deepseek-coder:6.7b-instruct-q4_K_M
/// Flash attention DISABLED (Pascal instability).
/// Single-parallel enforced via Ollama env vars.
/// </summary>
public sealed class NodeBInferenceNode : IInferenceNode
{
    private const string PrimaryModel  = "qwen2.5-coder:7b-instruct-q5_K_M";
    private const string FallbackModel = "deepseek-coder:6.7b-instruct-q4_K_M";

    private readonly IOllamaClient _client;
    private readonly ILogger<NodeBInferenceNode> _logger;
    private NodeHealthStatus _health = new() { State = HealthState.Unavailable, LastChecked = DateTimeOffset.MinValue };

    public string NodeId => "B";
    public NodeProviderType Provider => NodeProviderType.Ollama;
    public NodeHealthStatus Health => _health;

    public NodeCapabilities Capabilities { get; } = new()
    {
        NodeId = "B",
        Model = PrimaryModel,
        VramMb = 8192,
        SupportsStreaming = true
    };

    public NodeBInferenceNode(IOllamaClient client, ILogger<NodeBInferenceNode> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var model = request.UseFallback ? FallbackModel : PrimaryModel;
        var req = request with { Model = model };
        _logger.LogDebug("Node B executing model={Model} fallback={Fallback} promptLen={Len}",
            model, request.UseFallback, request.Prompt.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            var text = await _client.ExecuteAsync(req, cancellationToken);
            sw.Stop();
            _logger.LogInformation("Node B completed latencyMs={Latency} tokensOut={Tokens}",
                sw.ElapsedMilliseconds, text.Length / 4);

            return new InferenceResult
            {
                Text      = text,
                NodeId    = NodeId,
                Model     = model,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !request.UseFallback && !IsConnectivityException(ex))
        {
            sw.Restart();
            _logger.LogWarning(ex,
                "Node B primary model {PrimaryModel} failed — retrying with fallback {FallbackModel}",
                PrimaryModel, FallbackModel);

            var fallbackReq = request with { Model = FallbackModel, UseFallback = true };
            var fallbackText = await _client.ExecuteAsync(fallbackReq, cancellationToken);
            sw.Stop();
            _logger.LogInformation("Node B fallback completed latencyMs={Latency}", sw.ElapsedMilliseconds);

            return new InferenceResult
            {
                Text      = fallbackText,
                NodeId    = NodeId,
                Model     = FallbackModel,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
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
                LastChecked = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            status = new NodeHealthStatus
            {
                State = HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow
            };
        }
        _health = status;
        return status;
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> result =
        [
            new ModelInfo { ModelId = PrimaryModel },
            new ModelInfo { ModelId = FallbackModel }
        ];
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static bool IsConnectivityException(Exception ex) =>
        ex is System.Net.Http.HttpRequestException httpEx &&
        (httpEx.InnerException is System.Net.Sockets.SocketException ||
         httpEx.InnerException is TaskCanceledException);
}
