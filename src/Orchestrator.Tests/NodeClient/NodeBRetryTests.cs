using Microsoft.Extensions.Logging;
using Orchestrator.Core.Models;
using NodeClient.Ollama;

namespace Orchestrator.Tests.NodeClient;

/// <summary>
/// Tests for §12 fault-tolerance: Node B retries once with the fallback model on primary failure.
/// </summary>
public sealed class NodeBRetryTests
{
    private readonly IOllamaClient _client = Substitute.For<IOllamaClient>();
    private readonly ILogger<NodeBInferenceNode> _logger = Substitute.For<ILogger<NodeBInferenceNode>>();
    private readonly NodeBInferenceNode _sut;

    public NodeBRetryTests()
    {
        _sut = new NodeBInferenceNode(_client, _logger);
    }

    [Test]
    public async Task ExecuteAsync_PrimaryFails_RetiesWithFallbackModel()
    {
        var request = new InferenceRequest { Prompt = "analyse this", UseFallback = false };

        // Primary call throws, fallback call succeeds
        _client.ExecuteAsync(
                Arg.Is<InferenceRequest>(r => r.Model == "qwen2.5-coder:7b-instruct-q5_K_M"),
                Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("CUDA OOM"));

        _client.ExecuteAsync(
                Arg.Is<InferenceRequest>(r => r.Model == "deepseek-coder:6.7b-instruct-q4_K_M"),
                Arg.Any<CancellationToken>())
            .Returns("fallback response");

        var result = await _sut.ExecuteAsync(request);

        result.Text.Should().Be("fallback response");
        result.Model.Should().Be("deepseek-coder:6.7b-instruct-q4_K_M");
        result.NodeId.Should().Be("B");
    }

    [Test]
    public async Task ExecuteAsync_PrimaryFails_FallbackAlsoFails_Throws()
    {
        var request = new InferenceRequest { Prompt = "analyse this", UseFallback = false };

        _client.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("total failure"));

        var act = async () => await _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteAsync_OperationCanceled_DoesNotRetry()
    {
        var request = new InferenceRequest { Prompt = "analyse this", UseFallback = false };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _client.ExecuteAsync(
                Arg.Is<InferenceRequest>(r => r.Model == "qwen2.5-coder:7b-instruct-q5_K_M"),
                Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new OperationCanceledException("cancelled"));

        var act = async () => await _sut.ExecuteAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Fallback must never be called
        await _client.DidNotReceive().ExecuteAsync(
            Arg.Is<InferenceRequest>(r => r.Model == "deepseek-coder:6.7b-instruct-q4_K_M"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenUseFallbackTrue_DoesNotRetryOnFailure()
    {
        // If UseFallback is already true the request is already the retry — no second chance
        var request = new InferenceRequest { Prompt = "analyse this", UseFallback = true };

        _client.ExecuteAsync(
                Arg.Is<InferenceRequest>(r => r.Model == "deepseek-coder:6.7b-instruct-q4_K_M"),
                Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("fallback also dead"));

        var act = async () => await _sut.ExecuteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Called exactly once — no second retry
        await _client.Received(1).ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>());
    }
}
