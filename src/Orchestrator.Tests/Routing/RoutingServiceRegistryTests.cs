using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Health;
using Orchestrator.Infrastructure.Queue;
using Orchestrator.Infrastructure.Routing;

namespace Orchestrator.Tests.Routing;

/// <summary>
/// Tests for <see cref="RoutingService"/> using the registry-based primary constructor
/// (INodeRegistry + Func&lt;string, IInferenceQueue&gt;). These complement
/// <see cref="ScoringFunctionTests"/> which tests the compat constructor path.
/// </summary>
public sealed class RoutingServiceRegistryTests
{
    // ── shared fixtures ───────────────────────────────────────────────────────
    private readonly IInferenceNode _fastNode  = Substitute.For<IInferenceNode>();
    private readonly IInferenceNode _deepNode  = Substitute.For<IInferenceNode>();
    private readonly IInferenceNode _hybridNode = Substitute.For<IInferenceNode>();
    private readonly ILogger<RoutingService> _logger = Substitute.For<ILogger<RoutingService>>();

    public RoutingServiceRegistryTests()
    {
        _fastNode.NodeId.Returns("FAST");
        _fastNode.Capabilities.Returns(new NodeCapabilities { NodeId = "FAST", VramMb = 8192, Model = "fast-model" });

        _deepNode.NodeId.Returns("DEEP");
        _deepNode.Capabilities.Returns(new NodeCapabilities { NodeId = "DEEP", VramMb = 16384, Model = "deep-model" });

        _hybridNode.NodeId.Returns("HYBRID");
        _hybridNode.Capabilities.Returns(new NodeCapabilities { NodeId = "HYBRID", VramMb = 12288, Model = "hybrid-model" });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static NodeConfiguration MakeConfig(string nodeId, NodeRole role,
        NodeProviderType provider = NodeProviderType.Ollama) => new()
    {
        NodeId      = nodeId,
        DisplayName = $"Node {nodeId}",
        Provider    = provider,
        Role        = role,
    };

    private static NodeRegistration MakeReg(IInferenceNode node, NodeRole role,
        NodeProviderType provider = NodeProviderType.Ollama) =>
        new() { Config = MakeConfig(node.NodeId, role, provider), Node = node };

    private INodeRegistry RegistryWith(params NodeRegistration[] registrations)
    {
        var registry = Substitute.For<INodeRegistry>();
        registry.GetAllNodes().Returns(registrations.ToList());
        return registry;
    }

    private RoutingService BuildSut(
        INodeRegistry registry,
        INodeHealthCache? healthCache = null,
        RoutingOptions? options = null)
    {
        var opts = options is null
            ? null
            : Options.Create(options);

        return new RoutingService(
            registry:     registry,
            queueFactory: nodeId => new NodeQueue(capacity: 32),
            logger:       _logger,
            healthCache:  healthCache,
            routingOptions: opts);
    }

    // ── registry contract ─────────────────────────────────────────────────────

    [Test]
    public void Constructor_WhenRegistryIsNull_Throws()
    {
        var act = () => new RoutingService(
            registry:     null!,
            queueFactory: _ => new NodeQueue(32),
            logger:       _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registry");
    }

    [Test]
    public void Constructor_WhenQueueFactoryIsNull_Throws()
    {
        var act = () => new RoutingService(
            registry:     RegistryWith(MakeReg(_fastNode, NodeRole.Fast)),
            queueFactory: null!,
            logger:       _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("queueFactory");
    }

    [Test]
    public async Task RouteAsync_WhenNoNodesRegistered_ThrowsInvalidOperation()
    {
        var registry = RegistryWith(); // empty
        var sut      = BuildSut(registry);
        var request  = new InferenceRequest { Prompt = "test" };

        var act = async () => await sut.RouteAsync(TaskType.Chat, request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RouteAsync_SingleFastNode_AlwaysRoutesToIt()
    {
        var reg     = MakeReg(_fastNode, NodeRole.Fast);
        var sut     = BuildSut(RegistryWith(reg));
        var request = new InferenceRequest { Prompt = "hello" };
        var result  = new InferenceResult { Text = "hi", NodeId = "FAST" };

        _fastNode.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>()).Returns(result);

        var actual = await sut.RouteAsync(TaskType.Chat, request);

        actual.Should().BeEquivalentTo(result);
        await _fastNode.Received(1).ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>());
    }

    // ── role-based hard rules §6.3 ────────────────────────────────────────────

    [Test]
    public void SelectNode_Autocomplete_PrefersFastNode()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest { Prompt = "auto" };

        var node = sut.SelectNode(TaskType.Autocomplete, request);

        node.NodeId.Should().Be("FAST");
    }

    [Test]
    public void SelectNode_Autocomplete_WhenNoFastNode_ReturnsAnyNode()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest { Prompt = "auto" };

        var node = sut.SelectNode(TaskType.Autocomplete, request);

        node.NodeId.Should().Be("DEEP"); // only available — should not throw
    }

    // ── role-based scoring §6.2 ───────────────────────────────────────────────

    [Test]
    public void SelectNode_ReviewTask_PrefersDeepOverFast()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest { Prompt = "review this code" };

        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().Be("DEEP");
    }

    [Test]
    public void SelectNode_RefactorTask_PrefersDeepOverFast()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest { Prompt = "refactor this" };

        var node = sut.SelectNode(TaskType.Refactor, request);

        node.NodeId.Should().Be("DEEP");
    }

    [Test]
    public void SelectNode_TestGenerationTask_PrefersDeepOverFast()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest { Prompt = "generate tests for this" };

        var node = sut.SelectNode(TaskType.TestGeneration, request);

        node.NodeId.Should().Be("DEEP");
    }

    [Test]
    public void SelectNode_ChatTask_PrefersFastOverDeep()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest { Prompt = "chat message" };

        var node = sut.SelectNode(TaskType.Chat, request);

        node.NodeId.Should().Be("FAST");
    }

    [Test]
    public void SelectNode_ReviewTask_HybridPreferredOverFast()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_hybridNode, NodeRole.Hybrid)));
        var request = new InferenceRequest { Prompt = "review" };

        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().Be("HYBRID");
    }

    [Test]
    public void SelectNode_StandbyNode_NeverChosenWhenBetterAvailable()
    {
        var standbyNode = Substitute.For<IInferenceNode>();
        standbyNode.NodeId.Returns("STANDBY");
        standbyNode.Capabilities.Returns(new NodeCapabilities { NodeId = "STANDBY", VramMb = 8192 });

        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(standbyNode, NodeRole.Standby)));
        var request = new InferenceRequest { Prompt = "chat" };

        var node = sut.SelectNode(TaskType.Chat, request);

        node.NodeId.Should().Be("FAST");
    }

    // ── health cache integration ──────────────────────────────────────────────

    [Test]
    public void SelectNode_UnavailableNodeSkipped_RoutesToHealthyAlternative()
    {
        var cache = new InMemoryNodeHealthCache();
        cache.Set(new NodeHealth
        {
            NodeId = "DEEP",
            Status = NodeStatus.Unavailable,
            CheckedAt = DateTimeOffset.UtcNow
        });

        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)), cache);
        var request = new InferenceRequest { Prompt = "review code deeply" };

        // Even though Review prefers Deep, DEEP is unavailable → falls back to FAST
        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().Be("FAST");
    }

    [Test]
    public void SelectNode_AllUnavailableInCache_StillReturnsANode()
    {
        var cache = new InMemoryNodeHealthCache();
        cache.Set(new NodeHealth { NodeId = "FAST", Status = NodeStatus.Unavailable, CheckedAt = DateTimeOffset.UtcNow });
        cache.Set(new NodeHealth { NodeId = "DEEP", Status = NodeStatus.Unavailable, CheckedAt = DateTimeOffset.UtcNow });

        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)), cache);
        var request = new InferenceRequest { Prompt = "chat" };

        // Should not throw — must return something as fallback
        var node = sut.SelectNode(TaskType.Chat, request);

        node.Should().NotBeNull();
    }

    // ── CopilotSdk provider VRAM handling ─────────────────────────────────────

    [Test]
    public void SelectNode_CopilotSdkProvider_TreatedAsPartiallyAvailable()
    {
        var copilotNode = Substitute.For<IInferenceNode>();
        copilotNode.NodeId.Returns("COPILOT");
        copilotNode.Capabilities.Returns(new NodeCapabilities { NodeId = "COPILOT", VramMb = 0 });

        // Copilot vs a heavily loaded local Hybrid
        var sut = BuildSut(RegistryWith(
            MakeReg(copilotNode, NodeRole.Hybrid, NodeProviderType.CopilotSdk),
            MakeReg(_hybridNode, NodeRole.Hybrid)));

        var request = new InferenceRequest { Prompt = "review" };

        // Should not throw; copilot uses 0.75 synthetic VRAM ratio
        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().BeOneOf("COPILOT", "HYBRID");
    }

    // ── fallback chain ────────────────────────────────────────────────────────

    [Test]
    public async Task RouteAsync_WhenPrimaryFails_FallbackChainUsed()
    {
        var options = new RoutingOptions
        {
            FallbackChains = new Dictionary<string, List<string>>
            {
                ["DEEP"] = ["FAST"]
            }
        };

        var registry = RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep));
        var sut      = BuildSut(registry, options: options);
        var request  = new InferenceRequest { Prompt = "review this" };
        var fallbackResult = new InferenceResult { Text = "fallback response", NodeId = "FAST" };

        // DEEP always throws; FAST succeeds
        _deepNode.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns<InferenceResult>(_ => throw new Exception("node offline"));
        _fastNode.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns(fallbackResult);

        var result = await sut.RouteAsync(TaskType.Review, request);

        result.NodeId.Should().Be("FAST");
    }

    // ── queue overload threshold ──────────────────────────────────────────────

    [Test]
    public void SelectNode_QueueOverloaded_FallsBackToLessLoadedNode()
    {
        // Create a real NodeQueue and fill it past the threshold (>2 items)
        var deepQueue  = new NodeQueue(capacity: 32);
        var fastQueue  = new NodeQueue(capacity: 32);

        // Enqueue dummy items to overflow the deep queue past threshold (QueueFallbackThreshold = 2)
        for (var i = 0; i < 5; i++)
            deepQueue.TryEnqueue(new InferenceQueueItem
            {
                Request  = new InferenceRequest { Prompt = $"item {i}" },
                TaskType = TaskType.Review
            });

        var registry = RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep));

        // Use explicit queue factory that returns the pre-filled queues
        var sut = new RoutingService(
            registry:     registry,
            queueFactory: nodeId => nodeId == "DEEP" ? deepQueue : fastQueue,
            logger:       _logger);

        var request = new InferenceRequest { Prompt = "review" };

        // Deep is preferred for Review, but its queue is overloaded → FAST wins
        var node = sut.SelectNode(TaskType.Review, request);

        node.NodeId.Should().Be("FAST");
    }

    // ── large-context routing ─────────────────────────────────────────────────

    [Test]
    public void SelectNode_LargePrompt_PrefersDeepOrHybridNode()
    {
        var sut     = BuildSut(RegistryWith(MakeReg(_fastNode, NodeRole.Fast), MakeReg(_deepNode, NodeRole.Deep)));
        var request = new InferenceRequest
        {
            // ~5500 tokens via word-count heuristic (>5000 threshold)
            Prompt = string.Concat(Enumerable.Repeat("word ", 5500))
        };

        var node = sut.SelectNode(TaskType.Chat, request);

        // Large context favours Deep node even for Chat
        node.NodeId.Should().Be("DEEP");
    }

    // ── cancellation ─────────────────────────────────────────────────────────

    [Test]
    public async Task RouteAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var registry = RegistryWith(MakeReg(_fastNode, NodeRole.Fast));
        var sut      = BuildSut(registry);
        var request  = new InferenceRequest { Prompt = "test" };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _fastNode.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns<InferenceResult>(_ => throw new OperationCanceledException());

        var act = async () => await sut.RouteAsync(TaskType.Chat, request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
