using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Routing;

namespace Orchestrator.Tests.Routing;

public sealed class RoutingServiceTests
{
    private readonly IInferenceNode _nodeA = Substitute.For<IInferenceNode>();
    private readonly ILogger<RoutingService> _logger = Substitute.For<ILogger<RoutingService>>();
    private readonly NodeQueue _queueA = new(capacity: 8);
    private readonly RoutingService _sut;

    public RoutingServiceTests()
    {
        _nodeA.NodeId.Returns("A");
        _sut = new RoutingService(_nodeA, _queueA, _logger);
    }

    [Fact]
    public async Task RouteAsync_ForwardsRequestToNodeA()
    {
        var request = new InferenceRequest { Prompt = "review this", Model = "qwen2.5-coder:7b-instruct-q4_K_M" };
        var expected = new InferenceResult { Text = "looks good", NodeId = "A", Model = "qwen2.5-coder:7b-instruct-q4_K_M", LatencyMs = 100 };

        _nodeA.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.RouteAsync(TaskType.Review, request);

        result.Should().BeEquivalentTo(expected);
        await _nodeA.Received(1).ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_AutocompleteTask_RoutesToNodeA()
    {
        var request = new InferenceRequest { Prompt = "complete this", Model = "qwen2.5-coder:7b-instruct-q4_K_M" };
        var expected = new InferenceResult { Text = "completion", NodeId = "A", Model = "qwen2.5-coder:7b-instruct-q4_K_M", LatencyMs = 50 };

        _nodeA.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.RouteAsync(TaskType.Autocomplete, request);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RouteAsync_PassesCancellationTokenThrough()
    {
        using var cts = new CancellationTokenSource();
        var request = new InferenceRequest { Prompt = "test", Model = "qwen2.5-coder:7b-instruct-q4_K_M" };
        var expected = new InferenceResult { Text = "ok", NodeId = "A", Model = "qwen2.5-coder:7b-instruct-q4_K_M" };

        _nodeA.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.RouteAsync(TaskType.Chat, request, cts.Token);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RouteAsync_LargePrompt_RoutesToNodeA_WhenNoBIsRegistered()
    {
        // No Node B registered — all traffic must go to A regardless of context size
        var bigPrompt = new string('x', 5_001 * 4); // > 5k token estimate
        var request = new InferenceRequest { Prompt = bigPrompt };
        var expected = new InferenceResult { Text = "ok", NodeId = "A", Model = "m" };

        _nodeA.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.RouteAsync(TaskType.Review, request);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RouteAsync_WithNodeB_LargePrompt_RoutesToNodeB()
    {
        var nodeB = Substitute.For<IInferenceNode>();
        nodeB.NodeId.Returns("B");
        var queueB = new NodeQueue(capacity: 8);
        var sut = new RoutingService(_nodeA, _queueA, _logger, nodeB, queueB);

        var bigPrompt = new string('x', 5_001 * 4);
        var request = new InferenceRequest { Prompt = bigPrompt };
        var expected = new InferenceResult { Text = "deep result", NodeId = "B", Model = "m" };

        nodeB.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await sut.RouteAsync(TaskType.Review, request);

        result.Should().BeEquivalentTo(expected);
        await nodeB.Received(1).ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>());
    }
}
