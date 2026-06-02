using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Configuration;

namespace Orchestrator.Tests.Infrastructure;

public sealed class TopologyRoutingSaveCoordinatorTests
{
    private INodeRegistry _nodeRegistry = null!;
    private IRoutingOptionsPersistence _routingPersistence = null!;
    private IOptionsMonitor<RoutingOptions> _routingOptionsMonitor = null!;
    private TopologyRoutingSaveCoordinator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _nodeRegistry = Substitute.For<INodeRegistry>();
        _routingPersistence = Substitute.For<IRoutingOptionsPersistence>();
        _routingOptionsMonitor = Substitute.For<IOptionsMonitor<RoutingOptions>>();

        _routingOptionsMonitor.CurrentValue.Returns(new RoutingOptions
        {
            FallbackChains = new Dictionary<string, List<string>>
            {
                ["A"] = ["C"]
            }
        });

        var nodes = new List<NodeRegistration>
        {
            CreateRegistration("A", Core.Configuration.NodeProviderType.Ollama, Core.Configuration.NodeRole.Fast),
            CreateRegistration("B", Core.Configuration.NodeProviderType.Ollama, Core.Configuration.NodeRole.Deep),
            CreateRegistration("C", Core.Configuration.NodeProviderType.CopilotSdk, Core.Configuration.NodeRole.Hybrid)
        };
        _nodeRegistry.GetAllNodes().Returns(nodes);

        _sut = new TopologyRoutingSaveCoordinator(
            _nodeRegistry,
            _routingPersistence,
            _routingOptionsMonitor);
    }

    [Test]
    public async Task SaveAsync_WhenAllSavesSucceed_PersistsRoutingThenTopology()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["B"] = ["A"]
        };

        await _sut.SaveAsync(chains);

        await _routingPersistence.Received(1)
            .SaveFallbackChainsAsync(
                Arg.Is<IReadOnlyDictionary<string, List<string>>>(x => x.ContainsKey("B")),
                Arg.Any<CancellationToken>());

        await _nodeRegistry.Received(1).SaveTopologyAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void SaveAsync_WhenFallbackChainsAreNull_Throws()
    {
        Func<Task> act = async () => await _sut.SaveAsync(null!);

        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task SaveAsync_WhenTopologySaveFails_RollsBackRouting()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["B"] = ["A"]
        };

        _nodeRegistry
            .SaveTopologyAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disk full"));

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rolled back*");

        await _routingPersistence.Received(1)
            .SaveFallbackChainsAsync(
                Arg.Is<IReadOnlyDictionary<string, List<string>>>(x => x.ContainsKey("B")),
                Arg.Any<CancellationToken>());

        await _routingPersistence.Received(1)
            .SaveFallbackChainsAsync(
                Arg.Is<IReadOnlyDictionary<string, List<string>>>(x => x.ContainsKey("A") && x["A"].Contains("C")),
                CancellationToken.None);
    }

    [Test]
    public async Task SaveAsync_WhenCancelled_RollsBackRoutingAndThrows()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["B"] = ["A"]
        };

        _nodeRegistry
            .SaveTopologyAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException("cancelled"));

        Func<Task> act = async () => await _sut.SaveAsync(chains, new CancellationTokenSource().Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        await _routingPersistence.Received(1)
            .SaveFallbackChainsAsync(
                Arg.Is<IReadOnlyDictionary<string, List<string>>>(x => x.ContainsKey("A") && x["A"].Contains("C")),
                CancellationToken.None);
    }

    [Test]
    public void SaveAsync_WhenSourceNodeIsUnknown_Throws()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["Z"] = ["A"]
        };

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*source*not a registered node*");
    }

    [Test]
    public void SaveAsync_WhenTargetNodeIsUnknown_Throws()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["A"] = ["Z"]
        };

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*target*not a registered node*");
    }

    [Test]
    public void SaveAsync_WhenChainContainsSelfReference_Throws()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["A"] = ["A"]
        };

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot contain itself*");
    }

    [Test]
    public void SaveAsync_WhenTwoNodeCycleExists_Throws()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["A"]
        };

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Test]
    public void SaveAsync_WhenMultiNodeCycleExists_Throws()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = ["A"]
        };

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Test]
    public async Task SaveAsync_WhenGraphIsAcyclic_AllowsSave()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["A"] = ["B", "C"],
            ["B"] = ["C"],
            ["C"] = []
        };

        await _sut.SaveAsync(chains);

        await _routingPersistence.Received(1)
            .SaveFallbackChainsAsync(
                Arg.Is<IReadOnlyDictionary<string, List<string>>>(x => x.ContainsKey("A") && x.ContainsKey("B") && x.ContainsKey("C")),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveAsync_WhenCycleExists_ExceptionContainsCyclePath()
    {
        var chains = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["A"]
        };

        Func<Task> act = async () => await _sut.SaveAsync(chains);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*A -> B -> A*");
    }

    private static NodeRegistration CreateRegistration(
        string nodeId,
        Core.Configuration.NodeProviderType provider,
        Core.Configuration.NodeRole role)
    {
        return new NodeRegistration
        {
            Config = new Core.Configuration.NodeConfiguration
            {
                NodeId = nodeId,
                DisplayName = $"Node {nodeId}",
                Provider = provider,
                Role = role
            },
            Node = BuildNode(nodeId)
        };
    }

    private static IInferenceNode BuildNode(string nodeId)
    {
        var node = Substitute.For<IInferenceNode>();
        node.NodeId.Returns(nodeId);
        node.DisposeAsync().Returns(ValueTask.CompletedTask);
        return node;
    }
}
