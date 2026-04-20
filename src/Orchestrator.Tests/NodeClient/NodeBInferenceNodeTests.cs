using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Models;
using NodeClient.Ollama;

namespace Orchestrator.Tests.NodeClient;

public sealed class NodeBInferenceNodeTests
{
    private readonly IOllamaClient _client = Substitute.For<IOllamaClient>();
    private readonly ILogger<NodeBInferenceNode> _logger = Substitute.For<ILogger<NodeBInferenceNode>>();
    private readonly NodeBInferenceNode _sut;

    public NodeBInferenceNodeTests()
    {
        _sut = new NodeBInferenceNode(_client, _logger);
    }

    [Fact]
    public void NodeId_IsB()
    {
        _sut.NodeId.Should().Be("B");
    }

    [Fact]
    public void Capabilities_HasExpectedValues()
    {
        _sut.Capabilities.NodeId.Should().Be("B");
        _sut.Capabilities.VramMb.Should().Be(8192);
        _sut.Capabilities.SupportsStreaming.Should().BeTrue();
        _sut.Capabilities.Model.Should().Be("qwen2.5-coder:7b-instruct-q5_K_M");
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsHealthy()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var health = await _sut.GetHealthAsync();

        health.NodeId.Should().Be("B");
        health.Status.Should().Be(NodeStatus.Healthy);
        health.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_UsesPrimaryModel_WhenUseFallbackIsFalse()
    {
        const string expectedText = "deep review result";
        var request = new InferenceRequest { Prompt = "review deeply", UseFallback = false };

        _client.ExecuteAsync(
            Arg.Is<InferenceRequest>(r => r.Model == "qwen2.5-coder:7b-instruct-q5_K_M"),
            Arg.Any<CancellationToken>())
            .Returns(expectedText);

        var result = await _sut.ExecuteAsync(request);

        result.Text.Should().Be(expectedText);
        result.NodeId.Should().Be("B");
        result.Model.Should().Be("qwen2.5-coder:7b-instruct-q5_K_M");
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteAsync_UsesFallbackModel_WhenUseFallbackIsTrue()
    {
        const string expectedText = "fallback result";
        var request = new InferenceRequest { Prompt = "review", UseFallback = true };

        _client.ExecuteAsync(
            Arg.Is<InferenceRequest>(r => r.Model == "deepseek-coder:6.7b-instruct-q4_K_M"),
            Arg.Any<CancellationToken>())
            .Returns(expectedText);

        var result = await _sut.ExecuteAsync(request);

        result.Text.Should().Be(expectedText);
        result.Model.Should().Be("deepseek-coder:6.7b-instruct-q4_K_M");
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var request = new InferenceRequest { Prompt = "test", UseFallback = false };

        _client.ExecuteAsync(Arg.Any<InferenceRequest>(), cts.Token).Returns("ok");

        await _sut.ExecuteAsync(request, cts.Token);

        await _client.Received(1).ExecuteAsync(Arg.Any<InferenceRequest>(), cts.Token);
    }
}
