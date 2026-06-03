using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NodeClient.Ollama;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Tests.NodeClient;

public sealed class OllamaInferenceNodeTests
{
    private readonly IOllamaClient _client = Substitute.For<IOllamaClient>();
    private readonly ILogger<OllamaInferenceNode> _logger = Substitute.For<ILogger<OllamaInferenceNode>>();
    private readonly OllamaProviderConfig _config;
    private readonly OllamaInferenceNode _sut;

    public OllamaInferenceNodeTests()
    {
        _config = new OllamaProviderConfig
        {
            Host = "localhost",
            Port = 11434,
            TimeoutSeconds = 10,
            GpuVramTotalMB = 8192
        };
        _sut = new OllamaInferenceNode("test-node", _config, _client, _logger);
    }

    [Test]
    public void NodeId_MatchesConstructorArgument()
    {
        _sut.NodeId.Should().Be("test-node");
    }

    [Test]
    public void Provider_IsOllama()
    {
        _sut.Provider.Should().Be(Orchestrator.Core.Configuration.NodeProviderType.Ollama);
    }

    [Test]
    public void Capabilities_ReflectConfig()
    {
        _sut.Capabilities.NodeId.Should().Be("test-node");
        _sut.Capabilities.VramMb.Should().Be(8192);
        _sut.Capabilities.SupportsStreaming.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_DelegatesToClient()
    {
        var request = new InferenceRequest { Prompt = "hello", Model = "llama3:latest" };
        _client.ExecuteAsync(request, Arg.Any<CancellationToken>()).Returns("world");

        var result = await _sut.ExecuteAsync(request);

        result.Text.Should().Be("world");
        result.NodeId.Should().Be("test-node");
        result.Model.Should().Be("llama3:latest");
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        var request = new InferenceRequest { Prompt = "hello", Model = "llama3" };
        using var cts = new CancellationTokenSource();
        _client.ExecuteAsync(request, cts.Token).Returns("ok");

        await _sut.ExecuteAsync(request, cts.Token);

        await _client.Received(1).ExecuteAsync(request, cts.Token);
    }

    [Test]
    public async Task StreamAsync_ReturnsSingleFinalChunk()
    {
        var request = new InferenceRequest { Prompt = "stream me", Model = "llama3" };
        _client.ExecuteAsync(request, Arg.Any<CancellationToken>()).Returns("streamed");

        var chunks = new List<InferenceChunk>();
        await foreach (var chunk in _sut.StreamAsync(request))
            chunks.Add(chunk);

        chunks.Should().HaveCount(1);
        chunks[0].IsFinal.Should().BeTrue();
        chunks[0].Content.Should().Be("streamed");
        chunks[0].FinalResult.Should().NotBeNull();
    }

    [Test]
    public async Task GetHealthAsync_ReturnsHealthy_WhenClientIsHealthy()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Healthy);
        health.LastChecked.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        health.VramTotalMB.Should().Be(8192);
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenClientUnreachable()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false);

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenClientThrows()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>())
               .ThrowsAsync(new HttpRequestException("connection refused"));

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
        health.ErrorMessage.Should().Contain("connection refused");
    }

    [Test]
    public async Task ListModelsAsync_ReturnsEmptyList()
    {
        var models = await _sut.ListModelsAsync();

        models.Should().BeEmpty();
    }

    [Test]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var act = async () => await _sut.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}
