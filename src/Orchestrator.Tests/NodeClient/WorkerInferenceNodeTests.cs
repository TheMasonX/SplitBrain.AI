using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NodeClient.Worker;
using NUnit.Framework;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Tests.NodeClient;

public sealed class WorkerInferenceNodeTests
{
    private readonly IWorkerClient _client = Substitute.For<IWorkerClient>();
    private readonly ILogger<WorkerInferenceNode> _logger = Substitute.For<ILogger<WorkerInferenceNode>>();
    private readonly WorkerProviderConfig _config = new()
    {
        BaseUrl = "http://192.168.1.50:5100",
        TimeoutSeconds = 30,
        GpuVramTotalMB = 8192,
        DefaultModel = "qwen2.5-coder:7b-instruct-q5_K_M"
    };
    private readonly WorkerInferenceNode _sut;

    public WorkerInferenceNodeTests()
    {
        _sut = new WorkerInferenceNode("W", _config, _client, _logger);
    }

    [Test]
    public void NodeId_MatchesConstructorArgument()
    {
        _sut.NodeId.Should().Be("W");
    }

    [Test]
    public void Provider_IsWorker()
    {
        _sut.Provider.Should().Be(NodeProviderType.Worker);
    }

    [Test]
    public void Capabilities_ReflectConfig()
    {
        _sut.Capabilities.NodeId.Should().Be("W");
        _sut.Capabilities.Model.Should().Be("qwen2.5-coder:7b-instruct-q5_K_M");
        _sut.Capabilities.VramMb.Should().Be(8192);
        _sut.Capabilities.SupportsStreaming.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteAsync_DelegatesToClient()
    {
        var request = new InferenceRequest { Prompt = "hello", Model = "qwen2.5-coder:7b-instruct-q5_K_M" };
        var expected = new InferenceResult { Text = "world", NodeId = "W", Model = "qwen2.5-coder:7b-instruct-q5_K_M" };

        _client.ExecuteAsync(request, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.ExecuteAsync(request);

        result.Should().Be(expected);
    }

    [Test]
    public async Task GetHealthAsync_ReturnsHealthy_WhenClientIsHealthy()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _client.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(new List<ModelInfo>
        {
            new ModelInfo { ModelId = "qwen2.5-coder:7b-instruct-q5_K_M" }
        });

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Healthy);
        health.AvailableModels.Should().Contain("qwen2.5-coder:7b-instruct-q5_K_M");
        health.VramTotalMB.Should().Be(8192);
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenClientUnreachable()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>())
               .Returns(false);

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
        health.AvailableModels.Should().BeEmpty();
    }

    [Test]
    public async Task GetHealthAsync_ReturnsUnavailable_WhenClientThrows()
    {
        _client.IsHealthyAsync(Arg.Any<CancellationToken>())
               .Returns(Task.FromException<bool>(new HttpRequestException("connection refused")));

        var health = await _sut.GetHealthAsync();

        health.State.Should().Be(HealthState.Unavailable);
        health.ErrorMessage.Should().Contain("connection refused");
    }

    [Test]
    public async Task StreamAsync_ReturnsSingleFinalChunk()
    {
        var request = new InferenceRequest { Prompt = "explain x", Model = "qwen2.5-coder:7b-instruct-q5_K_M" };
        var inferenceResult = new InferenceResult { Text = "explanation", NodeId = "W", Model = "qwen2.5-coder:7b-instruct-q5_K_M" };

        _client.ExecuteAsync(request, Arg.Any<CancellationToken>()).Returns(inferenceResult);

        var chunks = new List<InferenceChunk>();
        await foreach (var chunk in _sut.StreamAsync(request))
            chunks.Add(chunk);

        chunks.Should().HaveCount(1);
        chunks[0].IsFinal.Should().BeTrue();
        chunks[0].Content.Should().Be("explanation");
        chunks[0].FinalResult.Should().Be(inferenceResult);
    }

    [Test]
    public async Task ListModelsAsync_DelegatesToClient()
    {
        var models = new List<ModelInfo>
        {
            new() { ModelId = "model-a" },
            new() { ModelId = "model-b" }
        };
        _client.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(models);

        var result = await _sut.ListModelsAsync();

        result.Should().HaveCount(2);
        result.Select(m => m.ModelId).Should().BeEquivalentTo(new[] { "model-a", "model-b" });
    }
}
