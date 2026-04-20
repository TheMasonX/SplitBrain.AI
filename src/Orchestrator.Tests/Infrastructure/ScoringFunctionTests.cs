using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Routing;

namespace Orchestrator.Tests.Infrastructure;

/// <summary>
/// Tests for the §6.2 / §6.3 scoring and hard-rule logic inside <see cref="RoutingService"/>.
/// </summary>
public sealed class ScoringFunctionTests
{
    private readonly IInferenceNode _nodeA = Substitute.For<IInferenceNode>();
    private readonly IInferenceNode _nodeB = Substitute.For<IInferenceNode>();
    private readonly ILogger<RoutingService> _logger = Substitute.For<ILogger<RoutingService>>();

    public ScoringFunctionTests()
    {
        _nodeA.NodeId.Returns("A");
        _nodeA.Capabilities.Returns(new NodeCapabilities { NodeId = "A", VramMb = 8192, Model = "nodeA-model" });

        _nodeB.NodeId.Returns("B");
        _nodeB.Capabilities.Returns(new NodeCapabilities { NodeId = "B", VramMb = 8192, Model = "nodeB-model" });
    }

    private RoutingService BuildSut(INodeHealthCache? cache = null) =>
        new(
            nodeA: _nodeA,
            nodeAQueue: new NodeQueue(64),
            logger: _logger,
            nodeB: _nodeB,
            nodeBQueue: new NodeQueue(32),
            healthCache: cache);

    // -----------------------------------------------------------------------
    // Hard rules §6.3
    // -----------------------------------------------------------------------

    [Fact]
    public void SelectNode_Autocomplete_AlwaysReturnsNodeA()
    {
        var sut = BuildSut();
        var request = new InferenceRequest { Prompt = "auto" };

        var node = sut.SelectNode(TaskType.Autocomplete, request);

        node.NodeId.Should().Be("A");
    }

    [Fact]
    public void SelectNode_WhenNodeBUnavailableInCache_ReturnsNodeA()
    {
        var cache = new InMemoryNodeHealthCache();
        cache.Set(new NodeHealth
        {
            NodeId          = "B",
            Status          = NodeStatus.Unavailable,
            QueueDepth      = 0,
            AvailableVramMb = 0,
            CheckedAt       = DateTimeOffset.UtcNow
        });

        var sut = BuildSut(cache);
        var request = new InferenceRequest { Prompt = "review this code thoroughly" };

        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().Be("A");
    }

    [Fact]
    public void SelectNode_NodeBHealthy_DeepTask_PrefersNodeB()
    {
        var cache = new InMemoryNodeHealthCache();
        cache.Set(new NodeHealth
        {
            NodeId          = "B",
            Status          = NodeStatus.Healthy,
            QueueDepth      = 0,
            AvailableVramMb = 7500,
            CheckedAt       = DateTimeOffset.UtcNow
        });
        cache.Set(new NodeHealth
        {
            NodeId          = "A",
            Status          = NodeStatus.Healthy,
            QueueDepth      = 0,
            AvailableVramMb = 7500,
            CheckedAt       = DateTimeOffset.UtcNow
        });

        var sut = BuildSut(cache);
        // Review is a deep task; Node B should win the scoring
        var request = new InferenceRequest { Prompt = new string('x', 20_000) }; // large context

        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().Be("B");
    }

    [Fact]
    public void SelectNode_NodeBQueueOverThreshold_FallsBackToNodeA()
    {
        var nodeBQueue = new NodeQueue(32);
        // Enqueue 3 items (threshold is 2)
        for (var i = 0; i < 3; i++)
            nodeBQueue.TryEnqueue(new InferenceQueueItem
            {
                Request  = new InferenceRequest { Prompt = "x" },
                TaskType = TaskType.Chat
            });

        var sut = new RoutingService(
            nodeA: _nodeA,
            nodeAQueue: new NodeQueue(64),
            logger: _logger,
            nodeB: _nodeB,
            nodeBQueue: nodeBQueue);

        var node = sut.SelectNode(TaskType.Review, new InferenceRequest { Prompt = "review" });

        node.NodeId.Should().Be("A");
    }

    [Fact]
    public void SelectNode_NullNodeB_AlwaysReturnsNodeA()
    {
        var sut = new RoutingService(
            nodeA: _nodeA,
            nodeAQueue: new NodeQueue(64),
            logger: _logger);

        var node = sut.SelectNode(TaskType.Review, new InferenceRequest { Prompt = "review" });

        node.NodeId.Should().Be("A");
    }
}
