using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Routing;

namespace Orchestrator.Tests.Routing;

[TestFixture]
public class FallbackChainResolverTests
{
    private INodeRegistry _registry = null!;
    private IModelRegistry _modelRegistry = null!;
    private FallbackChainResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _registry      = Substitute.For<INodeRegistry>();
        _modelRegistry = Substitute.For<IModelRegistry>();
        _resolver      = new FallbackChainResolver(
            _registry, _modelRegistry,
            NullLogger<FallbackChainResolver>.Instance);
    }

    private static NodeRegistration MakeRegistration(
        string nodeId,
        IInferenceNode node,
        HealthState health = HealthState.Healthy) =>
        new()
        {
            Config = new NodeConfiguration
            {
                NodeId      = nodeId,
                DisplayName = nodeId,
                Provider    = NodeProviderType.Ollama,
                Role        = NodeRole.Fast,
                Enabled     = true
            },
            Node       = node,
            LastHealth = new NodeHealthStatus { State = health, LastChecked = DateTimeOffset.UtcNow }
        };

    private static IInferenceNode MockNode(string nodeId, string returnText = "ok")
    {
        var node = Substitute.For<IInferenceNode>();
        node.NodeId.Returns(nodeId);
        node.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InferenceResult { Text = returnText, NodeId = nodeId });
        node.DisposeAsync().Returns(ValueTask.CompletedTask);
        return node;
    }

    private static FallbackChainConfig MakeChain(TaskType task, params string[] modelIds) =>
        new()
        {
            TaskType = task,
            Steps    = modelIds.Select(m => new FallbackStep { ModelId = m }).ToList()
        };

    [Test]
    public async Task ExecuteAsync_FirstStepSucceeds_ReturnsResult()
    {
        var node = MockNode("A", "response text");
        _registry.GetAllNodes().Returns([MakeRegistration("A", node)]);
        _modelRegistry.GetAvailableModels("A").Returns(["model1"]);

        var result = await _resolver.ExecuteAsync(
            TaskType.Chat,
            new InferenceRequest { Prompt = "hello" },
            MakeChain(TaskType.Chat, "model1"));

        result.Text.Should().Be("response text");
    }

    [Test]
    public async Task ExecuteAsync_FirstStepFails_TriesSecondStep()
    {
        var nodeA = Substitute.For<IInferenceNode>();
        nodeA.NodeId.Returns("A");
        nodeA.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Node A down"));
        nodeA.DisposeAsync().Returns(ValueTask.CompletedTask);

        var nodeB = MockNode("B", "fallback response");

        _registry.GetAllNodes().Returns([
            MakeRegistration("A", nodeA),
            MakeRegistration("B", nodeB)
        ]);
        _modelRegistry.GetAvailableModels("A").Returns(["model1"]);
        _modelRegistry.GetAvailableModels("B").Returns(["model2"]);

        var chain = new FallbackChainConfig
        {
            TaskType = TaskType.Chat,
            Steps =
            [
                new FallbackStep { ModelId = "model1", PreferredNodeIds = ["A"] },
                new FallbackStep { ModelId = "model2", PreferredNodeIds = ["B"] }
            ]
        };

        var result = await _resolver.ExecuteAsync(
            TaskType.Chat,
            new InferenceRequest { Prompt = "hello" },
            chain);

        result.Text.Should().Be("fallback response");
    }

    [Test]
    public async Task ExecuteAsync_NodeUnhealthy_SkipsStep()
    {
        var node = MockNode("A");
        _registry.GetAllNodes().Returns([MakeRegistration("A", node, HealthState.Unavailable)]);
        _modelRegistry.GetAvailableModels("A").Returns(["model1"]);

        var act = async () => await _resolver.ExecuteAsync(
            TaskType.Chat,
            new InferenceRequest { Prompt = "hi" },
            MakeChain(TaskType.Chat, "model1"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All fallback steps*");
    }

    [Test]
    public async Task ExecuteAsync_ModelNotAvailableOnNode_SkipsStep()
    {
        var node = MockNode("A");
        _registry.GetAllNodes().Returns([MakeRegistration("A", node)]);
        _modelRegistry.GetAvailableModels("A").Returns([]);  // node has no models

        var act = async () => await _resolver.ExecuteAsync(
            TaskType.Chat,
            new InferenceRequest { Prompt = "hi" },
            MakeChain(TaskType.Chat, "model1"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteAsync_AllStepsFail_ThrowsInvalidOperation()
    {
        var node = Substitute.For<IInferenceNode>();
        node.NodeId.Returns("A");
        node.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("fail"));
        node.DisposeAsync().Returns(ValueTask.CompletedTask);

        _registry.GetAllNodes().Returns([MakeRegistration("A", node)]);
        _modelRegistry.GetAvailableModels("A").Returns(["m1", "m2"]);

        var act = async () => await _resolver.ExecuteAsync(
            TaskType.Chat,
            new InferenceRequest { Prompt = "hi" },
            MakeChain(TaskType.Chat, "m1", "m2"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All fallback steps*");
    }

    [Test]
    public async Task ExecuteAsync_RespectsCancellation()
    {
        _registry.GetAllNodes().Returns([]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _resolver.ExecuteAsync(
            TaskType.Chat,
            new InferenceRequest { Prompt = "hi" },
            MakeChain(TaskType.Chat, "m1"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
