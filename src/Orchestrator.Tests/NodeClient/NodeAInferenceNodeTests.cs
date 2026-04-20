using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Orchestrator.Core.Models;
using NodeClient.Ollama;
using Orchestrator.Core.Enums;

namespace Orchestrator.Tests.NodeClient;

public sealed class NodeAInferenceNodeTests
{
    private readonly IOllamaClient _client = Substitute.For<IOllamaClient>();
    private readonly ILogger<NodeAInferenceNode> _logger = Substitute.For<ILogger<NodeAInferenceNode>>();
    private readonly NodeAInferenceNode _sut;

    public NodeAInferenceNodeTests()
    {
        _sut = new NodeAInferenceNode(_client, _logger);
    }

    [Fact]
    public void NodeId_IsA()
    {
        _sut.NodeId.Should().Be("A");
    }

    [Fact]
    public void Capabilities_HasExpectedNodeId()
    {
        _sut.Capabilities.NodeId.Should().Be("A");
        _sut.Capabilities.VramMb.Should().Be(8192);
        _sut.Capabilities.SupportsStreaming.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsHealthy()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var health = await _sut.GetHealthAsync();

        health.NodeId.Should().Be("A");
        health.Status.Should().Be(NodeStatus.Healthy);
        health.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_OverridesModelAndReturnsResult()
    {
        const string expectedText = "review result";
        var request = new InferenceRequest { Prompt = "review", Model = "some-other-model" };

        _client.ExecuteAsync(Arg.Is<InferenceRequest>(r => r.Model == "qwen2.5-coder:7b-instruct-q4_K_M"), Arg.Any<CancellationToken>())
               .Returns(expectedText);

        var result = await _sut.ExecuteAsync(request);

        result.Text.Should().Be(expectedText);
        result.NodeId.Should().Be("A");
        result.Model.Should().Be("qwen2.5-coder:7b-instruct-q4_K_M");
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }
}
